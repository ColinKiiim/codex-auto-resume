using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexAutoResume
{
    public sealed class TimedInstruction
    {
        public string Id { get; set; }
        public string Prompt { get; set; }
        public DateTime DueUtc { get; set; }
        public string Zone { get; set; }
        public string State { get; set; }
        public string Title { get; set; }
        public ComposerContext Context { get; set; }
        public ScopeEntry Sidebar { get; set; }
        public string WaitReason { get; set; }
        public long Window { get; set; }
        public int ProcessId { get; set; }
        public long ProcessStart { get; set; }
        public string ProcessPath { get; set; }
        public string AccountKey { get; set; }
        internal DateTime NextAttempt, BoundUtc;
        internal double BoundClock;
        public override string ToString()
        { return DueUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + " · " + UiText.Get(State) + " · " + (Title ?? UiText.Get("未绑定")) + (State == "等待条件" && !String.IsNullOrEmpty(WaitReason) ? " · " + UiText.Get(WaitReason) : ""); }
    }

    internal static class TimedInstructionPolicy
    {
        internal static string TargetKey(TimedInstruction task)
        { return task.Window + ":" + task.ProcessId + ":" + task.ProcessStart + ":" + (task.Sidebar == null ? task.Context == null ? "" : task.Context.Conversation : task.Sidebar.Id); }
        internal static TimedInstruction NextDue(IEnumerable<TimedInstruction> tasks, DateTime now)
        {
            var all = tasks.ToArray();
            return all.Where(x => (x.State == "等待执行" || x.State == "等待条件") && x.Context != null)
                .GroupBy(TargetKey).Select(g => g.OrderBy(x => x.DueUtc).ThenBy(x => x.Id).First())
                .Where(x => x.DueUtc <= now && x.NextAttempt <= now && !all.Any(y => y.State == "结果不明，请核对" && TargetKey(y) == TargetKey(x)))
                .OrderBy(x => x.DueUtc).ThenBy(x => x.Id).FirstOrDefault();
        }
        internal static string OnLoad(string state)
        {
            if (state == "执行中") return "结果不明，请核对";
            return state == "等待执行" || state == "等待条件" ? "需重新绑定" : state;
        }
        internal static string AfterReply(string result)
        { return result == "Submitted" ? "已完成" : result == "Blocked" ? "等待条件" : "结果不明，请核对"; }
        internal static bool CanRebind(string state)
        { return state == "等待执行" || state == "等待条件" || state == "需重新绑定"; }
        internal static string AccountFingerprint(string account)
        {
            if (String.IsNullOrEmpty(account)) return null;
            using (var hash = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(account))).Replace("-", "").ToLowerInvariant();
        }
        internal static bool SameAccount(UsageSnapshot snapshot, UsageError error, string key, DateTime now)
        { return snapshot != null && error == UsageError.None && !String.IsNullOrEmpty(key) && AccountFingerprint(snapshot.AccountKey) == key && snapshot.FetchedUtc <= now && (now - snapshot.FetchedUtc).TotalSeconds <= 30; }
    }
    internal sealed partial class MainForm
    {
        readonly List<TimedInstruction> timedInstructions = new List<TimedInstruction>();
        bool timedLoaded, timedBusy, timedRefreshing;
        Func<Target, string, ComposerContext, string, DateTime, CancellationToken, DateTime?, Task<ComposerReply>> timedRunner = null;
        Task<ComposerReply> RunTimedComposer(Target target, string operation, ComposerContext context, string prompt, DateTime expiry, CancellationToken cancel, DateTime? due)
        { return timedRunner == null ? ComposerBridge.RunAsync(target, operation, context, prompt, expiry, cancel, due) : timedRunner(target, operation, context, prompt, expiry, cancel, due); }
        string timedAccount;
        ListBox timedList;
        DateTimePicker timedPicker;
        TextBox timedPrompt;
        Label timedInfo;
        string TimedPath { get { return Path.Combine(Portable.DataDirectory, "timed-instructions.json"); } }
        void LoadTimedInstructions()
        {
            if (timedLoaded) return; timedLoaded = true;
            try
            {
                if (!File.Exists(TimedPath)) return;
                var saved = JsonFile.Read<List<TimedInstruction>>(TimedPath);
                if (saved == null) return;
                foreach (var task in saved)
                {
                    if (task == null || String.IsNullOrEmpty(task.Id) || !ComposerRequest.ValidPrompt(task.Prompt)) continue;
                    task.State = TimedInstructionPolicy.OnLoad(task.State);
                    task.Context = null; timedInstructions.Add(task);
                }
                SaveTimedInstructions();
            }
            catch (Exception e) { journal.Write("diagnostics.log", "timed-load-error", e.GetType().Name, DateTime.UtcNow); }
        }
        void SaveTimedInstructions()
        { Directory.CreateDirectory(Portable.DataDirectory); JsonFile.Write(TimedPath, timedInstructions); }
        void RefreshTimedList(TimedInstruction selected)
        {
            if (timedList == null || timedList.IsDisposed) return;
            var previous = timedList.SelectedItem as TimedInstruction;
            timedRefreshing = true; timedList.BeginUpdate(); timedList.Items.Clear();
            foreach (var task in timedInstructions.OrderBy(x => x.DueUtc)) timedList.Items.Add(task);
            if (selected != null || previous != null) timedList.SelectedItem = selected ?? previous;
            timedList.EndUpdate(); timedRefreshing = false;
        }
        void AddTimedInstructions(FlowLayoutPanel panel)
        {
            LoadTimedInstructions();
            panel.Controls.Add(UiLabel("一次性任务指令"));
            panel.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(490, 0), ForeColor = Color.DimGray,
                Text = "先在 Codex 打开目标聊天，再绑定当前聊天。到点后等待空框、额度与输入安全条件\n重启后需重新绑定；结果不明的任务不会自动重试" });
            timedList = new ListBox { Name = "TimedInstructions", Width = 480, Height = 100, HorizontalScrollbar = true };
            panel.Controls.Add(timedList);
            timedPicker = new DateTimePicker { Name = "TimedInstructionAt", Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm:ss", Width = 460, Value = DateTime.Now.AddHours(1) };
            panel.Controls.Add(timedPicker);
            timedPrompt = new TextBox { Name = "TimedInstructionPrompt", Multiline = true, AcceptsReturn = true, MaxLength = 2048, Width = 480, Height = 70, ScrollBars = ScrollBars.Vertical, Text = preferences.ContinuePrompt };
            panel.Controls.Add(timedPrompt);
            timedInfo = new Label { AutoSize = true, MaximumSize = new Size(490, 0), ForeColor = Color.DimGray, Text = "修改后点击保存；每个任务保存自己的指令" };
            panel.Controls.Add(timedInfo);
            timedList.SelectedIndexChanged += delegate {
                if (timedRefreshing) return;
                var task = timedList.SelectedItem as TimedInstruction;
                if (task == null) return;
                timedPicker.Value = task.DueUtc.ToLocalTime(); timedPrompt.Text = task.Prompt;
                timedInfo.Text = "目标：" + (task.Title ?? "未绑定") + " · " + task.State;
            };
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            row.Controls.Add(UiButton("新增并绑定当前聊天", async delegate { await BindTimedInstruction(null); }));
            row.Controls.Add(UiButton("保存并绑定选中任务", async delegate {
                var task = timedList.SelectedItem as TimedInstruction;
                if (task != null) await BindTimedInstruction(task);
            }));
            row.Controls.Add(UiButton("取消选中任务", delegate {
                var task = timedList.SelectedItem as TimedInstruction;
                if (task == null || task.State == "执行中") return;
                if (timedBusy) { timedInfo.Text = "请等待当前操作结束后再取消任务"; return; }
                task.State = "已取消"; task.Context = null;
                try { SaveTimedInstructions(); } catch { timedInfo.Text = "保存失败，请检查本地存储"; }
                RefreshTimedList(task);
            }));
            panel.Controls.Add(row); RefreshTimedList(null);
        }
        async Task BindTimedInstruction(TimedInstruction task)
        {
            if (timedBusy || updating || quotaCancellation != null) return;
            if (task != null && !TimedInstructionPolicy.CanRebind(task.State))
            { timedInfo.Text = "已完成、已取消或结果不明的任务不能重新执行；请新建任务"; return; }
            string prompt = timedPrompt.Text.Replace("\r\n", "\n");
            DateTime local = DateTime.SpecifyKind(timedPicker.Value, DateTimeKind.Unspecified);
            if (!ComposerRequest.ValidPrompt(prompt)) { timedInfo.Text = "请输入有效的任务指令"; return; }
            if (TimeZoneInfo.Local.IsInvalidTime(local) || TimeZoneInfo.Local.IsAmbiguousTime(local)) { timedInfo.Text = "请选择没有夏令时歧义的时间"; return; }
            DateTime due = TimeZoneInfo.ConvertTimeToUtc(local, TimeZoneInfo.Local);
            if (due <= DateTime.UtcNow) { timedInfo.Text = "请选择未来时间"; return; }
            if (target == null || !monitoring || target.Demo) { timedInfo.Text = "请先连接真实 Codex 并开启监测"; return; }
            if (usageSnapshot == null || usageError != UsageError.None || String.IsNullOrEmpty(usageSnapshot.AccountKey) || (DateTime.UtcNow - usageSnapshot.FetchedUtc).TotalSeconds < 0 || (DateTime.UtcNow - usageSnapshot.FetchedUtc).TotalSeconds > 30)
            { timedInfo.Text = "等待当前账号核验后再绑定"; return; }
            Target connected = target; int revision = generation; string account = usageSnapshot.AccountKey;
            Label feedback = timedInfo;
            timedBusy = true;
            try
            {
                var reply = await ComposerBridge.RunAsync(connected, "inspect", null, prompt, DateTime.UtcNow.AddSeconds(25), lifetime.Token);
                if (feedback == null || feedback.IsDisposed || timedInfo != feedback) return;
                if (target != connected || revision != generation || !monitoring || usageSnapshot == null || usageSnapshot.AccountKey != account || reply.Result != "Observed" || reply.Context == null)
                { timedInfo.Text = "当前聊天或账号无法确认，未保存任务"; return; }
                var side = await Task.Run(() => ScopeBridge.Run(connected, "read", null, null, null, 0, 4000));
                if (feedback.IsDisposed || timedInfo != feedback) return;
                if (target != connected || revision != generation || !monitoring || usageSnapshot == null || usageSnapshot.AccountKey != account || side.Result != "Observed" || side.CurrentTitle != reply.Context.Title || side.Entries == null)
                { timedInfo.Text = "侧栏目标无法确认，未保存任务"; return; }
                var sidebar = side.Entries.Single(x => x.Kind == "chat" && x.Id == side.CurrentId);
                if (sidebar.Ambiguous) { timedInfo.Text = "存在同名聊天，未保存任务"; return; }
                if (reply.Context.Backend == "UIA" && !reply.Context.Conversation.EndsWith(":" + sidebar.Id, StringComparison.Ordinal))
                { timedInfo.Text = "当前聊天证据不一致，未保存任务"; return; }
                var value = task ?? new TimedInstruction { Id = Guid.NewGuid().ToString("N") };
                value.Sidebar = sidebar; value.WaitReason = null;
                value.Prompt = prompt; value.DueUtc = due; value.Zone = ScheduleZone; value.State = "等待执行"; value.Title = reply.Context.Title;
                value.BoundUtc = DateTime.UtcNow; value.BoundClock = ScheduleClock;
                value.Context = reply.Context; value.Window = connected.Window.ToInt64(); value.ProcessId = connected.ProcessId; value.ProcessStart = connected.ProcessStart; value.ProcessPath = connected.ProcessPath; value.AccountKey = TimedInstructionPolicy.AccountFingerprint(account);
                if (task == null) timedInstructions.Add(value);
                try { SaveTimedInstructions(); }
                catch { value.State = "需重新绑定"; value.Context = null; timedInfo.Text = "保存失败，任务不会执行"; RefreshTimedList(value); return; }
                timedInfo.Text = "已绑定当前聊天并保存"; RefreshTimedList(value);
            }
            catch (Exception e) { if (feedback != null && !feedback.IsDisposed) feedback.Text = "绑定未完成：" + e.GetType().Name; }
            finally { timedBusy = false; }
        }
        bool TimedCurrent(TimedInstruction task, Target connected, int revision)
        {
            string why;
            return !IsDisposed && monitoring && automatic.Checked && target == connected && generation == revision && connected.ValidateProcessIdentity(out why) &&
                connected.Window.ToInt64() == task.Window && connected.ProcessId == task.ProcessId && connected.ProcessStart == task.ProcessStart &&
                TimedInstructionPolicy.SameAccount(usageSnapshot, usageError, task.AccountKey, DateTime.UtcNow) &&
                (!preferences.QuotaControl || QuotaNow == QuotaLevel.Healthy) && !questions.Waiting && !quotaPause.Owned && !gate.AwaitingRunning && ScheduleZone == task.Zone;
        }
        async void TickTimedInstructions()
        {
            LoadTimedInstructions();
            if (timedBusy || updating || quotaCancellation != null || !monitoring || !automatic.Checked || target == null || diagnosing) return;
            DateTime now = DateTime.UtcNow;
            var task = TimedInstructionPolicy.NextDue(timedInstructions, now);
            if (task == null) return;
            Target connected = target; int revision = generation; task.NextAttempt = now.AddSeconds(15);
            if (task.BoundUtc == DateTime.MinValue || Math.Abs((now - task.BoundUtc).TotalSeconds - (ScheduleClock - task.BoundClock)) > 5 || task.Zone != ScheduleZone)
            {
                task.State = "需重新绑定"; task.Context = null;
                try { SaveTimedInstructions(); } catch { } RefreshTimedList(null); return;
            }
            if (!TimedCurrent(task, connected, revision) || !ScopeAllowsEntry(connected, task.Sidebar)) { task.State = "等待条件"; task.WaitReason = "等待目标、账号或已启用的安全条件"; RefreshTimedList(null); return; }
            InputSnapshot input = inputReader();
            if (!input.Valid || input.Held || input.IdleMilliseconds < RecoveryWindow.QuietMilliseconds) { task.State = "等待条件"; RefreshTimedList(null); return; }
            timedBusy = true; updating = true; timedAccount = task.AccountKey;
            using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                quotaCancellation = cancellation; quotaOperationLevel = QuotaNow;
                bool dispatched = false, navigationAttempted = false;
                try
                {
                    if (task.Sidebar != null)
                    {
                        var side = await Task.Run(() => ScopeBridge.Run(connected, "read", null, null, null, 0, 4000));
                        if (!TimedCurrent(task, connected, revision) || cancellation.IsCancellationRequested || side.Result != "Observed" || side.Entries == null ||
                            !side.Entries.Any(x => x.Kind == "chat" && x.Id == task.Sidebar.Id && x.Title == task.Sidebar.Title && x.ProjectId == task.Sidebar.ProjectId && x.Project == task.Sidebar.Project))
                        { task.State = "等待条件"; task.WaitReason = "等待原聊天身份核验"; return; }
                        if (side.CurrentId != task.Sidebar.Id)
                        {
                            var beforeSwitch = inputReader();
                            if (!beforeSwitch.Valid || beforeSwitch.Held || beforeSwitch.Tick != input.Tick || beforeSwitch.IdleMilliseconds < RecoveryWindow.QuietMilliseconds)
                            { task.State = "等待条件"; task.WaitReason = "等待输入空闲"; return; }
                            navigationAttempted = true;
                            var switched = await Task.Run(() => ScopeBridge.Run(connected, "switch", side.CurrentId, task.Sidebar.Id, task.Sidebar.ProjectId, input.Tick, 14000));
                            if (switched.Result == "Uncertain")
                            {
                                task.State = "结果不明，请核对"; task.WaitReason = "聊天切换结果不明"; SaveTimedInstructions();
                                automatic.Checked = false; return;
                            }
                            task.State = "等待条件"; task.WaitReason = switched.Result == "Switched" ? "已切换目标，等待重新核验" : "等待目标聊天可切换";
                            if (switched.Result == "Switched") { gate.BreakStreak(); recoveryWindow.Reset(); ResetPreview(); nextMonitor = DateTime.MinValue; task.NextAttempt = DateTime.UtcNow.AddSeconds(2); }
                            SaveTimedInstructions(); return;
                        }
                    }
                    if (!ScopeCheck(connected)) { task.State = "等待条件"; task.WaitReason = "等待指定范围核验"; return; }
                    DateTime expiry = usageSnapshot.FetchedUtc.AddSeconds(30);
                    var observed = await RunTimedComposer(connected, "inspect", null, task.Prompt, expiry, cancellation.Token, task.DueUtc);
                    if (!TimedCurrent(task, connected, revision) || cancellation.IsCancellationRequested || observed.Result != "Observed" || !(ComposerContext.SameConversation(task.Context, observed.Context) || task.Sidebar != null && observed.Context != null && observed.Context.Backend == "UIA" && observed.Context.Title == task.Sidebar.Title && observed.Context.Conversation.EndsWith(":" + task.Sidebar.Id, StringComparison.Ordinal)) || !observed.Safe || !observed.Empty || observed.Running)
                    { task.State = "等待条件"; task.WaitReason = observed.Result != "Observed" ? "等待原聊天输入结构" : !observed.Safe ? "等待问答或附件处理" : !observed.Empty ? "原聊天有草稿" : observed.Running ? "等待聊天空闲" : "等待目标、账号或已启用的安全条件"; return; }
                    var after = inputReader();
                    if (!input.Valid || !after.Valid || after.Held || after.Tick != input.Tick || after.IdleMilliseconds < RecoveryWindow.QuietMilliseconds)
                    { task.State = "等待条件"; return; }
                    // Durably reserve before any write. A crash, timeout or lost
                    // acknowledgement can never replay an instruction on restart.
                    task.Context = observed.Context;
                    task.State = "执行中"; task.WaitReason = null; SaveTimedInstructions();
                    if (!TimedCurrent(task, connected, revision) || !ScopeCheck(connected) || cancellation.IsCancellationRequested) { task.State = "等待条件"; SaveTimedInstructions(); return; }
                    dispatched = true;
                    var result = await RunTimedComposer(connected, "submit", task.Context, task.Prompt, usageSnapshot.FetchedUtc.AddSeconds(30), cancellation.Token, task.DueUtc);
                    task.State = TimedInstructionPolicy.AfterReply(result.Result); task.WaitReason = result.Result == "Blocked" ? "输入前安全核验未通过" : null;
                    if (task.State == "已完成") task.Context = null;
                    SaveTimedInstructions();
                }
                catch (Exception e)
                {
                    task.State = dispatched || navigationAttempted ? "结果不明，请核对" : "等待条件";
                    try { SaveTimedInstructions(); } catch { task.State = "结果不明，请核对"; }
                    journal.Write("diagnostics.log", "timed-instruction-error", e.GetType().Name + "; dispatched=" + dispatched, DateTime.UtcNow);
                }
                finally
                {
                    if (quotaCancellation == cancellation) quotaCancellation = null;
                    timedAccount = null; timedBusy = false; updating = false; RefreshTimedList(null);
                }
            }
        }
    }
}
