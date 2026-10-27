using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexAutoResume
{
    internal sealed partial class MainForm
    {
        readonly QuotaPause quotaPause = new QuotaPause();
        CancellationTokenSource quotaCancellation;
        readonly RecoveryWindow quotaQuiet = new RecoveryWindow();
        DateTime quotaContextRetry;
        QuotaLevel quotaOperationLevel;
        ButtonState quotaQuietState;
        Func<Target, string, ComposerContext, string, DateTime, CancellationToken, Task<ComposerReply>> composerRunner = null;
        bool QuotaEnabled { get { return preferences.QuotaControl && automatic.Checked; } }
        bool QuotaSquareWake { get { return QuotaEnabled && QuotaNow == QuotaLevel.Low && quotaPause.Phase == QuotaPhase.None && !quotaPause.ManualOverride && DateTime.UtcNow >= quotaContextRetry; } }
        QuotaLevel QuotaNow { get { return QuotaPolicy.Evaluate(usageSnapshot, usageError, preferences, DateTime.UtcNow); } }
        bool QuotaAllowsNormal() { return ScheduleAllows() && (!QuotaEnabled || !quotaPause.Owned && QuotaNow == QuotaLevel.Healthy); }
        bool QuotaCurrent(Target connected, int revision)
        { string why; return !IsDisposed && monitoring && QuotaEnabled && generation == revision && target == connected && connected.ValidateIdentity(out why); }
        void CancelQuota()
        {
            if (quotaCancellation != null) quotaCancellation.Cancel();
            quotaPause.Reset(); quotaQuiet.Reset(); quotaQuietState = ButtonState.Unknown; quotaContextRetry = DateTime.MinValue;
        }
        void QuotaUsageChanged()
        {
            if (quotaCancellation != null && (QuotaNow != quotaOperationLevel || quotaPause.AccountKey != null && quotaPause.AccountKey != (usageSnapshot == null ? null : usageSnapshot.AccountKey) || timedAccount != null && timedAccount != TimedInstructionPolicy.AccountFingerprint(usageSnapshot == null ? null : usageSnapshot.AccountKey))) quotaCancellation.Cancel();
            if (QuotaEnabled && quotaPause.Phase == QuotaPhase.Waiting && QuotaNow == QuotaLevel.Healthy)
            { nextMonitor = DateTime.MinValue; quotaPause.NextSample = DateTime.MinValue; TickMonitor(); }
        }
        bool QuotaSampleDue()
        {
            if (!QuotaEnabled || !quotaPause.Owned) return true;
            if (quotaPause.AccountKey != null && usageSnapshot != null && quotaPause.AccountKey != usageSnapshot.AccountKey)
            { quotaPause.Phase = QuotaPhase.Uncertain; SetStatus("账号已变化，请手动核对额度暂停"); }
            DateTime now = DateTime.UtcNow;
            QuotaLevel level = QuotaNow;
            if (!quotaPause.Due(level, now)) return false;
            // Reserve the next slot even if OCR, visibility or localization
            // exits before HandleQuota. Question mode must not undo the minute.
            if (quotaPause.Phase == QuotaPhase.Waiting && level != QuotaLevel.Healthy) quotaPause.NextSample = now.AddMinutes(1);
            return true;
        }
        async Task<ComposerReply> ReadComposer(Target connected, string operation, ComposerContext context)
        {
            using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                quotaCancellation = cancellation; quotaOperationLevel = QuotaNow;
                try
                {
                    DateTime expires = usageSnapshot == null ? DateTime.UtcNow : usageSnapshot.FetchedUtc.AddSeconds(30);
                    if (composerRunner != null) return await composerRunner(connected, operation, context, preferences.ContinuePrompt, expires, cancellation.Token);
                    return await ComposerBridge.RunAsync(connected, operation, context, preferences.ContinuePrompt, expires, cancellation.Token,
                        operation == "submit" && resumeSchedule.Active ? (DateTime?)resumeSchedule.DueUtc : null);
                }
                finally { if (quotaCancellation == cancellation) quotaCancellation = null; }
            }
        }
        async Task<bool> ClickQuota(Target connected, int revision, ButtonState expected, Rectangle observedCrop, Detection observed)
        {
            InputSnapshot input = inputReader();
            if (!input.Valid || input.Held || input.IdleMilliseconds < RecoveryWindow.QuietMilliseconds) return false;
            string title = Native.Title(connected.Window);
            if (!QuotaCurrent(connected, revision) || !ScopeCheck(connected)) return false;
            DateTime scopedBeforeCapture = DateTime.UtcNow;
            using (WindowFrame fresh = background.Checked ? await ReadBackground(connected, revision, true) : null)
            {
                if (!QuotaCurrent(connected, revision) || fresh != null && !ScopePolicy.FrameAfterBinding(preferences.LimitScope, fresh.ContentAt, scopedBeforeCapture)) return false;
                Rectangle crop; string reason;
                if (fresh != null ? !connected.LocateBackground(fresh, recognizer, null, out crop, out reason) : !connected.Locate(recognizer, null, out crop, out reason)) return false;
                if (crop != observedCrop) return false;
                Rectangle local = crop; if (fresh != null) local.Offset(-fresh.Bounds.X, -fresh.Bounds.Y);
                using (Bitmap image = fresh == null ? Native.Capture(crop) : fresh.Image.Clone(local, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
                {
                    Detection d = recognizer.Classify(image);
                    if (d.State != expected || Math.Abs(d.ButtonBounds.X - observed.ButtonBounds.X) > 2 || Math.Abs(d.ButtonBounds.Y - observed.ButtonBounds.Y) > 2) return false;
                    Point center = new Point(crop.X + d.ButtonBounds.X + d.ButtonBounds.Width / 2, crop.Y + d.ButtonBounds.Y + d.ButtonBounds.Height / 2);
                    QuotaLevel needed = expected == ButtonState.Running ? QuotaLevel.Low : QuotaLevel.Healthy;
                    if (expected == ButtonState.Running) quotaPause.StopSent(DateTime.UtcNow);
                    else { quotaPause.Phase = QuotaPhase.Resuming; quotaPause.ActionAt = DateTime.UtcNow; }
                    bool sent = BackgroundClick.Send(connected.Window, fresh == null ? WindowCapture.Bounds(connected.Window) : fresh.Bounds, center, delegate {
                        InputSnapshot after = inputReader();
                        return QuotaCurrent(connected, revision) && ScopeCheck(connected) && (expected == ButtonState.Running || ScheduleAllows()) && QuotaNow == needed && input.Valid && after.Valid && !after.Held &&
                            after.IdleMilliseconds >= RecoveryWindow.QuietMilliseconds && input.Tick == after.Tick && Native.Title(connected.Window) == title &&
                            (fresh == null ? Native.Uncovered(connected.Window, crop) : fresh.ContentFresh(DateTime.UtcNow) && (DateTime.UtcNow - fresh.CapturedAt).TotalSeconds <= 2);
                    });
                    if (sent && expected == ButtonState.Resume) gate.MarkAttempt(DateTime.UtcNow);
                    return sent;
                }
            }
        }
        async Task<bool> HandleQuota(Target connected, int revision, Detection d, Rectangle crop, DateTime contentAt)
        {
            if (!QuotaEnabled) return false;
            QuotaLevel level = QuotaNow;
            DateTime now = DateTime.UtcNow;
            bool owned = quotaPause.Owned;
            if (d.State != quotaQuietState) { quotaQuiet.Reset(); quotaQuietState = d.State; }
            quotaPause.Observe(d.State, contentAt, now);
            if (owned && !quotaPause.Owned)
            { quotaQuiet.Reset(); SetStatus(quotaPause.ManualOverride ? "检测到手动继续，本次额度暂停已取消" : "额度恢复，Codex 已继续运行"); return true; }
            if (quotaPause.Phase == QuotaPhase.Uncertain)
            { stabilityTimer.Stop(); SetStatus("额度操作结果不明，请手动核对；不会重复发送"); return true; }
            if (quotaPause.Owned && quotaPause.AccountKey != (usageSnapshot == null ? null : usageSnapshot.AccountKey))
            { quotaPause.Phase = QuotaPhase.Uncertain; SetStatus("账号已变化，请手动核对额度暂停"); return true; }
            if (quotaPause.Phase == QuotaPhase.Stopping || quotaPause.Phase == QuotaPhase.Resuming)
            { SetStatus(quotaPause.Phase == QuotaPhase.Stopping ? "正在按额度暂停，等待画面确认" : "额度恢复，等待运行确认"); return true; }
            if (level == QuotaLevel.Healthy && !quotaPause.Owned)
            { quotaPause.ManualOverride = false; return false; }
            if (quotaPause.Phase == QuotaPhase.Waiting && level != QuotaLevel.Healthy)
            {
                quotaPause.NextSample = now.AddMinutes(1); WindowCapture.StopCapture(); stabilityTimer.Stop();
                SetStatus(level == QuotaLevel.Low ? "额度暂停中 · 每分钟检查画面" : "额度暂停中 · 等待有效额度"); return true;
            }
            if (level == QuotaLevel.Unknown)
            { stabilityTimer.Stop(); SetStatus("等待有效额度，暂不自动点击"); return true; }
            if (quotaPause.ManualOverride) return true;
            bool stopping = !quotaPause.Owned && level == QuotaLevel.Low;
            if (!stopping && !ScheduleAllows()) return true;
            if (stopping && d.State != ButtonState.Running)
            { stabilityTimer.Stop(); SetStatus("额度不足，等待运行方块后暂停"); return true; }
            if (!stopping && d.State != ButtonState.Resume && d.State != ButtonState.Send && d.State != ButtonState.Voice)
            { SetStatus("额度已恢复，等待可靠的继续入口"); return true; }
            // Reuse the complete quiet-window guard for both square and triangle.
            quotaQuiet.Observe(ButtonState.Resume, now);
            if (!quotaQuiet.Ready(inputReader(), now)) { SetStatus("等待鼠标、键盘空闲 1.5 秒"); return true; }
            if (now < quotaContextRetry) { SetStatus("无法核实原对话，等待重试；可手动继续"); return true; }
            try
            {
                if (!ScopeCheck(connected)) { ScheduleReason("等待确认指定范围"); SetStatus("等待确认指定范围"); return true; }
                ComposerReply composer = await ReadComposer(connected, "inspect", null);
                if (!QuotaCurrent(connected, revision)) return true;
                if (composer.Result != "Observed" || composer.Context == null || !composer.Safe || (stopping ? !composer.Running : composer.Running) ||
                    !stopping && !ComposerContext.SameConversation(quotaPause.Context, composer.Context))
                { ScheduleReason("等待原对话核验"); quotaContextRetry = DateTime.UtcNow.AddMinutes(1); SetStatus("无法核实原对话，等待重试；可手动继续"); return true; }
                if (stopping)
                {
                    quotaPause.Context = composer.Context; quotaPause.AccountKey = usageSnapshot.AccountKey;
                    if (QuotaNow != QuotaLevel.Low) return true;
                    if (await ClickQuota(connected, revision, ButtonState.Running, crop, d)) SetStatus("正在按额度暂停，等待画面确认");
                    else { quotaPause.Reset(); SetStatus("点击前状态变化，已取消本次点击并重新观察"); }
                    return true;
                }
                if (QuotaNow != QuotaLevel.Healthy || !ScheduleAllows() || !ScopeCheck(connected)) return true;
                quotaPause.Context = composer.Context;
                if (d.State == ButtonState.Resume)
                {
                    if (await ClickQuota(connected, revision, ButtonState.Resume, crop, d)) SetStatus("额度恢复，等待运行确认");
                    else { quotaPause.Phase = QuotaPhase.Waiting; SetStatus("点击前状态变化，已取消本次点击并重新观察"); }
                    return true;
                }
                if (!composer.Empty || quotaPause.ContinuationAttempted) { ScheduleReason(!composer.Empty ? "原对话有草稿" : "继续指令已尝试，请手动核对"); SetStatus("原对话有草稿或已尝试继续，请手动核对"); return true; }
                ScheduleReason(null);
                // Reserve before dispatch: timeout/partial write/unknown send may
                // never cause another write, fallback or submission next tick.
                quotaPause.ContinuationAttempted = true; quotaPause.Phase = QuotaPhase.Uncertain;
                DateTime submissionStarted = DateTime.UtcNow;
                ComposerReply submitted = await ReadComposer(connected, "submit", quotaPause.Context);
                if (!QuotaCurrent(connected, revision)) return true;
                if (submitted.Result == "Submitted")
                {
                    if (submitted.Running) { quotaPause.Phase = QuotaPhase.Resuming; quotaPause.ActionAt = submissionStarted; gate.MarkAttempt(submissionStarted); SetStatus("已补发继续指令，等待运行确认"); }
                    else { quotaPause.Reset(); CancelSchedule(false); SetStatus("继续指令已确认发送，当前对话已空闲"); }
                }
                else SetStatus(submitted.Result == "Blocked" ? "无法安全补发继续指令，请手动继续" : "额度操作结果不明，请手动核对；不会重复发送");
            }
            catch
            {
                if (QuotaCurrent(connected, revision))
                { if (quotaPause.Owned) quotaPause.Phase = QuotaPhase.Uncertain; quotaContextRetry = DateTime.UtcNow.AddMinutes(1); SetStatus("额度操作未完成，请手动核对；不会重复发送"); }
            }
            return true;
        }

        void AddQuotaSettings(FlowLayoutPanel panel)
        {
            panel.Controls.Add(UiLabel("额度自动暂停与恢复"));
            var enabled = new CheckBox { Name = "QuotaControl", Text = "启用额度控制（需开启自动恢复）", AutoSize = true, Checked = preferences.QuotaControl };
            var five = new CheckBox { Text = "5h", AutoSize = true, Checked = preferences.QuotaFiveHour };
            var week = new CheckBox { Text = "周额度", AutoSize = true, Checked = preferences.QuotaWeekly };
            var fiveThreshold = new NumericUpDown { Name = "FiveHourThreshold", Minimum = 0, Maximum = 99, Value = preferences.FiveHourThreshold, Width = 64 };
            var weekThreshold = new NumericUpDown { Name = "WeeklyThreshold", Minimum = 0, Maximum = 99, Value = preferences.WeeklyThreshold, Width = 64 };
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            row.Controls.Add(five); row.Controls.Add(UiLabel("剩余 ≤")); row.Controls.Add(fiveThreshold); row.Controls.Add(UiLabel("% 时暂停"));
            var weekRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            weekRow.Controls.Add(week); weekRow.Controls.Add(UiLabel("剩余 ≤")); weekRow.Controls.Add(weekThreshold); weekRow.Controls.Add(UiLabel("% 时暂停"));
            var prompt = new TextBox { Name = "ContinuePrompt", Multiline = true, AcceptsReturn = true, MaxLength = 2048, Width = 480, Height = 62, Text = preferences.ContinuePrompt, ScrollBars = ScrollBars.Vertical };
            panel.Controls.Add(enabled); panel.Controls.Add(row); panel.Controls.Add(weekRow);
            panel.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(490, 0), ForeColor = Color.DimGray,
                Text = "任一所选额度不足即暂停，全部恢复才继续\n等待时画面每分钟采样，额度正常每 10 秒查询\n后台输入不可用时允许切回 Codex 输入；已有草稿时等待" });
            panel.Controls.Add(UiLabel("无暂停三角形时补发的继续指令")); panel.Controls.Add(prompt);
            EventHandler changed = delegate {
                bool owned = quotaPause.Owned;
                CancelQuota(); preferences.QuotaFiveHour = five.Checked; preferences.QuotaWeekly = week.Checked;
                preferences.QuotaFiveHourThreshold = (int)fiveThreshold.Value;
                preferences.QuotaWeeklyThreshold = (int)weekThreshold.Value;
                preferences.QuotaControl = enabled.Checked && (five.Checked || week.Checked);
                if (owned) automatic.Checked = false;
                nextMonitor = DateTime.MinValue; SavePreferences();
            };
            enabled.CheckedChanged += changed; five.CheckedChanged += changed; week.CheckedChanged += changed; fiveThreshold.ValueChanged += changed; weekThreshold.ValueChanged += changed;
            prompt.TextChanged += delegate {
                CancelQuota(); if (automatic.Checked) automatic.Checked = false;
                string text = prompt.Text.Replace("\r\n", "\n");
                if (ComposerRequest.ValidPrompt(text)) { preferences.ContinuePrompt = text; SavePreferences(); }
            };
        }
    }
}
