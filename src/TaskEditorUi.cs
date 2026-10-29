using System;
using System.Drawing;
using System.Windows.Forms;

namespace CodexAutoResume
{
    internal sealed partial class MainForm
    {
        void AddTimedInstructions(FlowLayoutPanel panel)
        {
            LoadTimedInstructions(); panel.Controls.Add(UiStyle.Section("一次性任务指令"));
            panel.Controls.Add(UiStyle.Help("先在 Codex 打开目标聊天，再绑定当前聊天。到点后等待空框、额度与输入安全条件\n重启后需重新绑定；结果不明的任务不会自动重试"));
            timedList = new ListBox { Name = "TimedInstructions", Width = 480, Height = 120, HorizontalScrollbar = true, IntegralHeight = false };
            panel.Controls.Add(timedList);
            var actions = UiStyle.Actions();
            actions.Controls.Add(UiButton("新增任务", delegate { OpenTimedInstructionEditor(null); }));
            actions.Controls.Add(UiButton("编辑并绑定任务", delegate { var task = timedList.SelectedItem as TimedInstruction; if (task != null) OpenTimedInstructionEditor(task); }));
            actions.Controls.Add(UiButton("取消选中任务", delegate {
                var task = timedList.SelectedItem as TimedInstruction;
                if (task == null || task.State == "执行中" || timedBusy) return;
                task.State = "已取消"; task.Context = null;
                try { SaveTimedInstructions(); } catch { UiDialog.Show(this, "保存失败，请检查本地存储", "一次性任务指令"); }
                RefreshTimedList(task);
            }));
            panel.Controls.Add(actions); RefreshTimedList(null);
        }

        void OpenTimedInstructionEditor(TimedInstruction task)
        {
            if (timedBusy) return;
            if (task != null && !TimedInstructionPolicy.CanRebind(task.State))
            { UiDialog.Show(this, "已完成、已取消或结果不明的任务不能重新执行；请新建任务", "一次性任务指令"); return; }
            using (var editor = new Form { Text = UiText.Get(task == null ? "新增任务" : "编辑并绑定任务"), Font = Font, Icon = Icon, AutoScaleMode = AutoScaleMode.Dpi, AutoScaleDimensions = new SizeF(96, 96), ClientSize = new Size(560, 400), MinimumSize = new Size(430, 340), BackColor = UiStyle.Background })
            {
                var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(20) };
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.Controls.Add(UiLabel("执行时间"), 0, 0);
                timedPicker = new DateTimePicker { Name = "TimedInstructionAt", Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm:ss", Dock = DockStyle.Fill, Value = task == null ? DateTime.Now.AddHours(1) : task.DueUtc.ToLocalTime() };
                root.Controls.Add(timedPicker, 0, 1); root.Controls.Add(UiLabel("任务指令"), 0, 2);
                timedPrompt = new EditorTextBox { Name = "TimedInstructionPrompt", Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, MaxLength = 2048, ScrollBars = ScrollBars.Vertical, Text = task == null ? preferences.ContinuePrompt : task.Prompt };
                root.Controls.Add(timedPrompt, 0, 3);
                timedInfo = UiStyle.Help("保存时核验并绑定当前聊天；每个任务保存自己的指令"); timedInfo.Dock = DockStyle.Top; root.Controls.Add(timedInfo, 0, 4);
                var actions = UiStyle.Actions(); actions.Dock = DockStyle.Fill;
                bool saving = false, finished = false;
                string original = timedPrompt.Text; DateTime originalTime = timedPicker.Value;
                Func<bool> dirty = () => timedPrompt.Text != original || timedPicker.Value.Ticks / TimeSpan.TicksPerSecond != originalTime.Ticks / TimeSpan.TicksPerSecond;
                Button save = null;
                Func<System.Threading.Tasks.Task> saveTask = async delegate {
                    if (saving) return;
                    saving = true; save.Enabled = false;
                    try { if (await BindTimedInstruction(task)) { finished = true; editor.Close(); } }
                    finally { saving = false; if (!save.IsDisposed) save.Enabled = true; }
                };
                save = UiButton("保存并绑定当前聊天", async delegate { await saveTask(); }); save.Name = "SaveTimedInstruction"; actions.Controls.Add(save);
                Button cancel = UiButton("取消", delegate { editor.Close(); }); actions.Controls.Add(cancel); editor.CancelButton = cancel;
                root.Controls.Add(actions, 0, 5); editor.Controls.Add(root);
                editor.FormClosing += async delegate(object sender, FormClosingEventArgs e) {
                    if (finished) return;
                    if (saving) { e.Cancel = true; return; }
                    if (!dirty()) return;
                    DialogResult result = UiDialog.Show(editor, "任务有未保存的修改，是否保存并绑定？\n选择“否”放弃修改，选择“取消”继续编辑", "未保存的修改", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    if (result == DialogResult.Cancel) e.Cancel = true;
                    if (result == DialogResult.Yes) { e.Cancel = true; await saveTask(); }
                };
                editor.SizeChanged += delegate { timedInfo.MaximumSize = new Size(Math.Max(100, root.ClientSize.Width - root.Padding.Horizontal), 0); };
                UiText.Apply(editor); WindowUi.Modal(editor, this);
                timedPicker = null; timedPrompt = null; timedInfo = null;
            }
        }
    }
}
