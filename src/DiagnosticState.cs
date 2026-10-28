using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Automation;

namespace CodexAutoResume
{
    // A deliberately read-only, bounded provider query. Its output is diagnostic
    // context, never authorization to click, send, navigate or declare recovery.
    internal static class DiagnosticStateWorker
    {
        internal static Dictionary<string, object> Inspect(ComposerRequest request)
        {
            if (request == null || request.Operation != "diagnostic-inspect" || !ComposerWorker.Valid(request, null))
                return new Dictionary<string, object> { { "result", "blocked-target" } };
            var documents = new List<string>(); var current = new List<string>();
            var buttons = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int nodes = 0, editors = 0, offscreenEditors = 0;
            bool truncated = false;
            var automation = (ScopeUia)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("ff48dba4-60ef-4201-aa87-54103eef594e")));
            var walker = automation.ControlViewWalker();
            Action<ScopeElement, int> visit = null;
            visit = (element, depth) =>
            {
                if (element == null) return;
                if (++nodes > 1600 || depth > 36 || DateTime.UtcNow >= request.ExpiresUtc.ToUniversalTime()) { truncated = true; return; }
                int type = Convert.ToInt32(element.Property(30003));
                string css = element.PropertyEx(30012, true) as string ?? "", nameValue = element.PropertyEx(30005, true) as string ?? "";
                // Do not traverse chat message bodies or return their text.
                if (css.Contains("thread-scroll-container") || css.StartsWith("group/sidebar-rail ", StringComparison.Ordinal)) return;
                if (type == ControlType.Document.Id && (element.PropertyEx(30011, true) as string ?? "") == "RootWebArea") documents.Add(CaptureEvidence.Hash(nameValue));
                if (type == ControlType.Edit.Id) { editors++; if (Convert.ToBoolean(element.Property(30022))) offscreenEditors++; }
                if (type == ControlType.Button.Id)
                {
                    string aria = element.PropertyEx(30102, true) as string ?? "";
                    if (aria.Contains("current=page")) current.Add(CaptureEvidence.Hash(nameValue));
                    foreach (string name in new[] { "Stop", "Stop generating", "停止", "停止生成", "Resume", "Continue", "Retry", "继续", "恢复", "重试", "Send", "Send message", "发送", "发送消息", "Skip", "Submit", "跳过", "提交" })
                        if (String.Equals(nameValue, name, StringComparison.OrdinalIgnoreCase))
                        { string key = name.ToLowerInvariant() + ":enabled=" + element.Property(30010) + ":offscreen=" + element.Property(30022); int count; buttons.TryGetValue(key, out count); buttons[key] = count + 1; break; }
                }
                for (var child = walker.First(element); child != null && !truncated; child = walker.Next(child)) visit(child, depth + 1);
            };
            visit(automation.ElementFromHandle(new IntPtr(request.Window)), 0);
            if (!ComposerWorker.Valid(request, null)) return new Dictionary<string, object> { { "result", "target-changed" } };
            return new Dictionary<string, object> { { "result", "observed" }, { "diagnosticOnly", true }, { "verifiedConversation", false }, { "nodes", nodes },
                { "truncated", truncated }, { "documentTitleHashes", documents }, { "selectedRowTitleHashes", current }, { "editorCount", editors }, { "offscreenEditors", offscreenEditors }, { "knownButtons", buttons } };
        }
        internal static int Run()
        {
            var json = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 };
            object reply;
            try { reply = Inspect(json.Deserialize<ComposerRequest>(Console.ReadLine())); }
            catch (Exception error) { reply = new { result = "unavailable", error = error.GetType().Name }; }
            using (var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false))) { output.WriteLine(json.Serialize(reply)); output.Flush(); }
            return 0;
        }
        internal static async Task<string> ReadAsync(Target target, CancellationToken cancel)
        {
            var request = new ComposerRequest { Window = target.Window.ToInt64(), ProcessId = target.ProcessId, ProcessStart = target.ProcessStart,
                ProcessPath = target.ProcessPath, Operation = "diagnostic-inspect", ExpiresUtc = DateTime.UtcNow.AddSeconds(4) };
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            using (var process = new Process { StartInfo = new ProcessStartInfo(Updates.Executable, "--diagnostic-state-worker") { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 } })
            using (var job = new UsageJob())
            {
                deadline.CancelAfter(3500);
                process.Start(); job.Add(process);
                using (deadline.Token.Register(delegate { try { process.Kill(); } catch { } }))
                try
                {
                    var ignoredError = process.StandardError.ReadToEndAsync();
                    await process.StandardInput.WriteLineAsync(ComposerBridge.RequestText(request)); process.StandardInput.Close();
                    string line = await process.StandardOutput.ReadLineAsync(); deadline.Token.ThrowIfCancellationRequested();
                    if (!process.WaitForExit(500) || line == null || line.Length > 64 * 1024) throw new IOException();
                    // Reject malformed output rather than adding arbitrary pipe data to logs.
                    new JavaScriptSerializer().DeserializeObject(line); return line;
                }
                finally { try { if (!process.HasExited) process.Kill(); } catch { } }
            }
        }
    }
    internal sealed partial class MainForm
    {
        bool diagnosticStateBusy;
        DateTime nextDiagnosticState;
        async void PulseDiagnosticState()
        {
            if (!Evidence.Enabled || !diagnosticUiaEnabled || diagnosticStateBusy || target == null || target.Demo || DateTime.UtcNow < nextDiagnosticState || lifetime.IsCancellationRequested) return;
            Target connected = target; int revision = generation; diagnosticStateBusy = true; nextDiagnosticState = DateTime.UtcNow.AddSeconds(15);
            string context = CaptureEvidence.TargetInfo(connected, revision);
            try
            {
                string result = await Task.Run(() => DiagnosticStateWorker.ReadAsync(connected, lifetime.Token));
                if (!IsDisposed && Evidence.Enabled && target == connected && generation == revision)
                    journal.Write("diagnostics.log", "uia-diagnostic", result + "; " + context + "; after=" + CaptureEvidence.TargetInfo(connected, revision), DateTime.UtcNow);
            }
            catch (Exception error) { if (!IsDisposed && Evidence.Enabled) journal.Write("diagnostics.log", "uia-diagnostic", "result=unavailable; error=" + error.GetType().Name + "; " + context, DateTime.UtcNow); }
            finally { diagnosticStateBusy = false; }
        }
    }
}
