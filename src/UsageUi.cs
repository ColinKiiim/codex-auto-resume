using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexAutoResume
{
    internal sealed class UsageSummary : Button
    {
        internal UsageSnapshot Snapshot;
        internal string Message = "额度未连接";
        internal bool Stale;
        bool keyboardFocus;
        internal UsageSummary()
        {
            Name = "UsageSummary"; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            Text = ""; AccessibleName = "额度详情"; Cursor = Cursors.Hand; Margin = Padding.Empty;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void Select(bool directed, bool forward)
        { if (directed) keyboardFocus = true; base.Select(directed, forward); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); keyboardFocus = false; Invalidate(); }
        protected override void OnKeyDown(KeyEventArgs e) { keyboardFocus = true; base.OnKeyDown(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            var windows = Snapshot == null ? new UsageWindow[0] : Snapshot.Windows.Take(2).ToArray();
            if (windows.Length == 0)
            {
                TextRenderer.DrawText(e.Graphics, Message, Font, ClientRectangle, ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
            else
            {
                int gap = UiStyle.Px(this, 20), column = (Width - gap * (windows.Length - 1)) / windows.Length;
                for (int i = 0; i < windows.Length; i++)
                {
                    var window = windows[i]; string label = window.Minutes == 300 ? (UiText.English ? "5 hours" : "5 小时") : window.Minutes == 10080 ? (UiText.English ? "Weekly" : "本周") : window.Label;
                    int x = i * (column + gap), textHeight = Math.Min(Height, Font.Height + 4);
                    TextRenderer.DrawText(e.Graphics, label, Font, new Rectangle(x, 0, column, textHeight), ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(e.Graphics, window.Percent, Font, new Rectangle(x, 0, column, textHeight), ForeColor, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    int y = textHeight + 4, thickness = UiStyle.Px(this, 4);
                    if (y + thickness <= Height)
                    {
                        using (var track = new SolidBrush(SystemInformation.HighContrast ? SystemColors.ControlDark : Color.FromArgb(218, 225, 232))) e.Graphics.FillRectangle(track, x, y, column, thickness);
                        if (window.Remaining.HasValue)
                            using (var fill = new SolidBrush(SystemInformation.HighContrast ? SystemColors.Highlight : Stale ? Color.Gray : window.Remaining <= 10 ? Color.DarkGoldenrod : Color.SeaGreen))
                                e.Graphics.FillRectangle(fill, x, y, (float)(column * Math.Max(0, Math.Min(100, window.Remaining.Value)) / 100), thickness);
                    }
                }
            }
            if (Focused && keyboardFocus) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -1, -1));
        }
        void Draw(Graphics graphics, string text, ref int x, int width)
        {
            TextRenderer.DrawText(graphics, text, Font, new Rectangle(x, 0, width, Height), ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            x += width;
        }
    }

    internal sealed partial class MainForm
    {
        readonly UsageSummary usageSummary = new UsageSummary();
        UsageSnapshot usageSnapshot;
        UsageError usageError = UsageError.NoTarget;
        CancellationTokenSource usageCancellation;
        DateTime nextUsageRead = DateTime.MinValue;
        string usageTargetKey = "";
        int usageRevision, usageFailures;
        bool usageBusy;
        Form usageDialog;
        ProgressHistory usageDetails;
        Label usageTitle;
        Button usageRefresh;

        void InitializeUsageFooter(TableLayoutPanel layout)
        {
            var footer = new TableLayoutPanel { Name = "UsageSection", Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 0, 0, 12) };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.RowStyles.Add(new RowStyle(SizeType.AutoSize)); footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            usageTitle = UiLabel("额度剩余"); usageTitle.Margin = new Padding(0, 0, 0, 4); footer.Controls.Add(usageTitle, 0, 0);
            var detail = new LinkLabel { Text = "查看详情", AutoSize = true, Margin = Padding.Empty, LinkColor = UiStyle.Accent };
            detail.LinkClicked += delegate { ShowUsageDetails(); }; footer.Controls.Add(detail, 1, 0);
            usageSummary.Dock = DockStyle.Fill; usageSummary.BackColor = BackColor; usageSummary.ForeColor = ForeColor;
            usageSummary.Click += delegate { ShowUsageDetails(); };
            footer.Controls.Add(usageSummary, 0, 1); footer.SetColumnSpan(usageSummary, 2); layout.Controls.Add(footer, 0, 4);
            RenderUsage();
        }
        void UsagePulse()
        {
            string key;
            try { key = UsageSource.TargetKey(target); } catch { key = ""; }
            if (key != usageTargetKey)
            {
                usageTargetKey = key; usageRevision++; usageSnapshot = null; usageFailures = 0;
                if (usageCancellation != null) usageCancellation.Cancel();
                nextUsageRead = DateTime.UtcNow.AddSeconds(2); usageError = UsageError.NoTarget; RenderUsage();
            }
            if (key.Length > 0 && !usageBusy && DateTime.UtcNow >= nextUsageRead) { var pending = RefreshUsageAsync(); }
        }
        async Task RefreshUsageAsync()
        {
            if (usageBusy || target == null || target.Demo || IsDisposed) return;
            string key = UsageSource.TargetKey(target), path = target.ProcessPath;
            int revision = usageRevision;
            DateTime started = DateTime.UtcNow;
            usageBusy = true; usageCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); RenderUsage();
            var cancellation = usageCancellation;
            try
            {
                var result = await Task.Run(async delegate { return await CodexUsage.ReadAsync(UsageSource.Find(path, key), cancellation.Token); });
                if (IsDisposed || cancellation.IsCancellationRequested || revision != usageRevision || key != UsageSource.TargetKey(target)) return;
                usageError = result.Error;
                if (result.Snapshot != null) usageSnapshot = result.Snapshot;
                else
                {
                    if (usageSnapshot == null || result.AccountKey != usageSnapshot.AccountKey || result.Error == UsageError.Changed) usageSnapshot = null;
                }
                ScheduleNextUsageRead(result.Error == UsageError.None, started);
            }
            catch (OperationCanceledException) { }
            catch { if (!IsDisposed) { usageError = UsageError.Unavailable; usageSnapshot = null; ScheduleNextUsageRead(false, started); } }
            finally
            {
                usageBusy = false; usageCancellation = null; cancellation.Dispose();
                if (!IsDisposed && !lifetime.IsCancellationRequested) { RenderUsage(); QuotaUsageChanged(); }
            }
        }
        void ScheduleNextUsageRead(bool success, DateTime started)
        {
            if (success) { usageFailures = 0; nextUsageRead = started.AddSeconds(10); }
            else { usageFailures++; nextUsageRead = DateTime.UtcNow.AddMinutes(Math.Min(15, Math.Pow(2, Math.Min(usageFailures, 4)))); }
        }
        string UsageMessage()
        {
            switch (usageError)
            {
                case UsageError.NoTarget: return UiText.English ? "Usage not connected" : "额度未连接";
                case UsageError.NoExecutable: return UiText.English ? "Usage unavailable" : "额度不可用";
                case UsageError.NotSignedIn: return UiText.English ? "Sign in to Codex" : "请先登录 Codex";
                case UsageError.UnsupportedAuth: return UiText.English ? "ChatGPT login needed" : "需要 ChatGPT 登录";
                case UsageError.Timeout: return UiText.English ? "Usage timed out" : "额度查询超时";
                case UsageError.Changed: return UiText.English ? "Login source changed" : "登录来源已变化";
                case UsageError.None: return UiText.English ? "No quota windows" : "未提供额度窗口";
                default: return UiText.English ? "Usage update failed" : "额度更新失败";
            }
        }
        void RenderUsage()
        {
            usageSummary.Snapshot = usageSnapshot; usageSummary.Stale = usageSnapshot != null && usageError != UsageError.None;
            usageSummary.Message = usageBusy && usageSnapshot == null ? (UiText.English ? "Reading usage…" : "正在读取额度…") : UsageMessage();
            usageSummary.AccessibleDescription = usageSnapshot == null ? usageSummary.Message : (usageSummary.Stale ? UsageMessage() + "; " : "") + String.Join("; ", usageSnapshot.Windows.Select(w => w.Label + " " + w.Percent));
            usageSummary.AccessibleName = (UiText.English ? "Usage details: " : "额度详情：") + usageSummary.AccessibleDescription;
            if (usageTitle != null) UiText.Set(usageTitle, usageSummary.Stale ? "额度剩余 · 旧数据" : "额度剩余");
            if (settingsUsage != null && !settingsUsage.IsDisposed) settingsUsage.Text = usageSummary.AccessibleDescription;
            tips.SetToolTip(usageSummary, UsageHoverText());
            usageSummary.Invalidate();
            if (usageDetails != null && !usageDetails.IsDisposed)
            {
                string detailText = UsageDetailText();
                if (usageDetails.Text != detailText) usageDetails.UpdateHistory(detailText, true);
                usageRefresh.Enabled = !usageBusy && target != null && !target.Demo;
                usageRefresh.Text = UiText.English ? "Refresh" : "刷新";
                usageDialog.Text = UiText.English ? "Usage details" : "额度详情";
            }
        }
        string UsageHoverText()
        {
            if (usageSnapshot == null || usageSnapshot.Windows.Length == 0) return usageSummary.Message;
            string text = usageSummary.Stale ? (UiText.English ? "Old data · " : "旧数据 · ") + UsageMessage() + Environment.NewLine : "";
            text += String.Join(Environment.NewLine, usageSnapshot.Windows.Select(window => window.Label + (UiText.English ? " resets: " : " 重置：") +
                (window.ResetUtc.HasValue ? window.ResetUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : (UiText.English ? "Unknown" : "未知"))));
            if (usageSummary.Stale) text += Environment.NewLine + (UiText.English ? "Last successful read: " : "最近成功读取：") + usageSnapshot.FetchedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            return text;
        }
        string UsageDetailText()
        {
            if (usageSnapshot == null) return UsageMessage() + Environment.NewLine + Environment.NewLine + (UiText.English ? "Reads the current local Codex login. No accounts are imported. If unavailable, check Codex login and installation." : "读取本机 Codex 当前登录，不导入账号\n不可用时请检查 Codex 登录状态与安装");
            string text = UiText.Get(usageSummary.Stale ? "额度剩余 · 旧数据" : "额度剩余") + Environment.NewLine;
            foreach (var window in usageSnapshot.Windows)
                text += Environment.NewLine + window.Label + "  " + (UiText.English ? "remaining " : "剩余 ") + window.Percent + "  " + (UiText.English ? "reset " : "重置 ") + (window.ResetUtc.HasValue ? window.ResetUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "—");
            return text + Environment.NewLine + Environment.NewLine + (UiText.English ? "Last successful read: " : "最近成功读取：") + usageSnapshot.FetchedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + (usageError == UsageError.None ? "" : Environment.NewLine + UsageMessage() + (UiText.English ? " (old data)" : "（旧数据）")) + Environment.NewLine + Environment.NewLine +
                (UiText.English ? "Account: " : "账号：") + usageSnapshot.Account + Environment.NewLine + (UiText.English ? "Source: " : "来源：") + usageSnapshot.Home;
        }
        void ShowUsageDetails()
        {
            if (usageDialog != null && !usageDialog.IsDisposed) { usageDialog.Activate(); return; }
            usageDialog = new Form { Text = UiText.English ? "Usage details" : "额度详情", Font = Font, AutoScaleDimensions = new SizeF(96, 96), AutoScaleMode = AutoScaleMode.Dpi, ClientSize = new Size(470, 280), MinimumSize = new Size(430, 300), StartPosition = FormStartPosition.CenterParent, Icon = Icon };
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 2 };
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            usageDetails = new ProgressHistory { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, Text = UsageDetailText(), BackColor = SystemColors.Window };
            usageDetails.RestoreView(0, 0, 0);
            usageRefresh = UiButton(UiText.English ? "Refresh" : "刷新", async delegate { await RefreshUsageAsync(); }); usageRefresh.Anchor = AnchorStyles.Right;
            panel.RowStyles[1].SizeType = SizeType.AutoSize;
            panel.Controls.Add(usageDetails, 0, 0); panel.Controls.Add(usageRefresh, 0, 1); usageDialog.Controls.Add(panel);
            usageDialog.FormClosed += delegate { usageDetails = null; usageRefresh = null; }; RenderUsage(); WindowUi.Show(usageDialog, this);
        }
    }
}
