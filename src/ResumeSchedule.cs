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
        DateTime observedPause, checkedUtc;
        double checkedClock;
        internal void Arm(DateTime due, string target, DateTime now, double clock, string zone)
        {
            if (due.Kind != DateTimeKind.Utc || due <= now || String.IsNullOrEmpty(target)) throw new ArgumentException("Invalid recovery time or target");
            Active = true; Invalid = false; DueUtc = due; TargetKey = target; Zone = zone;
            checkedUtc = now; checkedClock = clock; observedPause = DateTime.MinValue;
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
            if (!Active) return;
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
        static double ScheduleClock { get { return (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency; } }
        static string ScheduleZone { get { TimeZoneInfo.ClearCachedData(); return TimeZoneInfo.Local.Id; } }
        string ScheduleTarget { get { return target == null ? "" : target.Window + ":" + target.ProcessId + ":" + target.ProcessStart; } }
        string ScheduleText()
        {
            if (!resumeSchedule.Active) return UiText.Get("未设置定时恢复");
            string prefix = DateTime.UtcNow < resumeSchedule.DueUtc ? UiText.Get("等待指定时间") : UiText.Get("已到时间，等待恢复条件");
            return prefix + " · " + new DateTimeOffset(resumeSchedule.DueUtc).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");
        }
        void RefreshSchedule()
        {
            scheduleSummary.Text = ScheduleText(); tips.SetToolTip(scheduleSummary, scheduleSummary.Text);
            if (scheduleInfo != null && !scheduleInfo.IsDisposed) scheduleInfo.Text = ScheduleText() + "\n" + TimeZoneInfo.Local.DisplayName;
        }
        void CancelSchedule(bool disableAutomatic)
        {
            bool active = resumeSchedule.Active;
            resumeSchedule.Cancel();
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
        { resumeSchedule.Observe(state, at); RefreshSchedule(); }
        void AddScheduleSettings(FlowLayoutPanel panel)
        {
            panel.Controls.Add(UiLabel("定时恢复"));
            schedulePicker = new DateTimePicker { Name = "ResumeAt", Format = DateTimePickerFormat.Custom,
                CustomFormat = "yyyy-MM-dd HH:mm:ss", Width = 460, Value = DateTime.Now.AddHours(1) };
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
                gate.BreakStreak(); recoveryWindow.Reset(); quotaQuiet.Reset(); nextMonitor = DateTime.MinValue;
                RefreshSchedule(); SetStatus(automatic.Checked ? "等待指定时间" : "定时已设置，请开启自动恢复");
            }));
            row.Controls.Add(UiButton("取消定时", delegate { if (resumeSchedule.Active) { CancelSchedule(true); CancelQuota(); } SetStatus("定时恢复已取消"); }));
            panel.Controls.Add(row);
            scheduleInfo = new Label { AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(490, 0) };
            panel.Controls.Add(scheduleInfo);
            panel.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(490, 0), ForeColor = Color.DimGray,
                Text = "到点且额度满足后才允许恢复，不会主动暂停\n仅本次运行有效；停止监测、切换目标或退出即取消" });
            RefreshSchedule();
        }
    }
}
