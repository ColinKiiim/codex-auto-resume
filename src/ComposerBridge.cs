using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexAutoResume
{
    public sealed class ComposerContext
    {
        public string Backend { get; set; }
        public string Conversation { get; set; }
        public string Editor { get; set; }
        public string Title { get; set; }
        public int Port { get; set; }
        internal static bool SameConversation(ComposerContext a, ComposerContext b)
        { return a != null && b != null && a.Backend == b.Backend && a.Conversation == b.Conversation && a.Title == b.Title && a.Port == b.Port; }
    }
    public sealed class ComposerRequest
    {
        public long Window { get; set; }
        public int ProcessId { get; set; }
        public long ProcessStart { get; set; }
        public string ProcessPath { get; set; }
        public string Operation { get; set; }
        public ComposerContext Context { get; set; }
        public string Prompt { get; set; }
        public DateTime ExpiresUtc { get; set; }
        public DateTime? NotBeforeUtc { get; set; }
        internal static bool ValidPrompt(string text)
        { return !String.IsNullOrWhiteSpace(text) && text.Length <= 2048 && !text.Any(c => Char.IsControl(c) && c != '\n' || c == '\u2028' || c == '\u2029'); }
    }
    public sealed class ComposerReply
    {
        public ComposerContext Context { get; set; }
        public string Result { get; set; }
        public bool Running { get; set; }
        public bool Empty { get; set; }
        public bool Safe { get; set; }
        public string Stage { get; set; }
    }

    internal static class ComposerBridge
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);
        internal static string RequestText(ComposerRequest request)
        {
            // Redirected .NET Framework stdin may inherit an ANSI code page.
            // ASCII JSON escapes preserve Chinese paths and text on every PC.
            return String.Concat(new JavaScriptSerializer().Serialize(request).Select(c => c > 127 ? "\\u" + ((int)c).ToString("x4") : c.ToString()));
        }
        // The helper is bounded and owned. UI Automation providers can hang;
        // they never run on the monitor thread or leave an unbounded task there.
        internal static async Task<ComposerReply> RunAsync(Target target, string operation, ComposerContext context, string prompt, DateTime expires, CancellationToken cancel, DateTime? notBefore = null)
        {
            var request = new ComposerRequest { Window = target.Window.ToInt64(), ProcessId = target.ProcessId,
                ProcessStart = target.ProcessStart, ProcessPath = target.ProcessPath, Operation = operation,
                Context = context, Prompt = prompt, ExpiresUtc = expires, NotBeforeUtc = notBefore };
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            using (var process = new Process { StartInfo = new ProcessStartInfo(Updates.Executable, "--composer-worker") {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
                RedirectStandardError = true, StandardOutputEncoding = System.Text.Encoding.UTF8 } })
            using (var job = new UsageJob())
            {
                deadline.CancelAfter(20000);
                process.Start(); job.Add(process);
                // This grants only an eligible child; Windows can deny it when
                // CAR has no activation rights. Never attach input queues.
                AllowSetForegroundWindow(process.Id);
                using (deadline.Token.Register(delegate { try { process.Kill(); } catch { } }))
                {
                    try
                    {
                        var json = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };
                        await process.StandardInput.WriteLineAsync(RequestText(request)); process.StandardInput.Close();
                        string line = await process.StandardOutput.ReadLineAsync();
                        deadline.Token.ThrowIfCancellationRequested();
                        if (!process.WaitForExit(1000)) throw new IOException("Composer worker did not exit");
                        return json.Deserialize<ComposerReply>(line ?? "null") ?? new ComposerReply { Result = "Uncertain" };
                    }
                    finally { try { if (!process.HasExited) process.Kill(); } catch { } }
                }
            }
        }
    }
}
