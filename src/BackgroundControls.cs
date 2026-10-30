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
        string attemptedRetry;
        int controlFailures;
        bool controlRecoveryActionPending;
        Func<Target, string, ComposerContext, Task<ComposerReply>> controlReader = null;
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
                    var reply = controlReader != null ? await controlReader(connected, operation, context) : await ComposerBridge.RunAsync(connected, operation == "inspect" ? "observe" : operation, context, null, DateTime.UtcNow.AddSeconds(12), cancel.Token,
                        operation == "resume" && resumeSchedule.Active ? (DateTime?)resumeSchedule.DueUtc : null);
                    if (operation != "inspect") journal.Write("diagnostics.log", "control-action", "operation=" + operation + "; result=" + reply.Result + "; stage=" + reply.Stage +
                        "; attempted=" + reply.ActionAttempted + "; state=" + reply.State + "; source=" + (context == null ? null : context.ActionSource) + "; " + DiagnosticContext(connected), DateTime.UtcNow);
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
            scores.Text = "状态来源：" + reply.Context.Backend + " · " + Portable.StateName(reply.State) + "\n恢复入口：" + (reply.Context.ActionSource == "capacity-retry" ? "底部重试" : "输入区按钮") + "\n已核对原窗口、聊天与输入区；不是窗口截图";
            journal.Write("diagnostics.log", "control-observation", "backend=" + reply.Context.Backend + "; state=" + reply.State + "; safe=" + reply.Safe +
                "; input-not-read=True; action-source=" + reply.Context.ActionSource + "; binding-note=" + reply.BindingNote + "; observed-utc=" + reply.ObservedUtc.ToString("O") + "; conversation-sha256=" + CaptureEvidence.Hash(reply.Context.Conversation) +
                "; page=" + reply.Context.PageId + "; " + CaptureEvidence.TargetInfo(target, generation), now);
        }
        void ClearControlObservation()
        {
            var old = preview.Image; preview.Image = null; if (old != null) old.Dispose();
            scores.Text = "后台控件暂不可核验\n等待重新确认，不使用旧状态";
            gate.BreakStreak(); recoveryWindow.Reset();
        }
        bool ControlUnavailable(Target connected, string reason, bool mayHaveActed = false)
        {
            controlFailures++;
            controlRecoveryActionPending |= gate.PendingVerification || mayHaveActed;
            backgroundNeedsUser = true; ClearControlObservation();
            controlUnavailableUntil = DateTime.UtcNow.AddSeconds(Math.Min(30, 5 + 5 * (controlFailures / 6)));
            journal.Write("diagnostics.log", "control-unavailable", reason + "; had-binding=" + backgroundControlsActive + "; failures=" + controlFailures +
                "; next-read=" + (controlFailures >= 2 ? "rediscover" : "bound") + "; action-pending=" + controlRecoveryActionPending, DateTime.UtcNow);
            if (backgroundControlsActive || Native.IsIconic(connected.Window))
            { SetStatus(controlFailures >= 2 ? "后台连接暂不可用，正在自动重新核验窗口与聊天" : "后台控件暂不可核验，等待重新确认；不会使用旧截图点击"); return true; }
            return false;
        }
        async Task<bool> TickBackgroundControls(Target connected, int revision)
        {
            if (backgroundTarget != connected || backgroundRevision != revision)
            {
                backgroundContext = null; backgroundControlsActive = false; backgroundNeedsUser = true; backgroundTarget = connected; backgroundRevision = revision;
                nextControlRead = controlUnavailableUntil = DateTime.MinValue;
                controlFailures = 0; controlRecoveryActionPending = false;
            }
            if (connected.Demo) return false;
            DateTime now = DateTime.UtcNow;
            if (now < controlUnavailableUntil) return backgroundControlsActive;
            if (now < nextControlRead) return backgroundControlsActive;
            nextControlRead = now.AddMilliseconds(lastObservedState == ButtonState.Resume ? 200 : 1500);
            bool mutationInFlight = false;
            try
            {
                bool rediscover = controlFailures >= 2;
                var reply = await ReadControls(connected, "inspect", !rediscover && backgroundContext != null && backgroundContext.Backend == "CDP" ? backgroundContext : null);
                if (!Current(connected, revision)) return true;
                if (reply.Result != "Observed" || reply.Context == null)
                {
                    return ControlUnavailable(connected, "stage=" + reply.Stage + "; binding-note=" + reply.BindingNote);
                }
                bool same = ComposerContext.SameObservedConversation(backgroundContext, reply.Context);
                if (backgroundContext != null && !same && (backgroundContext.Backend != reply.Context.Backend || gate.PendingVerification || controlRecoveryActionPending))
                    return ControlUnavailable(connected, "stage=rediscovery-conversation-unverified; candidate-backend=" + reply.Context.Backend);
                if (backgroundContext != null && !same)
                {
                    gate.CancelPending(); recoveryWindow.Reset(); quotaQuiet.Reset();
                    if (quotaPause.Owned) { quotaPause.Phase = QuotaPhase.Uncertain; SetStatus("聊天已变化，请手动核对额度暂停"); return true; }
                }
                if (controlFailures > 0) journal.Write("diagnostics.log", "control-rebound", "failures=" + controlFailures + "; rediscovered=" + rediscover +
                    "; previous-backend=" + (backgroundContext == null ? "none" : backgroundContext.Backend) + "; backend=" + reply.Context.Backend +
                    "; same-conversation=" + same + "; " + DiagnosticContext(connected), DateTime.UtcNow);
                controlFailures = 0; controlUnavailableUntil = DateTime.MinValue;
                backgroundContext = reply.Context; backgroundControlsActive = true; backgroundNeedsUser = !reply.Safe;
                if (reply.State == ButtonState.Running) { attemptedRetry = null; controlRecoveryActionPending = false; }
                ShowControlObservation(reply);
                int verified = gate.Verified;
                ObserveSchedule(reply.State, reply.ObservedUtc);
                bool ready = gate.ObserveCaptured(reply.State, DateTime.UtcNow, reply.ObservedUtc); RecordVerified(verified);
                var detection = new Detection { State = reply.State };
                if (!reply.Safe) { recoveryWindow.Reset(); gate.BreakStreak(); SetStatus("等待当前任务的问答处理"); journal.Write("diagnostics.log", "control-blocked", "reason=" + reply.BlockReason, DateTime.UtcNow); return true; }
                if (controlRecoveryActionPending) { SetStatus("连接已恢复，等待原操作结果确认；不会重复投递"); return true; }
                if (await HandleQuota(connected, revision, detection, Rectangle.Empty, reply.ObservedUtc)) return true;
                if (gate.AwaitingRunning) { SetStatus("后台操作已投递，等待原聊天控件确认运行"); return true; }
                if (!ready || !automatic.Checked) { ObserveStatus(reply.State); return true; }
                if (!QuotaAllowsNormal() || !ScopeCheck(connected)) return true;
                string retryIdentity = backgroundContext.Conversation + ":" + backgroundContext.ActionToken;
                if (backgroundContext.ActionSource == "capacity-retry" && attemptedRetry == retryIdentity) { SetStatus("重试已投递，等待本次结果或新的失败状态"); return true; }
                InputSnapshot input = inputReader();
                if (!recoveryWindow.Ready(input, DateTime.UtcNow)) { SetStatus("等待鼠标、键盘空闲 1.5 秒"); return true; }
                DateTime actionAt = DateTime.UtcNow;
                mutationInFlight = true;
                var changed = await ReadControls(connected, "resume", backgroundContext);
                mutationInFlight = false;
                if (!Current(connected, revision)) return true;
                if (changed.ActionAttempted)
                {
                    if (backgroundContext.ActionSource == "capacity-retry") attemptedRetry = retryIdentity;
                    gate.MarkAttempt(actionAt);
                    if (changed.Result == "Changed" && changed.State == ButtonState.Running)
                    { int prior = gate.Verified; gate.ObserveCaptured(changed.State, DateTime.UtcNow, changed.ObservedUtc); RecordVerified(prior); ShowControlObservation(changed); ObserveStatus(changed.State); }
                    else { controlRecoveryActionPending = true; SetStatus("后台操作已投递，等待原聊天控件确认运行"); }
                }
                else if (changed.Result == "Uncertain") { controlRecoveryActionPending = true; gate.BreakStreak(); SetStatus("连接已恢复，等待原操作结果确认；不会重复投递"); }
                else { gate.BreakStreak(); SetStatus("后台操作前状态变化，重新核验原聊天"); }
                return true;
            }
            catch (Exception error)
            {
                if (!Current(connected, revision)) return true;
                return ControlUnavailable(connected, "error=" + error.GetType().Name, mutationInFlight);
            }
        }
    }
}
