using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CodexAutoResume
{
    public sealed class RegionSelection
    {
        bool explicitChoice;
        bool requestedChoice;
        Rectangle selectedBadge;
        Size selectedSearchSize;
        // -1 requires explicit user choice; -2 means no candidates.
        public int Resolve(List<LocatedButton> buttons, Size searchSize)
        {
            if (buttons.Count == 0) return -2;
            if (requestedChoice) return -1;
            if (!explicitChoice) return buttons.Count == 1 ? 0 : -1;
            if (searchSize != selectedSearchSize) return -1;
            int match = -1;
            for (int i = 0; i < buttons.Count; i++)
            {
                Rectangle b = buttons[i].Badge;
                if (Math.Abs(b.X - selectedBadge.X) <= 6 && Math.Abs(b.Y - selectedBadge.Y) <= 6 &&
                    Math.Abs(b.Width - selectedBadge.Width) <= 6 && Math.Abs(b.Height - selectedBadge.Height) <= 6)
                { if (match != -1) return -1; match = i; }
            }
            return match;
        }
        public void Confirm(int index, List<LocatedButton> buttons, Size searchSize)
        {
            if (index < 0 || index >= buttons.Count) throw new ArgumentOutOfRangeException("index");
            requestedChoice = false; explicitChoice = true; selectedBadge = buttons[index].Badge; selectedSearchSize = searchSize;
        }
        public void RequestChoice() { requestedChoice = true; }
        public void CancelRequest() { requestedChoice = false; }
        internal RegionMemory Remember()
        { return new RegionMemory { Explicit = explicitChoice, Badge = selectedBadge, SearchSize = selectedSearchSize }; }
        internal void Restore(RegionMemory memory)
        { if (memory == null) return; explicitChoice = memory.Explicit; selectedBadge = memory.Badge; selectedSearchSize = memory.SearchSize; requestedChoice = false; }
    }

    public sealed class RegionMemory
    {
        public bool Explicit { get; set; }
        public Rectangle Badge { get; set; }
        public Size SearchSize { get; set; }
    }

    internal sealed class RegionPicker : Form
    {
        public int SelectedIndex = -1;
        readonly List<Image> thumbnails = new List<Image>();
        public RegionPicker(Bitmap scan, List<LocatedButton> candidates)
        {
            Shown += delegate { UiText.Apply(this); };
            Text = "请选择要监控的 Codex 按钮";
            Font = new Font("Microsoft YaHei UI", 10);
            ClientSize = new Size(860, 650); StartPosition = FormStartPosition.CenterScreen;
            Label instructions = new Label { Text = "发现多个候选，或原选区域已改变。请选择并确认；确认前不会自动点击。", Dock = DockStyle.Top, Height = 50, Padding = new Padding(12) };
            ListView list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false };
            list.Columns.Add(UiText.Get("编号 / 图标"), 160); list.Columns.Add(UiText.Get("识别状态"), 250); list.Columns.Add(UiText.Get("扫描区域中的位置"), 390);
            ImageList images = new ImageList { ImageSize = new Size(64, 64), ColorDepth = ColorDepth.Depth32Bit };
            for (int i = 0; i < candidates.Count; i++)
            {
                LocatedButton c = candidates[i];
                Bitmap thumb = scan.Clone(c.Crop, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                thumbnails.Add(thumb); images.Images.Add(thumb);
                ListViewItem item = new ListViewItem((UiText.English ? "Area " : "区域 ") + (i + 1), i);
                item.SubItems.Add(UiText.Get(Portable.StateName(c.Detection.State)));
                item.SubItems.Add("X=" + c.Badge.X + ", Y=" + c.Badge.Y + ", " + c.Badge.Width + "×" + c.Badge.Height);
                list.Items.Add(item);
            }
            list.SmallImageList = images;
            Bitmap overview = new Bitmap(scan);
            using (Graphics graphics = Graphics.FromImage(overview))
            using (Pen pen = new Pen(Color.Yellow, 3))
            using (Font numberFont = new Font("Arial", 16, FontStyle.Bold))
                for (int i = 0; i < candidates.Count; i++)
                {
                    Rectangle rect = candidates[i].Crop; graphics.DrawRectangle(pen, rect);
                    graphics.FillRectangle(Brushes.Black, rect.Left, Math.Max(0, rect.Top - 26), 40, 26);
                    graphics.DrawString((i + 1).ToString(), numberFont, Brushes.Yellow, rect.Left, Math.Max(0, rect.Top - 26));
                }
            PictureBox context = new PictureBox { Dock = DockStyle.Top, Height = 230, SizeMode = PictureBoxSizeMode.Zoom, Image = overview, BackColor = Color.FromArgb(24, 24, 24) };
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
            Button confirm = new Button { Text = "使用所选区域", Enabled = false, AutoSize = true };
            Button cancel = new Button { Text = "暂不选择", AutoSize = true, DialogResult = DialogResult.Cancel };
            list.SelectedIndexChanged += delegate { confirm.Enabled = list.SelectedIndices.Count == 1; };
            confirm.Click += delegate { if (list.SelectedIndices.Count != 1) return; SelectedIndex = list.SelectedIndices[0]; DialogResult = DialogResult.OK; Close(); };
            actions.Controls.Add(confirm); actions.Controls.Add(cancel); CancelButton = cancel;
            Controls.Add(list); Controls.Add(actions); Controls.Add(context); Controls.Add(instructions);
            FormClosed += delegate { context.Image = null; overview.Dispose(); list.SmallImageList = null; images.Dispose(); foreach (Image image in thumbnails) image.Dispose(); };
        }
    }
}
