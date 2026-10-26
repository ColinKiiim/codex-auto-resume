using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexAutoResume
{
    internal static class UsageTests
    {
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        static object Parse(string json) { return Json.DeserializeObject(json); }
        internal static void Run(Action<bool, string> check)
        {
            object account = Parse("{\"type\":\"chatgpt\",\"email\":\"sample@example.invalid\",\"planType\":\"team\"}");
            var snapshot = UsageJson.Parse(Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":100}},\"rateLimitsByLimitId\":{\"codex\":{\"limitId\":\"codex\",\"primary\":{\"usedPercent\":73.4,\"windowDurationMins\":300,\"resetsAt\":1792926855},\"secondary\":{\"usedPercent\":100,\"windowDurationMins\":10080}}}}"), account);
            check(snapshot.Windows.Length == 2 && snapshot.Windows[0].Percent == "27%" && snapshot.Windows[1].Percent == "0%", "usage parses remaining without changing a raw primary window when weekly is exhausted");
            check(snapshot.Windows[0].ResetUtc.Value.Kind == DateTimeKind.Utc && snapshot.Windows[0].Minutes == 300, "usage preserves Unix seconds and dynamic window lengths");
            check(UsageJson.Window(Parse("{\"usedPercent\":null,\"resetsAt\":1e30}" )).Remaining == null, "unknown quota and out-of-range reset never become zero");
            check(UsageJson.Window(Parse("{\"usedPercent\":-1}" )).Remaining == null, "invalid used percentage remains unknown");
            snapshot = UsageJson.Parse(Parse("{\"rateLimits\":{\"primary\":null,\"secondary\":null,\"credits\":{\"unlimited\":true}}}"), account);
            check(snapshot.Windows.Length == 0 && snapshot.Unlimited, "unlimited credit signal stays separate from absent quota windows");
            bool ambiguous = false;
            try { UsageJson.Parse(Parse("{\"rateLimitsByLimitId\":{\"a\":{},\"b\":{}}}"), account); } catch (InvalidDataException) { ambiguous = true; }
            check(ambiguous, "usage does not merge unrelated buckets");
            var home = Path.Combine(Path.GetTempPath(), "CAR-usage-中文-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(home);
            try
            {
                var source = new UsageSource { Executable = Process.GetCurrentProcess().MainModule.FileName, Home = home, Arguments = "--usage-fake success" };
                var result = CodexUsage.ReadAsync(source, CancellationToken.None, 10000).GetAwaiter().GetResult();
                check(result.Error == UsageError.None && result.Snapshot != null && result.Snapshot.Windows[0].Percent == "75%", "real child pipes match IDs, drain stderr and tolerate notifications/server requests");
                source.Arguments = "--usage-fake changed";
                result = CodexUsage.ReadAsync(source, CancellationToken.None, 10000).GetAwaiter().GetResult();
                check(result.Error == UsageError.Changed && result.Snapshot == null, "account change while fetching rejects old quota");
                source.Arguments = "--usage-fake bootstrap";
                result = CodexUsage.ReadAsync(source, CancellationToken.None, 10000).GetAwaiter().GetResult();
                check(result.Error == UsageError.None && result.Snapshot.Windows[0].Percent == "75%", "startup account notification discards poisoned in-flight limits and re-reads a complete stable epoch");
                source.Arguments = "--usage-fake churn";
                result = CodexUsage.ReadAsync(source, CancellationToken.None, 10000).GetAwaiter().GetResult();
                check(result.Error == UsageError.Changed && result.Snapshot == null, "repeated account notifications remain rejected after the one bounded retry");
                source.Arguments = "--usage-fake notified-change";
                result = CodexUsage.ReadAsync(source, CancellationToken.None, 10000).GetAwaiter().GetResult();
                check(result.Error == UsageError.Changed && result.Snapshot == null, "notification retry cannot silently accept a different account even if it stabilizes");
                source.Arguments = "--usage-fake signedout";
                result = CodexUsage.ReadAsync(source, CancellationToken.None, 10000).GetAwaiter().GetResult();
                check(result.Error == UsageError.NotSignedIn && result.Snapshot == null, "signed-out login is not shown as zero quota");
                source.Arguments = "--usage-fake wronghome";
                result = CodexUsage.ReadAsync(source, CancellationToken.None, 10000).GetAwaiter().GetResult();
                check(result.Error == UsageError.Changed, "unexpected login home is rejected");
                source.Arguments = "--usage-fake malformed";
                result = CodexUsage.ReadAsync(source, CancellationToken.None, 10000).GetAwaiter().GetResult();
                check(result.Error == UsageError.Unavailable, "protocol failure does not expose raw server text");
                source.Arguments = "--usage-fake idle";
                var watch = Stopwatch.StartNew(); result = CodexUsage.ReadAsync(source, CancellationToken.None, 350).GetAwaiter().GetResult();
                check(result.Error == UsageError.Timeout && watch.Elapsed.TotalSeconds < 5, "silent child times out and is terminated promptly");
                using (var cancel = new CancellationTokenSource(250))
                {
                    bool cancelled = false;
                    try { CodexUsage.ReadAsync(source, cancel.Token, 10000).GetAwaiter().GetResult(); } catch (OperationCanceledException) { cancelled = true; }
                    check(cancelled, "caller cancellation terminates a pending query");
                }
                source.Arguments = "--usage-fake family";
                result = CodexUsage.ReadAsync(source, CancellationToken.None, 1200).GetAwaiter().GetResult();
                string childFile = Path.Combine(home, "child.pid");
                bool childStopped = false;
                if (File.Exists(childFile))
                {
                    int childId = Int32.Parse(File.ReadAllText(childFile));
                    try { using (var child = Process.GetProcessById(childId)) childStopped = child.HasExited || child.WaitForExit(2000); } catch (ArgumentException) { childStopped = true; }
                }
                check(result.Error == UsageError.Timeout && childStopped, "timeout also terminates owned descendants holding inherited pipes");
                string install = Path.Combine(home, "different user", "app"); Directory.CreateDirectory(Path.Combine(install, "resources"));
                File.WriteAllText(Path.Combine(install, "resources", "codex.exe"), "fixture");
                check(UsageSource.Find(Path.Combine(install, "ChatGPT.exe"), "test").Executable.EndsWith("resources\\codex.exe"), "target-relative discovery supports spaces and arbitrary Store installation roots without PATH");
                check(UsageSource.Find(Path.Combine(home, "Other.exe"), "test") == null, "missing target CLI does not silently select an unrelated installation");
            }
            finally { Directory.Delete(home, true); }
        }
        internal static int Serve(string[] args)
        {
            Console.OutputEncoding = new System.Text.UTF8Encoding(false);
            string mode = args.Length > 1 ? args[1] : "success";
            if (mode == "idle") { Thread.Sleep(20000); return 0; }
            if (mode == "family")
            {
                using (var child = Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, "--usage-fake idle") { UseShellExecute = false, CreateNoWindow = true }))
                { File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("CODEX_HOME"), "child.pid"), child.Id.ToString()); Thread.Sleep(20000); }
                return 0;
            }
            if (mode == "malformed") { Console.WriteLine("invalid protocol"); return 0; }
            int reads = 0, limitReads = 0; string line;
            while ((line = Console.ReadLine()) != null)
            {
                var request = Parse(line); string method = UsageJson.Text(request, "method"); object id = UsageJson.Get(request, "id");
                if (method == "initialized" || method == "") continue;
                object result;
                if (method == "initialize") result = new { codexHome = mode == "wronghome" ? Path.GetTempPath() : Environment.GetEnvironmentVariable("CODEX_HOME") };
                else if (method == "account/read")
                {
                    reads++;
                    result = new { account = mode == "signedout" ? null : new { type = "chatgpt", email = (mode == "changed" || mode == "notified-change") && reads > 1 ? "other@example.invalid" : "sample@example.invalid", planType = "team" } };
                }
                else
                {
                    limitReads++;
                    if (mode == "churn" || (mode == "bootstrap" || mode == "notified-change") && limitReads == 1)
                        Console.WriteLine(Json.Serialize(new { method = "account/updated", @params = new { authMode = "chatgpt" } }));
                    Console.Error.WriteLine(new string('x', 20000));
                    Console.WriteLine(Json.Serialize(new { method = "account/rateLimits/updated", @params = new { } }));
                    Console.WriteLine(Json.Serialize(new { id = 999, method = "unsupported/request", @params = new { } }));
                    result = Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":" + (mode == "bootstrap" && limitReads == 1 ? "95" : "25") + ",\"windowDurationMins\":300}}}");
                }
                Console.WriteLine(Json.Serialize(new { id = id, result = result })); Console.Out.Flush();
            }
            return 0;
        }
    }
}
