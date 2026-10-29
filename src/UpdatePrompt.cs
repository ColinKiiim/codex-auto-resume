using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CodexAutoResume
{
    internal sealed class ReleaseSummary
    {
        internal readonly List<string> Items = new List<string>();
        internal string Limitation = "";
        static bool Han(string value) { return Regex.IsMatch(value, @"[\u3400-\u9fff]"); }
        static string Plain(string value)
        {
            value = Regex.Replace(value, @"\[([^\]]+)\]\([^)]*\)", "$1");
            value = Regex.Replace(value, @"<[^>]+>", "");
            return value.Replace("**", "").Replace("`", "").Trim().TrimEnd('。');
        }
        internal static ReleaseSummary Parse(string markdown, bool english)
        {
            ReleaseSummary result = new ReleaseSummary();
            List<string> chosen = new List<string>(), fallback = new List<string>();
            string language = "", section = "";
            foreach (string raw in (markdown ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("#"))
                {
                    string heading = line.TrimStart('#', ' ').Trim();
                    if (heading == "中文") { language = "zh"; section = ""; continue; }
                    if (heading.Equals("English", StringComparison.OrdinalIgnoreCase)) { language = "en"; section = ""; continue; }
                    section = heading;
                    if (heading.Contains("Known limitation") || heading.Contains("已知限制") || heading.Contains("已知问题") || heading.Contains("Known issue")) language = "";
                    continue;
                }
                if (String.IsNullOrWhiteSpace(line) || line.StartsWith("发布日期") || line.StartsWith("Release date") || line.StartsWith("---")) continue;
                bool limitation = section.Contains("已知") || section.IndexOf("Known", StringComparison.OrdinalIgnoreCase) >= 0;
                bool selected = language == (english ? "en" : "zh") || (language == "" && (english ? !Han(line) : Han(line)));
                string text = Plain(line.TrimStart('-', '*', '•', ' '));
                if (text.Length == 0) continue;
                if (text.Length > 360) text = text.Substring(0, 357) + "…";
                if (limitation) { if (selected) result.Limitation += (result.Limitation == "" ? "" : "\n") + text; continue; }
                if (line.StartsWith("- ") || line.StartsWith("* ") || line.StartsWith("• "))
                { fallback.Add(text); if (selected) chosen.Add(text); }
            }
            foreach (string item in chosen.Count > 0 ? chosen : fallback)
            { if (!result.Items.Contains(item)) result.Items.Add(item); if (result.Items.Count == 5) break; }
            if (result.Items.Count == 0) result.Items.Add(english ? "See the full release notes for details" : "请查看完整更新说明了解本次变化");
            if (result.Limitation.Length > 480) result.Limitation = result.Limitation.Substring(0, 477) + "…";
            return result;
        }
    }

    internal sealed class UpdatePrompt : Form
    {
        internal readonly FlowLayoutPanel Notes = new FlowLayoutPanel { Name = "UpdateSummary", Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(12), BackColor = Color.White };
        internal readonly Button Later, Now;
        internal readonly ReleaseSummary Summary;
        internal UpdatePrompt(AvailableUpdate update, string current)
        {
            bool en = UiText.English;
            Text = en ? "Update available" : "发现新版本"; Icon = DesktopIdentity.LoadIcon();
            Font = new Font("Microsoft YaHei UI", 10); AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(580, 520); MinimumSize = new Size(540, 430); StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; MaximizeBox = false; BackColor = UiStyle.Background; Notes.BackColor = SystemColors.Window;
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(20) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            TableLayoutPanel header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 2, Padding = new Padding(0, 0, 0, 12), Margin = Padding.Empty };
            header.RowStyles.Add(new RowStyle(SizeType.AutoSize)); header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            header.Controls.Add(new Label { Text = (en ? "Version " : "发现新版本 ") + update.Version, Font = new Font(Font.FontFamily, 14, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty }, 0, 0);
            string date = update.PublishedAt == DateTime.MinValue ? "" : "   ·   " + update.PublishedAt.ToLocalTime().ToString("yyyy-MM-dd");
            header.Controls.Add(new Label { Text = (en ? "Current " : "当前 ") + current + "  →  " + update.Version + date, Dock = DockStyle.Fill, ForeColor = Color.DimGray, Margin = Padding.Empty }, 0, 1);
            root.Controls.Add(header, 0, 0); root.Controls.Add(Notes, 0, 1);
            Summary = ReleaseSummary.Parse(update.Notes, en);
            AddText(en ? "What's new" : "本次更新", true, Color.Black);
            foreach (string item in Summary.Items) AddText("•  " + item, false, Color.Black);
            if (!String.IsNullOrEmpty(Summary.Limitation)) { AddText(en ? "Known issue" : "已知问题", true, Color.DimGray); AddText(Summary.Limitation, false, Color.DimGray); }
            Notes.ClientSizeChanged += delegate { ResizeNotes(); }; Notes.FontChanged += delegate { ResizeNotes(); };
            LinkLabel full = new LinkLabel { Text = en ? "Full release notes ↗" : "完整更新说明 ↗", AutoSize = true, Anchor = AnchorStyles.Left, Margin = Padding.Empty };
            full.LinkClicked += delegate
            {
                try { Process.Start(new ProcessStartInfo("https://github.com/" + Updates.Repository + "/releases/tag/" + Uri.EscapeDataString(update.Tag)) { UseShellExecute = true }); }
                catch (Exception error) { UiDialog.Show(this, error.Message, en ? "Unable to open release notes" : "无法打开更新说明"); }
            };
            root.Controls.Add(full, 0, 2);
            root.Controls.Add(new Label { Text = en ? "CAR will restart after the update" : "更新完成后将重启 CAR", Dock = DockStyle.Fill, ForeColor = Color.DimGray, Margin = Padding.Empty }, 0, 3);
            TableLayoutPanel actions = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty, Padding = new Padding(0, 8, 0, 0) };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 98)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
            LinkLabel skip = new LinkLabel { Name = "SkipUpdate", Text = en ? "Skip this version" : "跳过此版本", AutoSize = true, Anchor = AnchorStyles.Left, Margin = Padding.Empty, LinkColor = Color.DimGray };
            skip.LinkClicked += delegate { DialogResult = DialogResult.Ignore; Close(); };
            Later = new Button { Name = "UpdateLater", Text = en ? "Later" : "稍后", Dock = DockStyle.Fill, DialogResult = DialogResult.Cancel, Margin = new Padding(0, 3, 10, 3) };
            Now = new Button { Name = "UpdateNow", Text = en ? "Update now" : "立即更新", Dock = DockStyle.Fill, DialogResult = DialogResult.OK, Margin = new Padding(0, 3, 0, 3), BackColor = Color.FromArgb(30, 105, 210), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            actions.Controls.Add(skip, 0, 0); actions.Controls.Add(Later, 1, 0); actions.Controls.Add(Now, 2, 0); root.Controls.Add(actions, 0, 4);
            Controls.Add(root); AcceptButton = Later; CancelButton = Later;
            if (SystemInformation.HighContrast) { Now.BackColor = SystemColors.Highlight; Now.ForeColor = SystemColors.HighlightText; full.LinkColor = skip.LinkColor = SystemColors.HotTrack; }
            foreach (Control child in header.Controls) { child.AutoSize = true; child.Margin = new Padding(0, 4, 0, 4); }
            full.Margin = new Padding(0, 8, 0, 8);
            var restart = root.GetControlFromPosition(0, 3) as Label; restart.AutoSize = true; restart.Margin = new Padding(0, 0, 0, 8);
            Later.AutoSize = Now.AutoSize = true; Later.MinimumSize = Now.MinimumSize = new Size(88, 34);
            Action fit = delegate {
                int width = Math.Max(150, root.ClientSize.Width - root.Padding.Horizontal);
                foreach (Control child in header.Controls) child.MaximumSize = new Size(width, 0);
                restart.MaximumSize = new Size(width, 0);
                actions.ColumnStyles[1].Width = Math.Max(98, Later.PreferredSize.Width + 10);
                actions.ColumnStyles[2].Width = Math.Max(126, Now.PreferredSize.Width);
            };
            root.SizeChanged += delegate { fit(); }; FontChanged += delegate { header.GetControlFromPosition(0, 0).Font = new Font(Font.FontFamily, Font.SizeInPoints * 1.4f, FontStyle.Bold); fit(); }; fit();
            Shown += delegate { ActiveControl = Later; Later.Focus(); ResizeNotes(); };
        }
        void AddText(string text, bool bold, Color color)
        {
            Label label = bold ? new SectionLabel() : new Label();
            label.Text = text; label.AutoSize = true; label.UseMnemonic = false;
            label.ForeColor = SystemInformation.HighContrast ? SystemColors.WindowText : color;
            label.Margin = new Padding(0, bold ? 8 : 4, 0, 6); Notes.Controls.Add(label);
        }
        void ResizeNotes()
        {
            int width = Math.Max(100, Notes.ClientSize.Width - Notes.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 4);
            foreach (Control child in Notes.Controls) child.MaximumSize = new Size(width, 0);
        }
    }
}
