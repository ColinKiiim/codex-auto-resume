using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexAutoResume
{
    internal static class JsonFile
    {
        internal static T Read<T>(string path) { return new JavaScriptSerializer().Deserialize<T>(File.ReadAllText(path, Encoding.UTF8)); }
        internal static void Write(string path, object value)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(value), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    public sealed class UserPreferences
    {
        public bool CheckUpdates { get; set; }
        public bool Background { get; set; }
        public int CropSize { get; set; }
        public string SkippedVersion { get; set; }
        public string Language { get; set; }
        public bool QuotaControl { get; set; }
        public bool LimitScope { get; set; }
        public bool QuotaFiveHour { get; set; }
        public bool QuotaWeekly { get; set; }
        public int QuotaThreshold { get; set; }
        public int? QuotaFiveHourThreshold { get; set; }
        public int? QuotaWeeklyThreshold { get; set; }
        internal int FiveHourThreshold { get { return Math.Max(0, Math.Min(99, QuotaFiveHourThreshold ?? QuotaThreshold)); } }
        internal int WeeklyThreshold { get { return Math.Max(0, Math.Min(99, QuotaWeeklyThreshold ?? QuotaThreshold)); } }
        public string ContinuePrompt { get; set; }
        public UserPreferences() { CheckUpdates = true; Background = true; CropSize = 64; Language = "zh-CN"; QuotaFiveHour = QuotaWeekly = true; QuotaThreshold = 10; ContinuePrompt = "刚刚我中断了你的对话，请从上次中断处继续工作"; }
        internal static UserPreferences Load()
        {
            try
            {
                UserPreferences value = JsonFile.Read<UserPreferences>(Path.Combine(Portable.DataDirectory, "settings.json"));
                if (value == null) return new UserPreferences();
                value.Language = value.Language == "en" ? "en" : "zh-CN";
                value.QuotaThreshold = Math.Max(0, Math.Min(99, value.QuotaThreshold));
                value.QuotaFiveHourThreshold = value.FiveHourThreshold;
                value.QuotaWeeklyThreshold = value.WeeklyThreshold;
                if (!value.QuotaFiveHour && !value.QuotaWeekly) value.QuotaControl = false;
                if (!ComposerRequest.ValidPrompt(value.ContinuePrompt)) value.ContinuePrompt = new UserPreferences().ContinuePrompt;
                value.CropSize = Math.Max(36, Math.Min(160, value.CropSize)); return value;
            }
            catch { return new UserPreferences(); }
        }
        internal void Save()
        { Directory.CreateDirectory(Portable.DataDirectory); JsonFile.Write(Path.Combine(Portable.DataDirectory, "settings.json"), this); }
    }

    public sealed class ResumeMemory
    {
        public bool Monitoring { get; set; }
        public bool Automatic { get; set; }
        public bool Background { get; set; }
        public long Window { get; set; }
        public int ProcessId { get; set; }
        public long ProcessStart { get; set; }
        public string ProcessPath { get; set; }
        public bool AutoLocate { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int OffsetX { get; set; }
        public int OffsetY { get; set; }
        public int CropSize { get; set; }
        public uint Dpi { get; set; }
        public RegionMemory Region { get; set; }
        internal Target Reconnect()
        {
            if (Window == 0 || String.IsNullOrEmpty(ProcessPath)) return null;
            try
            {
                Target target = Target.FromWindow(new IntPtr(Window), false);
                if (target.ProcessId != ProcessId || target.ProcessStart != ProcessStart ||
                    !String.Equals(target.ProcessPath, ProcessPath, StringComparison.OrdinalIgnoreCase)) return null;
                target.Automatic = AutoLocate; target.Width = Width; target.Height = Height; target.OffsetX = OffsetX;
                target.OffsetY = OffsetY; target.CropSize = CropSize; target.Dpi = Dpi; target.selection.Restore(Region);
                string reason;
                return target.ValidateIdentity(out reason) ? target : null;
            }
            catch { return null; }
        }
    }

    internal sealed class AvailableUpdate
    {
        internal string Version, Tag, Notes, AssetName, DownloadUrl, ChecksumsUrl, Digest;
        internal DateTime PublishedAt;
    }

    internal sealed class ReleaseCheck
    {
        internal string PublishedVersion;
        internal AvailableUpdate Update;
        internal string Message(string current, bool english)
        {
            bool ahead = Updates.ParseVersion(current) > Updates.ParseVersion(PublishedVersion);
            return (english ? (ahead ? "This version is ahead of the published release" : "No newer published release is available") : (ahead ? "当前版本高于线上正式版" : "暂无更高版本的已发布更新")) +
                "\n\n" + (english ? "Current version: " : "当前版本：") + current + "\n" + (english ? "Published version: " : "线上正式版本：") + PublishedVersion + "\n" + (english ? "Source: GitHub stable releases" : "来源：GitHub 正式发布");
        }
    }

    public sealed class UpdateJob
    {
        public string Directory { get; set; }
        public string Destination { get; set; }
        public string Staged { get; set; }
        public string Backup { get; set; }
        public string OldHash { get; set; }
        public string NewHash { get; set; }
        public string Version { get; set; }
        public int ParentId { get; set; }
        public long ParentStart { get; set; }
        public ResumeMemory Resume { get; set; }
    }

    internal static partial class Updates
    {
        internal const string Repository = "ColinKiiim/codex-auto-resume";
        internal const string LatestUrl = "https://api.github.com/repos/" + Repository + "/releases/latest";
        internal static string UpdateRoot { get { return Path.Combine(Portable.DataDirectory, "updates"); } }
        internal static string Executable { get { return System.Reflection.Assembly.GetExecutingAssembly().Location; } }
        internal static string Hash(string path)
        { using (SHA256 hash = SHA256.Create()) using (Stream stream = File.OpenRead(path)) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        internal static Version ParseVersion(string text)
        {
            if (text == null || !Regex.IsMatch(text, @"^\d+\.\d+\.\d+$")) throw new InvalidDataException("版本号无效");
            return new Version(text);
        }
        internal static bool OfficialDownload(string url, string tag, string filename)
        {
            return String.Equals(url, "https://github.com/" + Repository + "/releases/download/" + tag + "/" + filename, StringComparison.Ordinal);
        }
        internal static AvailableUpdate ParseRelease(string json, string currentVersion)
        {
            Dictionary<string, object> data = new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024 }.Deserialize<Dictionary<string, object>>(json);
            if (data == null || !data.ContainsKey("draft") || !data.ContainsKey("prerelease") || (bool)data["draft"] || (bool)data["prerelease"]) return null;
            string tag = data["tag_name"] as string;
            if (String.IsNullOrEmpty(tag) || !tag.StartsWith("v", StringComparison.Ordinal)) throw new InvalidDataException("更新版本标签无效");
            string version = tag.Substring(1);
            if (ParseVersion(version) <= ParseVersion(currentVersion)) return null;
            string name = "CodexAutoResume-" + tag + "-windows-x64.exe";
            AvailableUpdate update = new AvailableUpdate { Version = version, Tag = tag, AssetName = name, Notes = data.ContainsKey("body") ? data["body"] as string : "" };
            DateTime published;
            if (data.ContainsKey("published_at") && DateTime.TryParse(data["published_at"] as string, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out published)) update.PublishedAt = published;
            object[] assets = data["assets"] as object[];
            IEnumerable list = assets != null ? (IEnumerable)assets : data["assets"] as IEnumerable;
            if (list == null) throw new InvalidDataException("新版缺少下载附件");
            foreach (object item in list)
            {
                Dictionary<string, object> asset = item as Dictionary<string, object>;
                if (asset == null) continue;
                string assetName = asset["name"] as string, url = asset["browser_download_url"] as string;
                if (assetName != name && assetName != "SHA256SUMS.txt") continue;
                if (!OfficialDownload(url, tag, assetName)) throw new InvalidDataException("更新附件地址不属于本项目的正式发布");
                if (assetName == name)
                {
                    if (update.DownloadUrl != null) throw new InvalidDataException("更新附件重复");
                    update.DownloadUrl = url;
                    update.Digest = asset.ContainsKey("digest") ? asset["digest"] as string : null;
                }
                else { if (update.ChecksumsUrl != null) throw new InvalidDataException("校验附件重复"); update.ChecksumsUrl = url; }
            }
            if (update.DownloadUrl == null || update.ChecksumsUrl == null) throw new InvalidDataException("新版尚未提供完整 EXE 与校验文件");
            return update;
        }
        internal static string ExpectedHash(string checksums, string filename)
        {
            string result = null;
            foreach (string line in checksums.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                Match match = Regex.Match(line, @"^([a-fA-F0-9]{64})  ([^\\/]+)$");
                if (!match.Success || match.Groups[2].Value != filename) continue;
                if (result != null) throw new InvalidDataException("EXE 校验条目重复");
                result = match.Groups[1].Value.ToLowerInvariant();
            }
            if (result == null) throw new InvalidDataException("找不到新版 EXE 的 SHA256 校验值");
            return result;
        }
        static HttpClient Client(TimeSpan timeout)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            HttpClient client = new HttpClient { Timeout = timeout };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CodexAutoResume/" + Portable.Version);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }
        internal static async Task<AvailableUpdate> CheckAsync(CancellationToken cancel)
        { return (await CheckLatestAsync(cancel).ConfigureAwait(false)).Update; }
        internal static ReleaseCheck ParseCheck(string json, string current)
        {
            Dictionary<string, object> data = new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024 }.Deserialize<Dictionary<string, object>>(json);
            string tag = data == null || !data.ContainsKey("tag_name") ? null : data["tag_name"] as string;
            if (String.IsNullOrEmpty(tag) || !tag.StartsWith("v") || !data.ContainsKey("draft") || !data.ContainsKey("prerelease") || (bool)data["draft"] || (bool)data["prerelease"])
                throw new InvalidDataException("更新源没有返回有效的正式版本");
            string version = tag.Substring(1); ParseVersion(version);
            return new ReleaseCheck { PublishedVersion = version, Update = ParseRelease(json, current) };
        }
        internal static async Task<ReleaseCheck> CheckLatestAsync(CancellationToken cancel)
        {
            using (HttpClient client = Client(TimeSpan.FromSeconds(25)))
            using (HttpResponseMessage response = await client.GetAsync(LatestUrl, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                using (Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (MemoryStream output = new MemoryStream())
                using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel))
                { deadline.CancelAfter(20000); await CopyBounded(input, output, 2 * 1024 * 1024, deadline.Token).ConfigureAwait(false); return ParseCheck(Encoding.UTF8.GetString(output.ToArray()), Portable.Version); }
            }
        }
        internal static async Task CopyBounded(Stream input, Stream output, long maximum, CancellationToken cancel)
        {
            // Disposing the response stream also aborts a pending read on older
            // .NET Framework network streams that do not honor read cancellation.
            using (cancel.Register(delegate { input.Dispose(); }))
            {
                try
                {
                    byte[] buffer = new byte[32768]; long total = 0;
                    while (true)
                    {
                        int count = await input.ReadAsync(buffer, 0, buffer.Length, cancel).ConfigureAwait(false);
                        if (count == 0) break;
                        total += count; if (total > maximum) throw new InvalidDataException("更新文件超出预期大小");
                        await output.WriteAsync(buffer, 0, count, cancel).ConfigureAwait(false);
                    }
                }
                catch { cancel.ThrowIfCancellationRequested(); throw; }
            }
        }
        internal static async Task<string> DownloadAsync(AvailableUpdate update, CancellationToken cancel)
        {
            if (!OfficialDownload(update.DownloadUrl, update.Tag, update.AssetName) || !OfficialDownload(update.ChecksumsUrl, update.Tag, "SHA256SUMS.txt")) throw new InvalidDataException("更新地址无效");
            string directory = Path.Combine(UpdateRoot, Guid.NewGuid().ToString("N")); System.IO.Directory.CreateDirectory(directory);
            string candidate = Path.Combine(directory, "candidate.exe");
            try
            {
                using (HttpClient client = Client(TimeSpan.FromMinutes(3)))
                using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel))
                {
                    deadline.CancelAfter(TimeSpan.FromMinutes(3));
                    string checksums;
                    using (HttpResponseMessage response = await client.GetAsync(update.ChecksumsUrl, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        using (Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (MemoryStream text = new MemoryStream())
                        { await CopyBounded(input, text, 65536, deadline.Token).ConfigureAwait(false); checksums = Encoding.UTF8.GetString(text.ToArray()); }
                    }
                    string expected = ExpectedHash(checksums, update.AssetName);
                    if (!String.IsNullOrEmpty(update.Digest) && update.Digest != "sha256:" + expected) throw new InvalidDataException("发布校验信息不一致");
                    using (HttpResponseMessage response = await client.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        using (Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (FileStream output = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None, 32768, true))
                            await CopyBounded(input, output, 64 * 1024 * 1024, deadline.Token).ConfigureAwait(false);
                    }
                    ValidateBinary(candidate, expected, update.Version);
                }
                return candidate;
            }
            catch { DeleteFile(candidate); throw; }
        }
        internal static void ValidateBinary(string path, string expectedHash, string version)
        {
            if (!Regex.IsMatch(expectedHash ?? "", "^[a-f0-9]{64}$") || Hash(path) != expectedHash) throw new InvalidDataException("更新文件 SHA256 校验失败，已保留当前版本");
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
            if (info.FileVersion != version + ".0" || info.ProductName != "CodexAutoResume") throw new InvalidDataException("更新文件的产品或版本不匹配");
            using (BinaryReader binary = new BinaryReader(File.OpenRead(path)))
            {
                if (binary.ReadUInt16() != 0x5a4d) throw new InvalidDataException("更新文件不是 Windows EXE");
                binary.BaseStream.Position = 0x3c; int offset = binary.ReadInt32();
                if (offset < 64 || offset > binary.BaseStream.Length - 6) throw new InvalidDataException("更新文件 PE 头无效");
                binary.BaseStream.Position = offset;
                if (binary.ReadUInt32() != 0x4550 || binary.ReadUInt16() != 0x8664) throw new InvalidDataException("更新文件不是 Windows x64 版本");
            }
        }
        internal static UpdateJob Prepare(string candidate, string version, ResumeMemory resume)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(candidate));
            string id = Path.GetFileName(directory);
            if (!Regex.IsMatch(id, "^[a-f0-9]{32}$") || !String.Equals(Path.GetDirectoryName(directory), UpdateRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新临时目录无效");
            string destination = Path.GetFullPath(Executable);
            if ((File.GetAttributes(destination) & FileAttributes.ReadOnly) != 0) throw new IOException("当前 EXE 为只读文件，请移到可写目录或手动更新");
            string prefix = Path.Combine(Path.GetDirectoryName(destination), ".CodexAutoResume-update-" + id);
            Process parent = Process.GetCurrentProcess();
            UpdateJob job = new UpdateJob { Directory = directory, Destination = destination, Staged = prefix + ".tmp", Backup = prefix + ".bak", OldHash = Hash(destination), NewHash = Hash(candidate),
                Version = version, ParentId = parent.Id, ParentStart = parent.StartTime.ToUniversalTime().Ticks, Resume = resume };
            ValidateBinary(candidate, job.NewHash, version);
            // Check write access before stopping the monitor. Never elevate.
            try
            {
                using (FileStream input = File.OpenRead(candidate))
                using (FileStream output = new FileStream(job.Staged, FileMode.CreateNew, FileAccess.Write, FileShare.None)) input.CopyTo(output);
                JsonFile.Write(Path.Combine(directory, "job.json"), job);
                // Use the already verified candidate's helper implementation,
                // so future releases can carry their own updater fixes.
                File.Copy(candidate, Path.Combine(directory, "updater.exe"));
                return job;
            }
            catch { DeleteFile(job.Staged); throw; }
        }
        internal static Process StartHelper(UpdateJob job)
        {
            string ready = Path.Combine(job.Directory, "helper-ready");
            if (File.Exists(ready)) File.Delete(ready);
            Process helper = Process.Start(new ProcessStartInfo(Path.Combine(job.Directory, "updater.exe"), "--apply-update " + Quote(Path.Combine(job.Directory, "job.json")))
                { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = job.Directory });
            if (helper == null) throw new IOException("更新助手未能启动");
            try
            {
                DateTime deadline = DateTime.UtcNow.AddSeconds(10);
                while (DateTime.UtcNow < deadline)
                {
                    if (File.Exists(ready) && JsonFile.Read<string>(ready) == helper.Id + ":" + job.NewHash) return helper;
                    if (helper.HasExited) throw new IOException("更新助手启动失败，原程序保持运行");
                    Thread.Sleep(50);
                }
                throw new IOException("更新助手未确认就绪，原程序保持运行");
            }
            catch { try { if (!helper.HasExited) { helper.Kill(); helper.WaitForExit(5000); } } catch { } helper.Dispose(); throw; }
        }
        internal static string Quote(string path)
        {
            if (path.IndexOf('"') >= 0 || path.EndsWith("\\", StringComparison.Ordinal)) throw new InvalidDataException("更新参数路径无效");
            return "\"" + path + "\"";
        }
        internal static UpdateJob LoadJob(string path)
        {
            string full = Path.GetFullPath(path), directory = Path.GetDirectoryName(full), id = Path.GetFileName(directory);
            if (!Regex.IsMatch(id, "^[a-f0-9]{32}$") || !String.Equals(Path.GetDirectoryName(directory), UpdateRoot, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(full) != "job.json") throw new InvalidDataException("更新任务路径无效");
            UpdateJob job = JsonFile.Read<UpdateJob>(full);
            if (job == null || !String.Equals(job.Directory, directory, StringComparison.OrdinalIgnoreCase) || !Path.IsPathRooted(job.Destination) || !String.Equals(Path.GetExtension(job.Destination), ".exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新任务无效");
            string prefix = Path.Combine(Path.GetDirectoryName(job.Destination), ".CodexAutoResume-update-" + id);
            if (job.Staged != prefix + ".tmp" || job.Backup != prefix + ".bak" || !Regex.IsMatch(job.OldHash ?? "", "^[a-f0-9]{64}$") || !Regex.IsMatch(job.NewHash ?? "", "^[a-f0-9]{64}$")) throw new InvalidDataException("更新替换路径无效");
            ParseVersion(job.Version); return job;
        }
        // Transaction functions are also exercised against isolated EXE copies.
        internal static void Install(UpdateJob job)
        {
            if (File.Exists(job.Backup)) throw new IOException("更新备份路径已存在，取消替换");
            if (Hash(job.Destination) != job.OldHash) throw new IOException("当前 EXE 已被其他操作修改，取消更新");
            ValidateBinary(job.Staged, job.NewHash, job.Version);
            File.Replace(job.Staged, job.Destination, job.Backup);
        }
        internal static void RollBack(UpdateJob job)
        {
            if (!File.Exists(job.Backup) || Hash(job.Backup) != job.OldHash) throw new IOException("旧版备份校验失败");
            if (Hash(job.Destination) != job.NewHash) throw new IOException("新版 EXE 已被其他操作修改，保留备份供手动恢复");
            File.Replace(job.Backup, job.Destination, null);
        }
        internal static int Apply(string path)
        {
            UpdateJob job = null; bool installed = false; Process child = null; string stage = "load-job";
            try
            {
                job = LoadJob(path);
                stage = "verify-helper";
                if (!String.Equals(Executable, Path.Combine(job.Directory, "updater.exe"), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新助手位置无效");
                if (Hash(Executable) != job.NewHash || Portable.Version != job.Version) throw new InvalidDataException("更新助手文件或版本不匹配");
                stage = "handoff-preflight";
                ValidateBinary(job.Staged, job.NewHash, job.Version);
                if (File.Exists(job.Backup) || !Matches(job.Destination, job.OldHash)) throw new IOException("更新前 EXE 或备份路径已变化，原程序保持运行");
                stage = "wait-parent";
                UpdateParent.Wait(job, 30000, delegate { JsonFile.Write(Path.Combine(job.Directory, "helper-ready"), Process.GetCurrentProcess().Id + ":" + job.NewHash); });
                stage = "replace"; Install(job); installed = true;
                stage = "start-new";
                child = Process.Start(new ProcessStartInfo(job.Destination, "--updated " + Quote(path)) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(job.Destination) });
                if (child == null) throw new IOException("新版未能启动");
                stage = "await-new-ready";
                DateTime deadline = DateTime.UtcNow.AddSeconds(25);
                while (DateTime.UtcNow < deadline)
                {
                    if (File.Exists(Path.Combine(job.Directory, "ready")))
                    {
                        if (!Matches(job.Destination, job.NewHash)) throw new IOException("启动确认后 EXE 已变化，保留备份");
                        DeleteFile(job.Backup); DeleteFile(Path.Combine(job.Directory, "candidate.exe"));
                        RecordOutcome(job, "complete", null, null, false);
                        // Bookkeeping failure after a verified startup must not
                        // kill the working new app after its backup was removed.
                        try { JsonFile.Write(Path.Combine(job.Directory, "result.json"), "success"); } catch { }
                        return 0;
                    }
                    if (child.HasExited) break;
                    Thread.Sleep(100);
                }
                throw new IOException("新版未完成启动，正在恢复旧版");
            }
            catch (Exception error)
            {
                if (job != null)
                {
                    Exception recoveryError = null; bool restored = false;
                    try
                    {
                        if (installed)
                        {
                            if (child != null && !child.HasExited) { child.Kill(); if (!child.WaitForExit(10000)) throw new IOException("新版进程未退出，保留备份"); }
                            RollBack(job); restored = true;
                        }
                    }
                    catch (Exception recovery) { recoveryError = recovery; }
                    RecordOutcome(job, stage, error, recoveryError, restored);
                    // Never relaunch unknown/new bytes after a failed rollback.
                    if (Matches(job.Destination, job.OldHash) && (installed || UpdateParent.Gone(job)))
                        try { Process.Start(new ProcessStartInfo(job.Destination, "--update-failed " + Quote(path)) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(job.Destination) }); }
                        catch (Exception launch) { RecordOutcome(job, stage, error, launch, restored); }
                    DeleteFile(job.Staged);
                }
                return 1;
            }
            finally { if (child != null) child.Dispose(); }
        }
        internal static void DeleteFile(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
        internal static void CleanOldJobs()
        {
            // Only exact helper-owned files in old UUID directories; no recursion.
            try
            {
                if (!System.IO.Directory.Exists(UpdateRoot)) return;
                foreach (string directory in System.IO.Directory.GetDirectories(UpdateRoot))
                {
                    if (!Regex.IsMatch(Path.GetFileName(directory), "^[a-f0-9]{32}$") || System.IO.Directory.GetLastWriteTimeUtc(directory) > DateTime.UtcNow.AddDays(-7)) continue;
                    if (File.Exists(Path.Combine(directory, "failure.txt"))) continue;
                    string outcomePath = Path.Combine(directory, "outcome.json");
                    if (File.Exists(outcomePath))
                    {
                        try { var outcome = JsonFile.Read<UpdateOutcome>(outcomePath); if (outcome == null || outcome.ErrorType != null) continue; }
                        catch { continue; }
                    }
                    foreach (string name in new[] { "candidate.exe", "updater.exe", "job.json", "ready", "helper-ready", "result.json", "outcome.json" }) DeleteFile(Path.Combine(directory, name));
                    if (System.IO.Directory.GetFileSystemEntries(directory).Length == 0) System.IO.Directory.Delete(directory);
                }
            }
            catch { }
        }
    }
}
