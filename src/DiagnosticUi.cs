using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexAutoResume
{
    internal sealed partial class MainForm
    {
        CaptureEvidence captureEvidence;
        DateTime nextDecisionLog;
        ButtonState diagnosticLastState = ButtonState.Unknown;
        bool diagnosticExporting;
        CheckBox detailedCaptureOption;
        bool diagnosticUiaEnabled;
        CaptureEvidence Evidence { get { return captureEvidence ?? (captureEvidence = new CaptureEvidence(logDirectory, journal)); } }
        string DiagnosticContext(Target connected)
        {
            var input = Native.ReadInput();
            return CaptureEvidence.TargetInfo(connected, generation) + "; monitoring=" + monitoring + "; automatic=" + automatic.Checked + "; background=" + background.Checked +
                "; last-observed=" + lastObservedState + "; attempts=" + gate.Attempts + "; confirmed=" + gate.Verified + "; question=" + questions.Waiting +
                "; quota-enabled=" + QuotaEnabled + "; schedule-active=" + resumeSchedule.Active + "; scope-limited=" + preferences.LimitScope +
                "; usage-snapshot-account-sha256=" + (usageSnapshot == null ? "unavailable" : CaptureEvidence.Hash(usageSnapshot.AccountKey)) + "; usage-fetched-utc=" + (usageSnapshot == null ? "unavailable" : usageSnapshot.FetchedUtc.ToString("O")) +
                "; input-valid=" + input.Valid + "; input-held=" + input.Held + "; input-idle-ms=" + input.IdleMilliseconds + "; input-tick=" + input.Tick;
        }
        void TraceTargetConnected(Target connected)
        {
            try { journal.Write("diagnostics.log", "target-environment", DiagnosticContext(connected) + "; target-version=" + System.Diagnostics.FileVersionInfo.GetVersionInfo(connected.ProcessPath).FileVersion + "; target-path-sha256=" + CaptureEvidence.Hash(connected.ProcessPath) + "; displays=" + Screen.AllScreens.Length, DateTime.UtcNow); }
            catch { journal.Write("diagnostics.log", "target-environment", "metadata=unavailable; " + CaptureEvidence.TargetInfo(connected, generation), DateTime.UtcNow); }
        }
        void TraceAcquisition(Target connected, WindowFrame frame, bool independent)
        {
            try
            {
                Rectangle? local = null;
                if (!lastBadge.IsEmpty && frame.Bounds.Contains(lastBadge)) { Rectangle value = lastBadge; value.Offset(-frame.Bounds.X, -frame.Bounds.Y); local = value; }
                if (Evidence.Enabled) Evidence.Record(frame, independent ? "independent-acquisition" : "session-acquisition", DiagnosticContext(connected) + "; badge-region=previous-observation", local, independent);
            }
            catch { /* Diagnostics must not change monitoring or click decisions. */ }
        }
        void TraceDecision(Target connected, WindowFrame frame, Detection detection, Rectangle crop, string role)
        {
            try
            {
                bool changed = diagnosticLastState != detection.State;
                if (changed || frame.Source == "independent" || DateTime.UtcNow >= nextDecisionLog || role == "action-preflight")
                {
                    nextDecisionLog = DateTime.UtcNow.AddSeconds(10); diagnosticLastState = detection.State;
                    journal.Write("diagnostics.log", "classification", "role=" + role + "; state=" + detection.State + "; crop=" + crop + "; button-bounds-in-crop=" + detection.ButtonBounds +
                        "; triangle=" + detection.ResumeScore.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + "; square=" + detection.RunningScore.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) +
                        "; send=" + detection.SendScore.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + "; voice=" + detection.VoiceScore.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) +
                        "; " + CaptureEvidence.FrameInfo(frame) + "; " + DiagnosticContext(connected), DateTime.UtcNow);
                }
                if (Evidence.Enabled && (changed || role == "action-preflight"))
                { Rectangle local = crop; local.Offset(-frame.Bounds.X, -frame.Bounds.Y); Evidence.Record(frame, role + "-" + detection.State, DiagnosticContext(connected), local, true); }
            }
            catch { }
        }
        void AddDiagnosticExportTools(FlowLayoutPanel tools)
        {
            tools.Controls.Add(UiButton("复制近 30 分钟日志", async delegate { await CopyRecentDiagnostics(); }));
            tools.Controls.Add(UiButton("导出近 30 分钟诊断包", async delegate { await ExportRecentDiagnostics(); }));
            var detail = new CheckBox { Name = "DetailedCapture", Text = "详细诊断（含原始窗口截图）", AutoSize = true, Checked = Evidence.Enabled };
            detailedCaptureOption = detail;
            detail.CheckedChanged += delegate { Evidence.Enable(detail.Checked); nextDiagnosticState = DateTime.MinValue; };
            tools.Controls.Add(detail);
            var uia = new CheckBox { Name = "ReadOnlyUiaDiagnostic", Text = "只读 UIA 对照（可能影响目标重绘）", AutoSize = true, Checked = diagnosticUiaEnabled };
            uia.CheckedChanged += delegate { diagnosticUiaEnabled = uia.Checked; nextDiagnosticState = DateTime.MinValue; journal.Write("diagnostics.log", "uia-diagnostic-mode", "enabled=" + uia.Checked, DateTime.UtcNow); };
            tools.Controls.Add(uia);
            tools.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(490, 0), ForeColor = Color.DimGray,
                Text = "仅本机保存，可能包含聊天内容；本次开启最多 1 小时\n截图按 180 组 / 128 MB 滚动，文本可复制，诊断包包含近期可用截图" });


        }
        async Task CopyRecentDiagnostics()
        {
            try
            {
                DateTime now = DateTime.UtcNow;
                var logs = await Task.Run(() => journal.Recent(now));
                if (IsDisposed) return;
                Clipboard.SetText(logs.Text());
                UiDialog.Show(this, UiText.Get("已复制近 30 分钟文本日志，不含截图"), UiText.Get("诊断日志"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception error) { if (!IsDisposed) UiDialog.Show(this, UiText.Get("复制失败，请使用导出诊断包") + " (" + error.GetType().Name + ")", UiText.Get("诊断日志"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        async Task ExportRecentDiagnostics()
        {
            if (diagnosticExporting) return;
            using (var dialog = new SaveFileDialog { Title = UiText.Get("导出近 30 分钟诊断包"), Filter = "ZIP (*.zip)|*.zip", FileName = "CodexAutoResume-diagnostics-v" + Portable.Version + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip", AddExtension = true, DefaultExt = "zip" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                diagnosticExporting = true;
                string temporary = dialog.FileName + ".partial-" + Guid.NewGuid().ToString("N");
                try
                {
                    DateTime now = DateTime.UtcNow;
                    var work = Evidence.Pending;
                    // A slow disk must not freeze monitoring or await a writer forever.
                    if (await Task.WhenAny(work, Task.Delay(5000)) != work) throw new IOException("Evidence writer is still busy");
                    await Task.Run(delegate
                    {
                        var logs = journal.Recent(now); logs.Export(temporary, Evidence);
                        if (File.Exists(dialog.FileName)) File.Replace(temporary, dialog.FileName, null);
                        else File.Move(temporary, dialog.FileName);
                    });
                    if (!IsDisposed) UiDialog.Show(this, UiText.Get("诊断包已导出；分享前请检查截图中的聊天内容") + Environment.NewLine + dialog.FileName, UiText.Get("诊断日志"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception error) { if (!IsDisposed) UiDialog.Show(this, UiText.Get("导出失败，原文件已保留") + " (" + error.GetType().Name + ")", UiText.Get("诊断日志"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                finally { diagnosticExporting = false; try { File.Delete(temporary); } catch { } }
            }
        }
    }
}
