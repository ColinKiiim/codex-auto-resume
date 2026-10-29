using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace CodexAutoResume
{
    // One-shot, in-memory intent. Saving a date is never permission to replay
    // an action after restart, update, or target replacement.
    internal sealed class ResumeSchedule
    {
        internal bool Active, Invalid;
        internal DateTime DueUtc;
        internal string TargetKey, Zone;
        DateTime observedPause, checkedUtc, armedUtc;
        double checkedClock;
        internal void Arm(DateTime due, string target, DateTime now, double clock, string zone)
        {
            if (due.Kind != DateTimeKind.Utc || due <= now || String.IsNullOrEmpty(target)) throw new ArgumentException("Invalid recovery time or target");
            Active = true; Invalid = false; DueUtc = due; TargetKey = target; Zone = zone;
            armedUtc = checkedUtc = now; checkedClock = clock; observedPause = DateTime.MinValue;
        }
        internal bool Allows(DateTime now, double clock, string target, string zone)
        {
            if (!Active) return true;
            if (target != TargetKey || zone != Zone || Math.Abs((now - checkedUtc).TotalSeconds - (clock - checkedClock)) > 5) Invalid = true;
            checkedUtc = now; checkedClock = clock;
            return !Invalid && now >= DueUtc;
        }
        internal void Observe(ButtonState state, DateTime contentAt)
        {
            if (!Active || contentAt < armedUtc) return;
            if (state == ButtonState.Resume || state == ButtonState.Send) observedPause = contentAt;
            if (state == ButtonState.Running && observedPause != DateTime.MinValue && contentAt > observedPause) Cancel();
        }
        internal void Cancel() { Active = Invalid = false; observedPause = DateTime.MinValue; }
    }

    internal sealed partial class MainForm
    {
        readonly ResumeSchedule resumeSchedule = new ResumeSchedule();
        readonly Label scheduleSummary = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = Color.DimGray, Margin = Padding.Empty };
        DateTimePicker schedulePicker;
        Label scheduleInfo;
        bool scheduleDraft;
        string lastScheduleEvent, scheduleReason;
        DateTime scheduleReasonAt;
        int scheduleReasonGeneration;
        void ScheduleReason(string reason) { scheduleReason = reason; scheduleReasonAt = DateTime.UtcNow; scheduleReasonGeneration = generation; }
        static double ScheduleClock { get { return (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency; } }
        static string ScheduleZone { get { TimeZoneInfo.ClearCachedData(); return TimeZoneInfo.Local.Id; } }
        string ScheduleTarget { get { return target == null ? "" : target.Window + ":" + target.ProcessId + ":" + target.ProcessStart; } }
        string ScheduleState()
        {
            if (!resumeSchedule.Active) return "未设置定时恢复";
            if (!monitoring) return "监测未开启";
            if (!automatic.Checked) return "计划已保存，未启用执行";
            if (DateTime.UtcNow < resumeSchedule.DueUtc) return "等待指定时间";
            if (quotaPause.Phase == QuotaPhase.Uncertain) return "操作结果不明，请手动核对";
            if (gate.AwaitingRunning || quotaPause.Phase == QuotaPhase.Resuming) return "已发出操作，等待确认";
            if (questions.Waiting) return "等待用户回答";
            if (QuotaEnabled && QuotaNow != QuotaLevel.Healthy) return "等待可确认的额度";
            InputSnapshot input = inputReader();
            if (!input.Valid) return "输入状态尚未核实";
            if (input.Held || input.IdleMilliseconds < RecoveryWindow.QuietMilliseconds) return "等待输入空闲";
            if (scheduleReason != null && scheduleReasonGeneration == generation && (DateTime.UtcNow - scheduleReasonAt).TotalSeconds < 15) return scheduleReason;
            DateTime observedAt = backgroundControlsActive ? activity.LastSample : activity.LastContent;
            if (observedAt != DateTime.MinValue && (DateTime.UtcNow - observedAt).TotalSeconds >= 0 && (DateTime.UtcNow - observedAt).TotalSeconds <= 5 &&
                (lastObservedState == ButtonState.Voice || lastObservedState == ButtonState.VoiceClose))
                return quotaPause.Owned ? "等待原对话继续入口核验" : "没有暂停三角形；此恢复计划不会主动发送消息";
            return "已到时间，等待恢复条件（具体原因未核实）";
        }
        string ScheduleText()
        {
            string state = ScheduleState();
            if (!resumeSchedule.Active) return UiText.Get(state);
            return UiText.Get(state) + " · " + resumeSchedule.DueUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");
        }
        void RefreshSchedule()
        {
            scheduleSummary.Text = ScheduleText(); tips.SetToolTip(scheduleSummary, scheduleSummary.Text);
            if (scheduleInfo != null && !scheduleInfo.IsDisposed)
                scheduleInfo.Text = ScheduleText() + "\n" + TimeZoneInfo.Local.DisplayName + (scheduleDraft ? "\n输入时间有未保存的修改" : "");
            string current = resumeSchedule.Active ? ScheduleState() : null;
            if (current != lastScheduleEvent)
            {
                lastScheduleEvent = current;
                if (current != null) ScheduleEvent("state", current);
            }
        }
        void ScheduleEvent(string action, string reason)
        {
            string identity;
            using (var hash = System.Security.Cryptography.SHA256.Create())
                identity = BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(resumeSchedule.TargetKey ?? ""))).Replace("-", "").Substring(0, 12);
            journal.Write("diagnostics.log", "schedule-" + action, "reason=" + reason + "; target=" + identity + "; due-utc=" + resumeSchedule.DueUtc.ToString("O") +
                "; offset=" + TimeZoneInfo.Local.GetUtcOffset(resumeSchedule.DueUtc) + "; automatic=" + automatic.Checked, DateTime.UtcNow);
        }
        void CancelSchedule(bool disableAutomatic, string reason = "canceled")
        {
            bool active = resumeSchedule.Active;
            if (active) ScheduleEvent(reason == "completed" ? "completed" : "canceled", reason);
            resumeSchedule.Cancel(); scheduleReason = null;
            if (active && disableAutomatic && automatic.Checked) automatic.Checked = false;
            RefreshSchedule();
        }
        bool ScheduleAllows()
        {
            bool allowed = resumeSchedule.Allows(DateTime.UtcNow, ScheduleClock, ScheduleTarget, ScheduleZone);
            if (resumeSchedule.Invalid)
            {
                CancelSchedule(true); SetStatus("时间或目标已变化，定时恢复已取消，请重新设置"); return false;
            }
            if (!allowed) SetStatus("等待指定时间");
            return allowed;
        }
        void ObserveSchedule(ButtonState state, DateTime at)
        { bool active = resumeSchedule.Active; resumeSchedule.Observe(state, at); if (active && !resumeSchedule.Active) ScheduleEvent("ended", "fresh-running-observed"); RefreshSchedule(); }
        void AddScheduleSettings(FlowLayoutPanel panel)
        {
            panel.Controls.Add(UiStyle.Section("定时恢复"));
            schedulePicker = new DateTimePicker { Name = "ResumeAt", Format = DateTimePickerFormat.Custom,
                CustomFormat = "yyyy-MM-dd HH:mm:ss", Width = 460, Value = resumeSchedule.Active ? resumeSchedule.DueUtc.ToLocalTime() : DateTime.Now.AddHours(1) };
            scheduleDraft = false;
            schedulePicker.ValueChanged += delegate { scheduleDraft = true; RefreshSchedule(); };
            panel.Controls.Add(schedulePicker);
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            row.Controls.Add(UiButton("设置一次性恢复时间", delegate {
                DateTime local = DateTime.SpecifyKind(schedulePicker.Value, DateTimeKind.Unspecified);
                if (TimeZoneInfo.Local.IsInvalidTime(local) || TimeZoneInfo.Local.IsAmbiguousTime(local))
                { UiDialog.Show(UpdateDialogOwner(), "请选择无夏令时歧义的有效时间", "定时恢复"); return; }
                DateTime due = TimeZoneInfo.ConvertTimeToUtc(local, TimeZoneInfo.Local);
                if (due <= DateTime.UtcNow) { UiDialog.Show(UpdateDialogOwner(), "请选择未来的日期和时间", "定时恢复"); return; }
                if (target == null || !monitoring) { UiDialog.Show(UpdateDialogOwner(), "请先连接目标并开启监测", "定时恢复"); return; }
                if (updating || gate.AwaitingRunning || quotaCancellation != null || quotaPause.Phase == QuotaPhase.Stopping || quotaPause.Phase == QuotaPhase.Resuming)
                { UiDialog.Show(UpdateDialogOwner(), "请等待当前操作确认后再设置定时", "定时恢复"); return; }
                bool replacing = resumeSchedule.Active;
                CancelSchedule(replacing);
                resumeSchedule.Arm(due, ScheduleTarget, DateTime.UtcNow, ScheduleClock, ScheduleZone);
                scheduleDraft = false;
                ScheduleEvent("created", replacing ? "replaced" : "created");
                gate.BreakStreak(); recoveryWindow.Reset(); quotaQuiet.Reset(); nextMonitor = DateTime.MinValue;
                RefreshSchedule(); SetStatus(automatic.Checked ? "等待指定时间" : "定时已设置，请开启自动恢复");
            }));
            row.Controls.Add(UiButton("取消定时", delegate { if (resumeSchedule.Active) { CancelSchedule(true); CancelQuota(); } SetStatus("定时恢复已取消"); }));
            panel.Controls.Add(row);
            scheduleInfo = new Label { AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(490, 0) };
            panel.Controls.Add(scheduleInfo);
            panel.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(490, 0), ForeColor = Color.DimGray,
                Text = "到点后仍须满足已启用的恢复条件，不会主动暂停或发送任务指令\n仅本次运行有效；停止监测、切换目标或退出即取消" });
            RefreshSchedule();
        }
    }
}
