using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Text.RegularExpressions;

namespace CodexAutoResume
{
    internal static class UpdateIntegrationTests
    {
        static void Require(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS: " + message); }
        static bool Wait(Func<bool> condition, int seconds)
        { DateTime deadline = DateTime.UtcNow.AddSeconds(seconds); while (!condition() && DateTime.UtcNow < deadline) Thread.Sleep(100); return condition(); }
        static Process Start(string exe, string arguments)
        { return Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }); }
        public static int Main(string[] args)
        {
            try
            {
                string testHome = Environment.GetEnvironmentVariable("CAR_UPDATE_TEST_HOME");
                if (String.IsNullOrEmpty(testHome))
                {
                    testHome = Path.Combine(Path.GetTempPath(), "CAR-update-suite-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(testHome);
                    Environment.SetEnvironmentVariable("CAR_UPDATE_TEST_HOME", testHome);
                }
                if (!String.Equals(Path.GetDirectoryName(testHome), Path.GetTempPath().TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) || !Regex.IsMatch(Path.GetFileName(testHome), "^CAR-update-suite-[a-f0-9]{32}$")) throw new Exception("Invalid isolated update-test root");
                typeof(Portable).GetField("DataDirectory").SetValue(null, testHome);
                if (args.Length == 1 && args[0] == "--check-live")
                {
                    using (System.Threading.CancellationTokenSource deadline = new System.Threading.CancellationTokenSource(30000))
                    {
                        AvailableUpdate update = Updates.CheckAsync(deadline.Token).GetAwaiter().GetResult();
                        Console.WriteLine("PASS: live official release endpoint read; " + (update == null ? "no newer complete stable release" : "newer version " + update.Version));
                    }
                    return 0;
                }
                if (args.Length == 2 && args[0] == "--apply-update") return Updates.Apply(args[1]);
                if (args.Length == 2 && args[0] == "--updated")
                {
                    UpdateJob job = Updates.LoadJob(args[1]);
                    if (job.Resume.ProcessPath == "crash") return 42;
                    if (job.Resume.ProcessPath == "rollback-failure") { File.AppendAllText(job.Backup, "external change"); return 42; }
                    File.WriteAllText(Path.Combine(job.Directory, "ready"), "fixture ready"); Thread.Sleep(500); return 0;
                }
                if (args.Length == 2 && args[0] == "--update-failed")
                { UpdateJob job = Updates.LoadJob(args[1]); File.WriteAllText(Path.Combine(job.Directory, "fallback.ready"), Updates.Hash(Updates.Executable)); return 0; }
                if (args.Length == 3 && args[0] == "--parent")
                {
                    string directory = Path.Combine(Updates.UpdateRoot, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
                    string candidate = Path.Combine(directory, "candidate.exe"); File.Copy(Updates.Executable, candidate);
                    // PE overlay makes the new hash distinct while preserving a
                    // real, executable x64 managed image and version metadata.
                    File.AppendAllText(candidate, "new build fixture");
                    UpdateJob job = Updates.Prepare(candidate, Portable.Version, new ResumeMemory { ProcessPath = args[1] });
                    File.WriteAllText(args[2], Path.Combine(directory, "job.json"));
                    if (args[1] == "gone") return 0;
                    if (args[1] == "helper-rejected")
                    {
                        File.AppendAllText(Path.Combine(directory, "updater.exe"), "tampered helper");
                        bool blocked = false; try { using (var helper = Updates.StartHelper(job)) { } } catch (IOException) { blocked = true; }
                        if (!blocked) throw new Exception("Unverified helper was accepted");
                        File.WriteAllText(Path.Combine(job.Directory, "rejected.ready"), "original still running"); return 0;
                    }
                    using (Process helper = Updates.StartHelper(job)) { }
                    if (args[1] == "handoff") Thread.Sleep(250); // Helper holds the live parent's verified handle until exit.
                    return 0;
                }
                foreach (string scenario in new[] { "handoff", "gone", "crash", "rollback-failure", "helper-rejected" })
                {
                    string sandbox = Path.Combine(Path.GetTempPath(), "CodexAutoResume-update-process-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(sandbox);
                    string original = Path.Combine(sandbox, "便携 & user's tool.exe"), result = Path.Combine(sandbox, "job-path.txt");
                    File.Copy(Updates.Executable, original); string originalHash = Updates.Hash(original);
                    UpdateJob job = null;
                    try
                    {
                        using (Process parent = Start(original, "--parent " + scenario + " " + Updates.Quote(result)))
                        { Require(parent.WaitForExit(10000) && parent.ExitCode == 0, scenario + ": original process stages update and exits"); }
                        job = Updates.LoadJob(File.ReadAllText(result));
                        if (scenario == "gone") using (Process helper = Updates.StartHelper(job)) { }
                        if (scenario == "handoff" || scenario == "gone")
                        {
                            Require(Wait(() => File.Exists(Path.Combine(job.Directory, "result.json")), 35), "new process acknowledges startup to the real helper");
                            Require(Updates.Hash(original) == job.NewHash && !File.Exists(job.Backup), "successful restart commits new EXE at same Unicode path and removes backup");
                        }
                        else if (scenario == "crash")
                        {
                            Require(Wait(() => File.Exists(Path.Combine(job.Directory, "fallback.ready")), 35), "failed new process triggers rollback and relaunch of original");
                            Require(Updates.Hash(original) == originalHash && File.ReadAllText(Path.Combine(job.Directory, "fallback.ready")) == originalHash, "fallback runs exact original bytes");
                            var outcome = JsonFile.Read<UpdateOutcome>(Path.Combine(job.Directory, "outcome.json"));
                            Require(outcome.Restored && outcome.OldVerified && !outcome.NewVerified, "rollback result is verified independently of the dialog text");
                        }
                        else if (scenario == "rollback-failure")
                        {
                            Require(Wait(() => File.Exists(Path.Combine(job.Directory, "outcome.json")), 35), "failed rollback records the actual destination state");
                            var outcome = JsonFile.Read<UpdateOutcome>(Path.Combine(job.Directory, "outcome.json"));
                            Require(!outcome.Restored && !outcome.OldVerified && outcome.NewVerified && outcome.RecoveryError != null && File.Exists(job.Backup) && !File.Exists(Path.Combine(job.Directory, "fallback.ready")), "changed backup is preserved and failed new bytes are not relaunched as restored");
                        }
                        else
                        {
                            Require(File.Exists(Path.Combine(job.Directory, "rejected.ready")) && Updates.Hash(original) == originalHash && !File.Exists(job.Backup), "helper validation fails before the original process exits or any replacement occurs");
                        }
                        // The fixture and helper exit shortly after their marker.
                        Require(Wait(() => CanOpenExclusive(original), 5), "updated fixture releases its executable");
                    }
                    finally
                    {
                        if (job != null)
                        {
                            Wait(() => CanOpenExclusive(Path.Combine(job.Directory, "updater.exe")), 5);
                            foreach (string file in Directory.GetFiles(job.Directory)) File.Delete(file); Directory.Delete(job.Directory);
                        }
                        foreach (string file in Directory.GetFiles(sandbox)) File.Delete(file); Directory.Delete(sandbox);
                    }
                }
                return 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        }
        static bool CanOpenExclusive(string path)
        { try { using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } return true; } catch (IOException) { return false; } }
    }
}
