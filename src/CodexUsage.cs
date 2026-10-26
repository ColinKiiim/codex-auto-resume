using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexAutoResume
{
    internal enum UsageError { None, NoTarget, NoExecutable, NotSignedIn, UnsupportedAuth, Timeout, Changed, Protocol, Unavailable }

    internal sealed class UsageWindow
    {
        internal double? Remaining;
        internal int? Minutes;
        internal DateTime? ResetUtc;
        internal string Label
        {
            get
            {
                if (!Minutes.HasValue) return "?";
                int minutes = Minutes.Value;
                if (minutes % 10080 == 0) return minutes == 10080 ? (UiText.English ? "wk" : "周") : (minutes / 10080) + "wk";
                if (minutes % 1440 == 0) return (minutes / 1440) + "d";
                if (minutes % 60 == 0) return (minutes / 60) + "h";
                return minutes + "m";
            }
        }
        internal string Percent { get { return Remaining.HasValue ? Math.Ceiling(Remaining.Value).ToString(CultureInfo.InvariantCulture) + "%" : "—"; } }
    }

    internal sealed class UsageSnapshot
    {
        internal string AccountKey, Account, Plan, Home, Executable, Bucket;
        internal bool Unlimited;
        internal DateTime FetchedUtc;
        internal UsageWindow[] Windows = new UsageWindow[0];
    }

    internal sealed class UsageResult
    {
        internal UsageError Error;
        internal string Stage;
        internal string AccountKey;
        internal UsageSnapshot Snapshot;
    }

    // One local login source, never an account collection. No credential contents are read.
    internal sealed class UsageSource
    {
        internal string Executable, Home, Key;
        internal string Arguments = "app-server --listen stdio://";
        internal static string HomePath()
        {
            string configured = Environment.GetEnvironmentVariable("CODEX_HOME");
            return Path.GetFullPath(String.IsNullOrWhiteSpace(configured) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex") : configured);
        }
        internal static string TargetKey(Target target)
        {
            if (target == null || target.Demo) return "";
            return target.ProcessId + "|" + target.ProcessStart + "|" + target.ProcessPath + "|" + HomePath();
        }
        internal static UsageSource Find(string targetPath, string key)
        {
            // The running target supplies the installation root, including versioned Store paths.
            // Do not fall back to another installation/profile when this target has no CLI.
            string directory = Path.GetDirectoryName(Path.GetFullPath(targetPath));
            List<string> roots = new List<string> { directory };
            DirectoryInfo parent = Directory.GetParent(directory);
            if (parent != null && String.Equals(Path.GetFileName(directory), "app", StringComparison.OrdinalIgnoreCase)) roots.Add(parent.FullName);
            foreach (string root in roots)
            foreach (string relative in new[] { "bin\\codex.exe", "resources\\codex.exe", "app\\resources\\codex.exe" })
            {
                string candidate = Path.Combine(root, relative);
                if (File.Exists(candidate) && !String.Equals(candidate, targetPath, StringComparison.OrdinalIgnoreCase))
                    return new UsageSource { Executable = candidate, Home = HomePath(), Key = key };
            }
            return null;
        }
        internal string AuthStamp()
        {
            // Detect concurrent login changes without opening either credential file.
            return Stamp(Path.Combine(Home, "auth.json")) + "|" + Stamp(Path.Combine(Home, "secrets", "codex_auth.age"));
        }
        static string Stamp(string path)
        {
            FileInfo file = new FileInfo(path);
            return file.Exists ? file.LastWriteTimeUtc.Ticks + ":" + file.Length : "missing";
        }
    }

    internal static class UsageJson
    {
        internal static Dictionary<string, object> Map(object value) { return value as Dictionary<string, object>; }
        internal static object Get(object value, string key) { var map = Map(value); object result; return map != null && map.TryGetValue(key, out result) ? result : null; }
        internal static string Text(object value, string key) { return Convert.ToString(Get(value, key), CultureInfo.InvariantCulture); }
        internal static bool IsTrue(object value) { return value is bool && (bool)value; }
        internal static double? Number(object value)
        {
            if (value == null || value is bool) return null;
            double result;
            return Double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out result) && !Double.IsNaN(result) && !Double.IsInfinity(result) ? (double?)result : null;
        }
        internal static string Identity(object account)
        {
            return String.Join("|", new[] { "type", "id", "accountId", "chatgptAccountId", "workspaceId", "email", "planType" }.Select(key => Text(account, key)).ToArray());
        }
        internal static UsageWindow Window(object value)
        {
            if (Map(value) == null) return null;
            var used = Number(Get(value, "usedPercent")); var minutes = Number(Get(value, "windowDurationMins")); var reset = Number(Get(value, "resetsAt"));
            var result = new UsageWindow();
            if (used.HasValue && used >= 0 && used <= 100) result.Remaining = 100 - used.Value;
            if (minutes.HasValue && minutes > 0 && minutes <= Int32.MaxValue && minutes == Math.Floor(minutes.Value)) result.Minutes = (int)minutes.Value;
            if (reset.HasValue)
                try { result.ResetUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(reset.Value); } catch (ArgumentOutOfRangeException) { }
            return result;
        }
        internal static UsageSnapshot Parse(object limits, object account)
        {
            object bucket = null;
            var buckets = Map(Get(limits, "rateLimitsByLimitId"));
            if (buckets != null)
            {
                if (!buckets.TryGetValue("codex", out bucket))
                {
                    if (buckets.Count == 1) bucket = buckets.Values.First();
                    // Multiple unrelated buckets must not be arbitrarily combined.
                }
            }
            if (bucket == null && (buckets == null || buckets.Count == 0)) bucket = Get(limits, "rateLimits");
            if (Map(bucket) == null) throw new InvalidDataException();
            var windows = new[] { Window(Get(bucket, "primary")), Window(Get(bucket, "secondary")) }.Where(window => window != null).ToArray();
            bool unlimited = IsTrue(Get(Get(bucket, "credits"), "unlimited"));
            return new UsageSnapshot { AccountKey = Identity(account), Account = Text(account, "email"), Plan = Text(account, "planType"),
                Bucket = Text(bucket, "limitId"), Windows = windows, Unlimited = unlimited, FetchedUtc = DateTime.UtcNow };
        }
    }

    internal sealed class UsageJob : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] struct BasicLimits
        {
            public long ProcessTime, JobTime;
            public uint Flags;
            public UIntPtr MinWorkingSet, MaxWorkingSet;
            public uint ActiveProcesses;
            public UIntPtr Affinity;
            public uint Priority, Scheduling;
        }
        [StructLayout(LayoutKind.Sequential)] struct IoCounters { public ulong A, B, C, D, E, F; }
        [StructLayout(LayoutKind.Sequential)] struct ExtendedLimits
        {
            public BasicLimits Basic;
            public IoCounters Io;
            public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetInformationJobObject(IntPtr job, int type, ref ExtendedLimits limits, int size);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        IntPtr handle;
        internal UsageJob()
        {
            handle = CreateJobObject(IntPtr.Zero, null);
            var limits = new ExtendedLimits(); limits.Basic.Flags = 0x2000; // KILL_ON_JOB_CLOSE
            if (handle == IntPtr.Zero || !SetInformationJobObject(handle, 9, ref limits, Marshal.SizeOf(limits))) { Dispose(); throw new InvalidOperationException(); }
        }
        internal void Add(Process process) { if (!AssignProcessToJobObject(handle, process.Handle)) throw new InvalidOperationException(); }
        public void Dispose() { IntPtr owned = Interlocked.Exchange(ref handle, IntPtr.Zero); if (owned != IntPtr.Zero) CloseHandle(owned); }
    }

    internal sealed class UsageRpc : IDisposable
    {
        readonly Process process;
        readonly UsageJob job = new UsageJob();
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };
        int nextId;
        internal bool TrackAccountChanges;
        internal UsageRpc(UsageSource source)
        {
            var info = new ProcessStartInfo(source.Executable, source.Arguments) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = Path.GetDirectoryName(source.Executable) };
            info.EnvironmentVariables["CODEX_HOME"] = source.Home;
            process = new Process { StartInfo = info };
            try
            {
                process.Start();
                job.Add(process);
                process.ErrorDataReceived += delegate { }; // Drain continuously; never retain raw diagnostics or secrets.
                process.BeginErrorReadLine();
            }
            catch { Stop(); process.Dispose(); throw; }
        }
        internal void Stop()
        {
            job.Dispose();
            try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
        }
        internal async Task<object> Request(string method, object parameters, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            int id = ++nextId;
            await Send(new { id = id, method = method, @params = parameters }).ConfigureAwait(false);
            for (int skipped = 0; skipped < 1024; skipped++)
            {
                string line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
                cancel.ThrowIfCancellationRequested();
                if (line == null || line.Length > 1024 * 1024) throw new InvalidDataException();
                var message = json.DeserializeObject(line);
                object responseId = UsageJson.Get(message, "id");
                string requestMethod = UsageJson.Text(message, "method");
                if (requestMethod.Length > 0)
                {
                    if (responseId != null) await Send(new { id = responseId, error = new { code = -32601, message = "Read-only usage client" } }).ConfigureAwait(false);
                    if (TrackAccountChanges && requestMethod == "account/updated") throw new UsageNotificationException();
                    continue;
                }
                if (UsageJson.Number(responseId) != id) continue;
                if (UsageJson.Get(message, "error") != null) throw new InvalidDataException();
                return UsageJson.Get(message, "result");
            }
            throw new InvalidDataException();
        }
        internal Task Send(object message) { return process.StandardInput.WriteLineAsync(json.Serialize(message)); }
        public void Dispose()
        {
            Stop();
            try { process.WaitForExit(2000); } catch (InvalidOperationException) { }
            process.Dispose();
        }
    }

    internal class UsageChangedException : Exception { }
    internal sealed class UsageNotificationException : UsageChangedException { }

    internal static class CodexUsage
    {
        internal static async Task<UsageResult> ReadAsync(UsageSource source, CancellationToken cancel, int timeoutMilliseconds = 20000)
        {
            var result = new UsageResult();
            if (source == null) { result.Error = UsageError.NoExecutable; return result; }
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                timeout.CancelAfter(timeoutMilliseconds);
                try
                {
                    result.Stage = "start"; string stamp = source.AuthStamp();
                    using (var rpc = new UsageRpc(source))
                    using (timeout.Token.Register(rpc.Stop))
                    {
                        result.Stage = "initialize";
                        object init = await rpc.Request("initialize", new { clientInfo = new { name = "codexautoresume", title = "Codex Auto Resume", version = Portable.Version } }, timeout.Token).ConfigureAwait(false);
                        string actualHome = UsageJson.Text(init, "codexHome");
                        result.Stage = "home";
                        if (actualHome.Length == 0) throw new InvalidDataException();
                        if (!String.Equals(Path.GetFullPath(actualHome).TrimEnd('\\'), source.Home.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                            throw new UsageChangedException();
                        await rpc.Send(new { method = "initialized", @params = new { } }).ConfigureAwait(false);
                        string initialAccount = null;
                        for (int epoch = 0; epoch < 2; epoch++)
                        {
                            try
                            {
                                if (source.AuthStamp() != stamp) throw new UsageChangedException();
                                result.Stage = "account";
                                object first = UsageJson.Get(await rpc.Request("account/read", new { refreshToken = false }, timeout.Token).ConfigureAwait(false), "account");
                                if (first == null) { result.Error = UsageError.NotSignedIn; return result; }
                                if (UsageJson.Text(first, "type") != "chatgpt") { result.Error = UsageError.UnsupportedAuth; return result; }
                                result.AccountKey = UsageJson.Identity(first);
                                if (initialAccount == null) initialAccount = result.AccountKey;
                                else if (result.AccountKey != initialAccount) throw new UsageChangedException();
                                if (String.IsNullOrEmpty(UsageJson.Text(first, "email")) && String.IsNullOrEmpty(UsageJson.Text(first, "id"))) throw new UsageChangedException();
                                rpc.TrackAccountChanges = true; result.Stage = "limits";
                                object limits = await rpc.Request("account/rateLimits/read", null, timeout.Token).ConfigureAwait(false);
                                result.Stage = "recheck";
                                object last = UsageJson.Get(await rpc.Request("account/read", new { refreshToken = false }, timeout.Token).ConfigureAwait(false), "account");
                                timeout.Token.ThrowIfCancellationRequested();
                                if (UsageJson.Identity(last) != result.AccountKey || source.AuthStamp() != stamp) throw new UsageChangedException();
                                result.Stage = "parse"; var snapshot = UsageJson.Parse(limits, last);
                                snapshot.Home = source.Home; snapshot.Executable = source.Executable;
                                result.Snapshot = snapshot; break;
                            }
                            catch (UsageNotificationException)
                            {
                                if (epoch != 0) throw;
                                // A startup account/updated can arrive after the
                                // first account reply. Discard that entire epoch
                                // and any outstanding limits reply; never reuse
                                // it or ignore the notification. Re-read account,
                                // limits and account once on the same channel.
                                rpc.TrackAccountChanges = false; result.AccountKey = null;
                            }
                        }
                    }
                }
                catch (UsageChangedException) { result.Error = UsageError.Changed; }
                catch (Exception)
                {
                    if (cancel.IsCancellationRequested) throw new OperationCanceledException(cancel);
                    result.Error = timeout.IsCancellationRequested ? UsageError.Timeout : UsageError.Unavailable;
                }
            }
            return result;
        }
    }
}
