using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CodexAutoResume
{
    internal sealed class WindowPicker : Form
    {
        public int SelectedIndex = -1;
        public WindowPicker(List<Target> windows)
        {
            Shown += delegate { UiText.Apply(this); };
            Text = "请选择要监测的 Codex 窗口"; Icon = DesktopIdentity.LoadIcon(); ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei UI", 10); ClientSize = new Size(640, 330); StartPosition = FormStartPosition.CenterParent;
            Label guide = WindowUi.Guide("检测到多个窗口，请选择一个。确认前不会点击；之后可用主窗口的“选择窗口”更换", 65, 12);
            ListBox list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
            for (int i = 0; i < windows.Count; i++)
            {
                Screen screen = Screen.FromHandle(windows[i].Window);
                list.Items.Add((UiText.English ? "Window " : "窗口 ") + (i + 1) + "  ·  " + Native.Title(windows[i].Window) + "  ·  " + (UiText.English ? (screen.Primary ? "Primary display" : "Secondary display") : (screen.Primary ? "主屏" : "副屏")));
            }
            list.SelectedIndex = -1;
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 55, Padding = new Padding(8), FlowDirection = FlowDirection.RightToLeft };
            Button confirm = new Button { Text = "使用所选窗口", AutoSize = true, Enabled = false };
            Button cancel = new Button { Text = "暂不选择", AutoSize = true, DialogResult = DialogResult.Cancel };
            list.SelectedIndexChanged += delegate { confirm.Enabled = list.SelectedIndex >= 0; };
            confirm.Click += delegate { if (list.SelectedIndex >= 0) { SelectedIndex = list.SelectedIndex; DialogResult = DialogResult.OK; Close(); } };
            actions.Controls.Add(confirm); actions.Controls.Add(cancel);
            Controls.Add(list); Controls.Add(actions); Controls.Add(guide); CancelButton = cancel;
        }
    }
}
