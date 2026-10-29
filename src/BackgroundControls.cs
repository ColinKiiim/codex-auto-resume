using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAutoResume
{
    internal sealed partial class MainForm
    {
        ComposerContext backgroundContext;
        Target backgroundTarget;
        int backgroundRevision = -1;
        bool backgroundControlsActive;
        bool backgroundNeedsUser;
        bool BackgroundNeedsUser { get { return background.Checked && backgroundControlsActive && backgroundNeedsUser; } }
        DateTime nextControlRead, controlUnavailableUntil;
        CancellationTokenSource controlCancellation;
        string controlAccount;
        bool controlMutating;
        QuotaLevel controlQuotaLevel;
        bool controlPreview;
        bool controlEvidenceBusy;
        DateTime nextControlEvidence;
        readonly System.Windows.Forms.Timer controlsTimer = new System.Windows.Forms.Timer { Interval = 200 };

        void StartControlPolling()
        {
            controlsTimer.Tick += delegate { if (monitoring && background.Checked && !IsDisposed) TickMonitor(); };
            FormClosed += delegate { controlsTimer.Dispose(); };
            controlsTimer.Start();
        }
        async void PulseControlEvidence()
        {
            if (!backgroundControlsActive || !monitoring || !background.Checked || !Evidence.Enabled || controlEvidenceBusy || target == null || target.Demo ||
                Native.IsIconic(target.Window) || DateTime.UtcNow < nextControlEvidence) return;
            Target connected = target; int revision = generation; controlEvidenceBusy = true; nextControlEvidence = DateTime.UtcNow.AddSeconds(15);
            try
            {
                using (var frame = await sampler.ReadActionAsync(connected.Window))
                    if (Current(connected, revision) && Evidence.Enabled)
                        Evidence.Record(frame, "control-image-comparison", DiagnosticContext(connected) + "; semantic-backend=" + backgroundContext.Backend +
                            "; semantic-state=" + lastObservedState + "; comparison-only=true", null, true);
            }
            catch (Exception error) { if (Current(connected, revision)) journal.Write("diagnostics.log", "control-image-comparison-unavailable", error.GetType().Name, DateTime.UtcNow); }
            finally { controlEvidenceBusy = false; }
        }

        async Task<ComposerReply> ReadControls(Target connected, string operation, ComposerContext context)
        {
            using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                controlCancellation = cancel; controlAccount = usageSnapshot == null ? null : usageSnapshot.AccountKey;
                controlMutating = operation != "inspect"; controlQuotaLevel = QuotaNow;
                try
                {
                    var reply = await ComposerBridge.RunAsync(connected, operation == "inspect" ? "observe" : operation, context, null, DateTime.UtcNow.AddSeconds(12), cancel.Token,
                        operation == "resume" && resumeSchedule.Active ? (DateTime?)resumeSchedule.DueUtc : null);
                    if (operation != "inspect") journal.Write("diagnostics.log", "control-action", "operation=" + operation + "; result=" + reply.Result + "; stage=" + reply.Stage +
                        "; attempted=" + reply.ActionAttempted + "; state=" + reply.State + "; " + DiagnosticContext(connected), DateTime.UtcNow);
                    return reply;
                }
                finally { if (controlCancellation == cancel) controlCancellation = null; }
            }
        }
        void ShowControlObservation(ComposerReply reply)
        {
            DateTime now = DateTime.UtcNow;
            activity.Start(now); activity.Control(reply.ObservedUtc); activity.Recognized(reply.State);
            WatchResume(reply.State);
            string resource = reply.State == ButtonState.Resume ? "resume" : reply.State == ButtonState.Running ? "running" : reply.State == ButtonState.Send ? "send" : reply.State == ButtonState.Voice ? "voice" : null;
            if (resource == null) { var old = preview.Image; preview.Image = null; if (old != null) old.Dispose(); }
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource + "-reference.png"))
            {
                if (stream != null) using (var image = new Bitmap(stream)) { var old = preview.Image; preview.Image = new Bitmap(image); if (old != null) old.Dispose(); }
            }
            controlPreview = true; previewContentAt = reply.ObservedUtc; SetPreviewKind(false);
            scores.Text = "状态来源：" + reply.Context.Backend + " · " + Portable.StateName(reply.State) + "\n已核对原窗口、聊天与输入区；不是窗口截图";
            journal.Write("diagnostics.log", "control-observation", "backend=" + reply.Context.Backend + "; state=" + reply.State + "; safe=" + reply.Safe +
                "; input-not-read=True; binding-note=" + reply.BindingNote + "; observed-utc=" + reply.ObservedUtc.ToString("O") + "; conversation-sha256=" + CaptureEvidence.Hash(reply.Context.Conversation) +
                "; page=" + reply.Context.PageId + "; " + CaptureEvidence.TargetInfo(target, generation), now);
        }
        async Task<bool> TickBackgroundControls(Target connected, int revision)
        {
            if (connected.Demo) return false;
            if (backgroundTarget != connected || backgroundRevision != revision)
            {
                backgroundContext = null; backgroundControlsActive = false; backgroundNeedsUser = true; backgroundTarget = connected; backgroundRevision = revision;
                nextControlRead = controlUnavailableUntil = DateTime.MinValue;
            }
            DateTime now = DateTime.UtcNow;
            if (now < controlUnavailableUntil) return backgroundControlsActive;
            if (now < nextControlRead) return backgroundControlsActive;
            nextControlRead = now.AddMilliseconds(lastObservedState == ButtonState.Resume ? 200 : 1500);
            try
            {
                var reply = await ReadControls(connected, "inspect", null);
                if (!Current(connected, revision)) return true;
                if (reply.Result != "Observed" || reply.Context == null)
                {
                    backgroundNeedsUser = true;
                    controlUnavailableUntil = DateTime.UtcNow.AddSeconds(5);
                    journal.Write("diagnostics.log", "control-unavailable", "stage=" + reply.Stage + "; binding-note=" + reply.BindingNote + "; had-binding=" + backgroundControlsActive, DateTime.UtcNow);
                    if (backgroundControlsActive || Native.IsIconic(connected.Window)) { SetStatus("后台控件暂不可核验，等待重新确认；不会使用旧截图点击"); return true; }
                    return false;
                }
                if (backgroundContext != null && !ComposerContext.SameConversation(backgroundContext, reply.Context))
                {
                    gate.CancelPending(); recoveryWindow.Reset(); quotaQuiet.Reset();
                    if (quotaPause.Owned) { quotaPause.Phase = QuotaPhase.Uncertain; SetStatus("聊天已变化，请手动核对额度暂停"); return true; }
                }
                backgroundContext = reply.Context; backgroundControlsActive = true; backgroundNeedsUser = !reply.Safe;
                ShowControlObservation(reply);
                int verified = gate.Verified;
                ObserveSchedule(reply.State, reply.ObservedUtc);
                bool ready = gate.ObserveCaptured(reply.State, DateTime.UtcNow, reply.ObservedUtc); RecordVerified(verified);
                var detection = new Detection { State = reply.State };
                if (!reply.Safe) { recoveryWindow.Reset(); gate.BreakStreak(); SetStatus("后台控件检测到问答或其他待处理状态，等待用户"); return true; }
                if (await HandleQuota(connected, revision, detection, Rectangle.Empty, reply.ObservedUtc)) return true;
                if (gate.AwaitingRunning) { SetStatus("后台操作已投递，等待原聊天控件确认运行"); return true; }
                if (!ready || !automatic.Checked) { ObserveStatus(reply.State); return true; }
                if (!QuotaAllowsNormal() || !ScopeCheck(connected)) return true;
                InputSnapshot input = inputReader();
                if (!recoveryWindow.Ready(input, DateTime.UtcNow)) { SetStatus("等待鼠标、键盘空闲 1.5 秒"); return true; }
                DateTime actionAt = DateTime.UtcNow;
                var changed = await ReadControls(connected, "resume", backgroundContext);
                if (!Current(connected, revision)) return true;
                if (changed.ActionAttempted)
                {
                    gate.MarkAttempt(actionAt);
                    if (changed.Result == "Changed" && changed.State == ButtonState.Running)
                    { int prior = gate.Verified; gate.ObserveCaptured(changed.State, DateTime.UtcNow, changed.ObservedUtc); RecordVerified(prior); ShowControlObservation(changed); ObserveStatus(changed.State); }
                    else SetStatus("后台操作已投递，等待原聊天控件确认运行");
                }
                else { gate.BreakStreak(); SetStatus("后台操作前状态变化，重新核验原聊天"); }
                return true;
            }
            catch (Exception error)
            {
                if (!Current(connected, revision)) return true;
                backgroundNeedsUser = true;
                journal.Write("diagnostics.log", "control-unavailable", "error=" + error.GetType().Name + "; had-binding=" + backgroundControlsActive, DateTime.UtcNow);
                controlUnavailableUntil = DateTime.UtcNow.AddSeconds(5);
                if (backgroundControlsActive || Native.IsIconic(connected.Window)) { SetStatus("后台控件暂不可核验，等待重新确认；不会使用旧截图点击"); return true; }
                return false;
            }
        }
    }
}
