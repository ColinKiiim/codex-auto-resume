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
        readonly System.Windows.Forms.Timer clickTimer = new System.Windows.Forms.Timer { Interval = 20 };
        bool keyboardFocus;
        int clickedAt;
        internal UsageSummary()
        {
            Name = "UsageSummary"; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            Text = ""; AccessibleName = "额度详情"; Cursor = Cursors.Hand; Margin = Padding.Empty;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            clickTimer.Tick += delegate { if (unchecked(Environment.TickCount - clickedAt) >= 180) clickTimer.Stop(); Invalidate(); };
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void Select(bool directed, bool forward)
        { if (directed) keyboardFocus = true; base.Select(directed, forward); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); keyboardFocus = false; Invalidate(); }
        protected override void OnKeyDown(KeyEventArgs e) { keyboardFocus = true; base.OnKeyDown(e); Invalidate(); }
        protected override void OnClick(EventArgs e)
        { clickedAt = Environment.TickCount; clickTimer.Start(); Invalidate(); base.OnClick(e); }
        protected override void Dispose(bool disposing) { if (disposing) clickTimer.Dispose(); base.Dispose(disposing); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            if (clickTimer.Enabled)
            {
                int age = Math.Max(0, unchecked(Environment.TickCount - clickedAt));
                int alpha = Math.Max(0, 32 * (180 - age) / 180);
                using (var pulse = new SolidBrush(Color.FromArgb(alpha, Color.DodgerBlue))) e.Graphics.FillRectangle(pulse, ClientRectangle);
            }
            float scale = Math.Max(1, Font.Height / 14f);
            var windows = Snapshot == null ? new UsageWindow[0] : Snapshot.Windows.Take(2).ToArray();
            if (windows.Length == 0)
            {
                TextRenderer.DrawText(e.Graphics, Message, Font, ClientRectangle, ForeColor, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                if (Focused && keyboardFocus) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -1, -1));
                return;
            }
            string prefix = Stale ? (UiText.English ? "Old" : "旧数据") : (UiText.English ? "Left" : "剩余");
            int spacing = (int)Math.Round(8 * scale);
            int[] labels = windows.Select(window => TextRenderer.MeasureText(window.Label, Font).Width).ToArray();
            int percentWidth = TextRenderer.MeasureText("100%", Font).Width;
            int prefixWidth = TextRenderer.MeasureText(prefix, Font).Width;
            int barWidth = Math.Min((int)(42 * scale), Math.Max((int)(12 * scale), (Width - prefixWidth - labels.Sum() - windows.Length * (percentWidth + spacing * 3)) / windows.Length));
            int total = prefixWidth + windows.Length * (barWidth + percentWidth + spacing * 3) + labels.Sum();
            if (total > Width)
            {
                string compact = prefix + " " + String.Join(" · ", windows.Select(w => w.Label + " " + w.Percent));
                TextRenderer.DrawText(e.Graphics, compact, Font, ClientRectangle, ForeColor, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                return;
            }
            int x = Math.Max(0, Width - total), middle = Height / 2;
            Draw(e.Graphics, prefix, ref x, prefixWidth); x += spacing;
            for (int i = 0; i < windows.Length; i++)
            {
                var window = windows[i]; Draw(e.Graphics, window.Label, ref x, labels[i]); x += spacing / 2;
                int thickness = Math.Max(3, Font.Height / 4);
                using (var track = new SolidBrush(Color.FromArgb(220, 226, 232))) e.Graphics.FillRectangle(track, x, middle - thickness / 2, barWidth, thickness);
                if (window.Remaining.HasValue)
                    using (var fill = new SolidBrush(Stale ? Color.Gray : window.Remaining <= 10 ? Color.DarkGoldenrod : Color.SeaGreen))
                        e.Graphics.FillRectangle(fill, x, middle - thickness / 2, (float)(barWidth * window.Remaining.Value / 100), thickness);
                x += barWidth + spacing / 2; Draw(e.Graphics, window.Percent, ref x, percentWidth); x += spacing;
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
        Label usageDetails;
        Button usageRefresh;

        void InitializeUsageFooter(TableLayoutPanel layout)
        {
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            recoveryCount.AutoSize = true; recoveryCount.Anchor = AnchorStyles.Left; recoveryCount.Dock = DockStyle.None; recoveryCount.Margin = new Padding(0, 0, 12, 0);
            usageSummary.Dock = DockStyle.Fill; usageSummary.BackColor = BackColor; usageSummary.ForeColor = Color.DimGray; usageSummary.Font = new Font(Font.FontFamily, 8);
            usageSummary.Click += delegate { ShowUsageDetails(); };
            footer.Controls.Add(recoveryCount, 0, 0); footer.Controls.Add(usageSummary, 1, 0); layout.Controls.Add(footer, 0, 5);
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
            usageSummary.AccessibleName = UiText.English ? "Usage details" : "额度详情";
            usageSummary.AccessibleDescription = usageSnapshot == null ? usageSummary.Message : (usageSummary.Stale ? UsageMessage() + "; " : "") + String.Join("; ", usageSnapshot.Windows.Select(w => w.Label + " " + w.Percent));
            tips.SetToolTip(usageSummary, UsageHoverText());
            usageSummary.Invalidate();
            if (usageDetails != null && !usageDetails.IsDisposed)
            {
                usageDetails.Text = UsageDetailText(); usageRefresh.Enabled = !usageBusy && target != null && !target.Demo;
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
            string text = (UiText.English ? "Account: " : "账号：") + usageSnapshot.Account + Environment.NewLine;
            text += (UiText.English ? "Source: " : "来源：") + usageSnapshot.Home + Environment.NewLine;
            foreach (var window in usageSnapshot.Windows)
                text += Environment.NewLine + window.Label + "  " + (UiText.English ? "remaining " : "剩余 ") + window.Percent + "  " + (UiText.English ? "reset " : "重置 ") + (window.ResetUtc.HasValue ? window.ResetUtc.Value.ToLocalTime().ToString("MM-dd HH:mm:ss") : "—");
            return text + Environment.NewLine + Environment.NewLine + (UiText.English ? "Last successful read: " : "最近成功读取：") + usageSnapshot.FetchedUtc.ToLocalTime().ToString("MM-dd HH:mm:ss") + (usageError == UsageError.None ? "" : Environment.NewLine + UsageMessage() + (UiText.English ? " (old data)" : "（旧数据）"));
        }
        void ShowUsageDetails()
        {
            if (usageDialog != null && !usageDialog.IsDisposed) { usageDialog.Activate(); return; }
            usageDialog = new Form { Text = UiText.English ? "Usage details" : "额度详情", Font = Font, AutoScaleDimensions = new SizeF(96, 96), AutoScaleMode = AutoScaleMode.Dpi, ClientSize = new Size(470, 280), MinimumSize = new Size(430, 300), StartPosition = FormStartPosition.CenterParent, Icon = Icon };
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 2 };
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            usageDetails = new Label { AutoSize = true, MaximumSize = new Size(425, 0), Text = UsageDetailText() }; scroll.Controls.Add(usageDetails);
            scroll.SizeChanged += delegate { if (usageDetails != null && !usageDetails.IsDisposed) usageDetails.MaximumSize = new Size(Math.Max(100, scroll.ClientSize.Width - 16), 0); };
            usageRefresh = UiButton(UiText.English ? "Refresh" : "刷新", async delegate { await RefreshUsageAsync(); }); usageRefresh.Anchor = AnchorStyles.Right;
            panel.Controls.Add(scroll, 0, 0); panel.Controls.Add(usageRefresh, 0, 1); usageDialog.Controls.Add(panel);
            usageDialog.FormClosed += delegate { usageDetails = null; usageRefresh = null; }; RenderUsage(); WindowUi.Show(usageDialog, this);
        }
    }
}
