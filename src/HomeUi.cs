using System;
using System.Drawing;
using System.Windows.Forms;

namespace CodexAutoResume
{
    internal sealed partial class MainForm
    {
        TableLayoutPanel homeLayout, homeOptions, homeHistory;
        Label idleHelp, homeTitle;
        LinkLabel homeRecords;
        bool fittingHome;
        ComboBox recordsFilter;
        bool changingRecordFilter;

        void InitializeCompactUi()
        {
            Text = "Codex Auto Resume"; AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(560, 500); MinimumSize = new Size(536, 530); BackColor = UiStyle.Background; ForeColor = SystemColors.WindowText;
            homeLayout = new TableLayoutPanel { Name = "HomeLayout", Dock = DockStyle.Fill, Padding = new Padding(20, 16, 20, 16), Margin = Padding.Empty, ColumnCount = 1, RowCount = 6 };
            homeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 5; i++) homeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            homeLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0, 0, 0, 8) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            homeTitle = new Label { Text = "监测", AutoSize = true, Font = new Font(Font.FontFamily, 14, FontStyle.Bold), Anchor = AnchorStyles.Left, Margin = Padding.Empty }; header.Controls.Add(homeTitle, 0, 0);
            Button settings = UiButton("设置", delegate { ShowSettings(); }); settings.Name = "OpenSettings"; settings.Margin = Padding.Empty; header.Controls.Add(settings, 1, 0); homeLayout.Controls.Add(header, 0, 0);

            stateLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Margin = new Padding(0, 0, 0, 8) };
            stateLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56)); stateLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); stateLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            stateLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); stateLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            preview.Size = new Size(44, 44); preview.Margin = new Padding(0, 4, 12, 0); preview.BackColor = BackColor; preview.Cursor = Cursors.Hand; preview.TabStop = false;
            preview.Click += delegate { ShowPreviewDetail(); }; stateLayout.Controls.Add(preview, 0, 0); stateLayout.SetRowSpan(preview, 2);
            health.Dock = DockStyle.Fill; health.Margin = Padding.Empty; health.TextAlign = ContentAlignment.MiddleLeft; stateLayout.Controls.Add(health, 1, 0);
            toggle.AutoSize = true; toggle.MinimumSize = new Size(96, 32); toggle.Margin = Padding.Empty; toggle.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            toggle.Click += delegate { Toggle(); }; stateLayout.Controls.Add(toggle, 2, 0);
            status.Font = new Font(Font.FontFamily, 13, FontStyle.Bold); status.AutoEllipsis = true; status.Margin = Padding.Empty; status.TextAlign = ContentAlignment.MiddleLeft;
            stateLayout.Controls.Add(status, 1, 1);
            regionAction.AutoSize = true; regionAction.Anchor = AnchorStyles.Right; regionAction.LinkClicked += delegate { RequestRegion(); }; stateLayout.Controls.Add(regionAction, 2, 1);
            // Source and capture time remain available in the focused preview details.
            previewCaption.Visible = false; previewTime.Visible = false;
            homeLayout.Controls.Add(stateLayout, 0, 1);

            var connection = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0, 0, 0, 10) };
            connection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); connection.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            targetName.Margin = Padding.Empty; targetName.Cursor = Cursors.Hand; targetName.TabStop = true;
            targetName.AccessibleName = "目标详情"; targetName.Click += delegate { ShowTargetDetails(); };
            targetName.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { ShowTargetDetails(); e.Handled = true; } };
            modeName.AutoSize = true; modeName.Anchor = AnchorStyles.Right; modeName.Margin = new Padding(12, 0, 0, 0); modeName.ForeColor = UiStyle.Secondary;
            connection.Controls.Add(targetName, 0, 0); connection.Controls.Add(modeName, 1, 0); homeLayout.Controls.Add(connection, 0, 2);

            homeOptions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0, 0, 0, 12) };
            homeOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++) homeOptions.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            automatic.Text = "自动恢复"; automatic.AutoSize = false; automatic.Dock = DockStyle.Fill; automatic.Margin = Padding.Empty; homeOptions.Controls.Add(automatic, 0, 0);
            idleHelp = UiStyle.Help("检测到暂停后，空闲 1.5 秒自动继续"); idleHelp.Margin = Padding.Empty; idleHelp.Dock = DockStyle.Fill;
            homeOptions.Controls.Add(idleHelp, 0, 1); homeOptions.Controls.Add(scheduleSummary, 0, 2); homeLayout.Controls.Add(homeOptions, 0, 3);
            InitializeUsageFooter(homeLayout);

            homeHistory = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Margin = Padding.Empty };
            homeHistory.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); homeHistory.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); homeHistory.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            homeHistory.RowStyles.Add(new RowStyle(SizeType.AutoSize)); homeHistory.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Label historyTitle = UiLabel("最近进度"); historyTitle.ForeColor = UiStyle.Secondary; historyTitle.Margin = new Padding(0, 0, 0, 6); homeHistory.Controls.Add(historyTitle, 0, 0);
            recoveryCount.AutoSize = true; recoveryCount.ForeColor = UiStyle.Secondary; recoveryCount.Margin = new Padding(8, 0, 12, 6); recoveryCount.Cursor = Cursors.Hand;
            recoveryCount.Click += delegate { ShowRecords(true); }; recoveryCount.MouseEnter += delegate { tips.SetToolTip(recoveryCount, RecoveryPreview()); }; homeHistory.Controls.Add(recoveryCount, 1, 0);
            homeRecords = new LinkLabel { Name = "ActivityLink", Text = "完整记录", AutoSize = true, Margin = new Padding(0, 0, 0, 6), LinkColor = UiStyle.Accent };
            homeRecords.LinkClicked += delegate { ShowActivity(); }; homeHistory.Controls.Add(homeRecords, 2, 0);
            recentLog.BackColor = BackColor; recentLog.ForeColor = ForeColor; recentLog.WordWrap = false;
            homeHistory.Controls.Add(recentLog, 0, 1); homeHistory.SetColumnSpan(recentLog, 3); homeLayout.Controls.Add(homeHistory, 0, 5);
            Controls.Add(homeLayout); InitializeRecordsPage();
            homeLayout.SizeChanged += delegate { FitHome(); }; FontChanged += delegate {
                Font titleFont = homeTitle.Font, statusFont = status.Font;
                homeTitle.Font = new Font(Font.FontFamily, Font.SizeInPoints * 1.4f, FontStyle.Bold);
                status.Font = new Font(Font.FontFamily, Font.SizeInPoints * 1.3f, FontStyle.Bold);
                titleFont.Dispose(); statusFont.Dispose(); FitHome();
            };
            Shown += delegate { WindowUi.FitToWorkArea(this); FitHome(); };
            idleHelp.TextChanged += delegate { FitHome(); }; scheduleSummary.TextChanged += delegate { FitHome(); };
            FitHome();
            stabilityTimer.Tick += delegate { if (monitoring && automatic.Checked && (lastObservedState == ButtonState.Resume || QuotaSquareWake && lastObservedState == ButtonState.Running)) TickMonitor(); else stabilityTimer.Stop(); };
            questionTimer.Tick += delegate { if (monitoring && questions.Waiting) TickMonitor(); else questionTimer.Stop(); };
            frameTimer.Tick += delegate { DispatchFrame(); };
        }

        void FitHome()
        {
            if (homeLayout == null || fittingHome || IsDisposed) return;
            fittingHome = true;
            homeLayout.SuspendLayout();
            try
            {
                int gap = UiStyle.Px(this, 8), width = Math.Max(100, homeLayout.ClientSize.Width - homeLayout.Padding.Horizontal);
                int line = Font.Height + UiStyle.Px(this, 4);
                int help = TextRenderer.MeasureText(idleHelp.Text, idleHelp.Font, new Size(width, Int32.MaxValue), TextFormatFlags.WordBreak).Height;
                int check = Math.Max(automatic.PreferredSize.Height, line);
                int header = Math.Max(UiStyle.Px(this, 32), toggle.PreferredSize.Height) + gap;
                int state = Math.Max(health.Font.Height + 4, toggle.PreferredSize.Height) + status.Font.Height + gap * 2;
                int context = line + gap, quota = Math.Max(UiStyle.Px(this, 76), line * 2 + gap * 4);
                int schedule = resumeSchedule.Active ? line : 0;
                bool compact = header + state + context + check + help + schedule + gap * 2 + quota + homeLayout.Padding.Vertical + line * 3 > homeLayout.ClientSize.Height;
                idleHelp.Visible = !compact; scheduleSummary.Visible = resumeSchedule.Active;
                homeOptions.RowStyles[0].Height = check; homeOptions.RowStyles[1].Height = compact ? 0 : help; homeOptions.RowStyles[2].Height = schedule;
                homeLayout.RowStyles[0].Height = header; homeLayout.RowStyles[1].Height = state;
                homeLayout.RowStyles[2].Height = context; homeLayout.RowStyles[3].Height = check + (compact ? 0 : help) + schedule + gap * 2;
                homeLayout.RowStyles[4].Height = quota;
                tips.SetToolTip(automatic, UiText.Get("检测到暂停后，空闲 1.5 秒自动继续"));
                recoveryCount.Visible = TextRenderer.MeasureText(recoveryCount.Text + homeRecords.Text + UiText.Get("最近进度"), Font).Width + gap * 4 < width;
            }
            finally { homeLayout.ResumeLayout(true); fittingHome = false; }
        }

        void SetPreviewKind(bool question)
        {
            questionPreview = question;
            UiText.Set(previewCaption, question ? "问答区域" : controlPreview ? "后台控件状态" : "采样图");
            preview.Visible = preview.Image != null;
            stateLayout.ColumnStyles[0].Width = preview.Visible ? UiStyle.Px(this, 56) : 0;
            preview.Size = new Size(UiStyle.Px(this, 44), UiStyle.Px(this, 44));
            tips.SetToolTip(preview, UiText.Get(controlPreview ? "后台控件状态示意 · 点击查看" : "最近识别的局部画面；点击放大查看"));
        }

        void ShowTargetDetails()
        {
            string text = targetName.Text + Environment.NewLine + modeName.Text + Environment.NewLine + UiText.Get(previewCaption.Text) + "  " + previewTime.Text + Environment.NewLine + UiText.Get(statusSource);
            WindowUi.TextDetails(this, UiText.Get("目标详情"), text);
        }
        void RefreshRecordLanguage()
        {
            changingRecordFilter = true;
            recordsFilter.Items.Clear(); recordsFilter.Items.Add(UiText.Get("恢复记录")); recordsFilter.Items.Add(UiText.Get("全部活动")); recordsFilter.SelectedIndex = showingRecoveryRecords ? 0 : 1;
            changingRecordFilter = false;
        }
    }
}
