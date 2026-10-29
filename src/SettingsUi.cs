using System;
using System.Drawing;
using System.Windows.Forms;

namespace CodexAutoResume
{
    internal sealed partial class MainForm
    {
        readonly string[] settingsNames = { "常规", "监测与恢复", "额度", "定时与任务", "诊断", "关于与更新" };
        Panel[] settingsPages;
        TableLayoutPanel settingsBody;
        ComboBox settingsCategories;
        ListBox settingsSidebar;
        Label settingsHealth, updateFeedback, settingsUsage;
        Button updateCheckButton;
        System.Windows.Forms.Timer settingsRefresh;
        int settingsCategory;
        bool changingSettingsCategory;
        TextBox quotaPromptEditor;
        Label quotaPromptFeedback;

        void ShowSettings()
        {
            if (settingsWindow == null || settingsWindow.IsDisposed) BuildSettings();
            settingsWindow.Visible = true; settingsWindow.BringToFront(); RefreshSettingsLanguage(); RefreshUi();
        }

        void BuildSettings()
        {
            settingsWindow = new Panel { Name = "SettingsPage", Dock = DockStyle.Fill, BackColor = UiStyle.Background };
            var header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Padding = new Padding(16, 12, 16, 8) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Button back = UiButton("返回监测", delegate { if (ResolveQuotaEdits()) { settingsWindow.Visible = false; homeLayout.BringToFront(); } }); back.Name = "BackFromSettings";
            header.Controls.Add(back, 0, 0);
            settingsHealth = new Label { AutoSize = true, Anchor = AnchorStyles.Right, ForeColor = UiStyle.Secondary, Margin = new Padding(8, 0, 8, 0) }; header.Controls.Add(settingsHealth, 1, 0);
            settingsPause = UiButton("暂停监测", delegate { Toggle(); RefreshUi(); }); header.Controls.Add(settingsPause, 2, 0);

            settingsBody = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
            settingsBody.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0)); settingsBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            settingsBody.RowStyles.Add(new RowStyle(SizeType.AutoSize)); settingsBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var categoryBar = new FlowLayoutPanel { Name = "SettingsCategoryBar", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(20, 4, 20, 8), WrapContents = false };
            Label categoryLabel = UiLabel("设置分类"); categoryBar.Controls.Add(categoryLabel);
            settingsCategories = new ComboBox { Name = "SettingsCategories", DropDownStyle = ComboBoxStyle.DropDownList, Width = 240, Margin = new Padding(12, 2, 0, 0) };
            categoryBar.Controls.Add(settingsCategories); settingsBody.Controls.Add(categoryBar, 0, 0); settingsBody.SetColumnSpan(categoryBar, 2);
            settingsSidebar = new ListBox { Name = "SettingsSidebar", Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, IntegralHeight = false, BackColor = BackColor, Margin = new Padding(12, 12, 4, 12) };
            settingsBody.Controls.Add(settingsSidebar, 0, 1);
            var pages = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }; settingsBody.Controls.Add(pages, 1, 1);
            settingsPages = new Panel[settingsNames.Length];
            var content = new FlowLayoutPanel[settingsNames.Length];
            for (int i = 0; i < settingsNames.Length; i++)
            {
                var page = new Panel { Name = "SettingsCategory" + i, Dock = DockStyle.Fill, Visible = false };
                settingsPages[i] = page; pages.Controls.Add(page); content[i] = UiStyle.Content(); WindowUi.ScrollContent(page, content[i]);
            }
            BuildGeneralSettings(content[0]); BuildRecoverySettings(content[1]);
            content[2].Controls.Add(UiStyle.Section("当前额度")); settingsUsage = UiStyle.Help(usageSummary.AccessibleDescription); content[2].Controls.Add(settingsUsage);
            content[2].Controls.Add(UiButton("额度详情", delegate { ShowUsageDetails(); })); AddQuotaSettings(content[2]);
            AddScheduleSettings(content[3]); AddTimedInstructions(content[3]);
            BuildDiagnosticSettings(content[4]); BuildAboutSettings(content[5]);
            settingsCategories.SelectedIndexChanged += delegate { if (!changingSettingsCategory) SwitchSettingsCategory(settingsCategories.SelectedIndex); };
            settingsSidebar.SelectedIndexChanged += delegate { if (!changingSettingsCategory) SwitchSettingsCategory(settingsSidebar.SelectedIndex); };
            settingsWindow.Controls.Add(settingsBody); settingsWindow.Controls.Add(header); Controls.Add(settingsWindow);
            Action fitNavigation = delegate {
                bool wide = settingsWindow.ClientSize.Width >= UiStyle.Px(this, 760);
                categoryBar.Visible = !wide; settingsSidebar.Visible = wide;
                settingsBody.ColumnStyles[0].Width = wide ? UiStyle.Px(this, 164) : 0;
                settingsCategories.Width = Math.Max(120, settingsWindow.ClientSize.Width - categoryBar.Padding.Horizontal - categoryLabel.PreferredSize.Width - settingsCategories.Margin.Horizontal - 8);
            };
            settingsWindow.SizeChanged += delegate { fitNavigation(); }; settingsWindow.FontChanged += delegate { fitNavigation(); }; categoryLabel.TextChanged += delegate { fitNavigation(); };
            settingsWindow.Disposed += delegate { if (settingsRefresh != null) { settingsRefresh.Stop(); settingsRefresh.Dispose(); } };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!installingUpdate && !ResolveQuotaEdits()) e.Cancel = true; };
            UiText.Apply(settingsWindow); RefreshSettingsLanguage(); SwitchSettingsCategory(settingsCategory);
        }

        void RefreshSettingsLanguage()
        {
            if (settingsWindow == null || settingsWindow.IsDisposed) return;
            changingSettingsCategory = true;
            try
            {
                settingsCategories.Items.Clear(); settingsSidebar.Items.Clear();
                foreach (string name in settingsNames) { settingsCategories.Items.Add(UiText.Get(name)); settingsSidebar.Items.Add(UiText.Get(name)); }
                settingsCategories.SelectedIndex = settingsCategory; settingsSidebar.SelectedIndex = settingsCategory;
            }
            finally { changingSettingsCategory = false; }
            UiText.Apply(settingsWindow);
        }

        void SwitchSettingsCategory(int index)
        {
            if (index < 0 || settingsPages == null) return;
            if (index != settingsCategory && !ResolveQuotaEdits()) { RefreshSettingsLanguage(); return; }
            settingsCategory = index;
            for (int i = 0; i < settingsPages.Length; i++) settingsPages[i].Visible = i == index;
            settingsPages[index].BringToFront(); RefreshSettingsLanguage();
        }

        void BuildGeneralSettings(FlowLayoutPanel content)
        {
            content.Controls.Add(UiStyle.Section("语言"));
            var language = new ComboBox { Name = "LanguageSelector", DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
            language.Items.AddRange(new object[] { "简体中文", "English" }); language.SelectedIndex = UiText.English ? 1 : 0;
            language.SelectedIndexChanged += delegate { UiText.SetLanguage(language.SelectedIndex == 1 ? "en" : "zh-CN"); }; content.Controls.Add(language);
            content.Controls.Add(UiStyle.Section("快捷键"));
            content.Controls.Add(UiStyle.Help(shortcut + "+F8 连接窗口\n" + shortcut + "+F9 暂停或继续监测\n" + shortcut + "+F10 紧急停止\nF1 使用说明 · F2 本机自检 · F4 后台自测"));
            content.Controls.Add(UiStyle.Help("启动默认仅观察，确认目标后可在首页开启自动恢复"));
        }

        void BuildRecoverySettings(FlowLayoutPanel content)
        {
            content.Controls.Add(UiStyle.Section("监测方式")); UiText.Set(background, "后台监测"); content.Controls.Add(background);
            content.Controls.Add(UiStyle.Help("后台支持遮挡；接口可核验时也支持最小化\n最小化时接口不可用则等待；前台模式需按钮可见"));
            AddScopeSettings(content);
            var advanced = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Visible = false, Margin = Padding.Empty };
            advanced.Controls.Add(UiButton("选择窗口", delegate { ChangeWindow(); }));
            advanced.Controls.Add(UiButton("选择按钮区域", delegate { RequestRegion(); }));
            advanced.Controls.Add(UiStyle.Help("手动标定：将鼠标移到目标按钮中心，按 " + shortcut + "+F7"));
            var crop = UiStyle.Actions(); crop.Controls.Add(UiLabel("手动区域大小（像素）")); crop.Controls.Add(cropSize); advanced.Controls.Add(crop);
            content.Controls.Add(UiStyle.Section("高级定位"));
            content.Controls.Add(UiButton("展开或收起定位工具", delegate { advanced.Visible = !advanced.Visible; })); content.Controls.Add(advanced);
        }

        string DiagnosticSummary()
        {
            return UiText.Diagnostics(scores.Text) + Environment.NewLine + UiText.Diagnostics(activity.Summary(DateTime.UtcNow, monitoring, updating, background.Checked, target == null ? -1 : target.CandidateCount, gate.Attempts, gate.Verified)) +
                Environment.NewLine + WindowCapture.BorderStatus + (journal.WriteFailed ? Environment.NewLine + UiText.Get("日志暂时无法写入") : "");
        }

        void BuildDiagnosticSettings(FlowLayoutPanel content)
        {
            content.Controls.Add(UiStyle.Section("当前连接与识别"));
            var summary = UiStyle.Help("正在识别 Codex"); content.Controls.Add(summary);
            content.Controls.Add(UiButton("查看完整诊断状态", delegate { WindowUi.TextDetails(this, UiText.Get("诊断"), DiagnosticSummary()); }));
            content.Controls.Add(UiStyle.Section("日志与诊断包")); AddDiagnosticExportTools(content);
            content.Controls.Add(UiStyle.Section("高级诊断"));
            var tools = UiStyle.Actions(); tools.Visible = false;
            tools.Controls.Add(UiButton("打开演示", delegate { OpenDemo(); })); tools.Controls.Add(UiButton("本机自检", delegate { ShowSelfCheck(); }));
            tools.Controls.Add(UiButton("后台自测", delegate { ShowBackgroundSelfCheck(); })); tools.Controls.Add(UiButton("保存按钮图", delegate { SaveDiagnostic(); }));
            tools.Controls.Add(UiButton("打开日志", delegate { OpenLogs(); })); tools.Controls.Add(UiButton("导出捕获对照", async delegate { await ExportCaptureComparison(); }));
            content.Controls.Add(UiButton("展开或收起诊断工具", delegate { tools.Visible = !tools.Visible; })); content.Controls.Add(tools);
            settingsRefresh = new System.Windows.Forms.Timer { Interval = 1000 };
            settingsRefresh.Tick += delegate {
                if (!settingsWindow.Visible || settingsCategory != 4) return;
                if (detailedCaptureOption != null && detailedCaptureOption.Checked != Evidence.Enabled) detailedCaptureOption.Checked = Evidence.Enabled;
                summary.Text = UiText.Diagnostics(scores.Text) + Environment.NewLine + UiText.Get(statusSource);
            }; settingsRefresh.Start();
        }

        void BuildAboutSettings(FlowLayoutPanel content)
        {
            content.Controls.Add(UiStyle.Section("Codex Auto Resume " + Portable.Version)); content.Controls.Add(UiStyle.Help("单文件便携版 · Windows x64 · MIT"));
            updateCheckButton = UiButton("检查更新", async delegate { await CheckUpdatesAsync(true); }); content.Controls.Add(updateCheckButton);
            updateFeedback = UiStyle.Help(""); updateFeedback.Name = "UpdateFeedback"; content.Controls.Add(updateFeedback);
            var check = new CheckBox { Text = "自动检查更新", AutoSize = true, Checked = preferences.CheckUpdates };
            check.CheckedChanged += delegate { preferences.CheckUpdates = check.Checked; nextUpdateCheck = DateTime.UtcNow.AddSeconds(1); SavePreferences(); }; content.Controls.Add(check);
            content.Controls.Add(UiStyle.Help("启动后检查，运行中每小时检查一次；没有更新时保持安静"));
            content.Controls.Add(UiStyle.Section("帮助")); content.Controls.Add(UiButton("使用说明", delegate { ShowHelp(); })); content.Controls.Add(UiButton("许可证", delegate { ShowLicense(); }));
        }

        bool SaveQuotaPrompt()
        {
            if (quotaPromptEditor == null || quotaPromptEditor.IsDisposed) return true;
            string value = quotaPromptEditor.Text.Replace("\r\n", "\n");
            if (value == preferences.ContinuePrompt) return true;
            if (!ComposerRequest.ValidPrompt(value)) { UiText.Set(quotaPromptFeedback, "请输入有效的继续指令"); return false; }
            CancelQuota(); if (automatic.Checked) automatic.Checked = false;
            string previous = preferences.ContinuePrompt; preferences.ContinuePrompt = value;
            if (!SavePreferences()) { preferences.ContinuePrompt = previous; UiText.Set(quotaPromptFeedback, "保存失败，请检查本地存储"); return false; }
            UiText.Set(quotaPromptFeedback, "指令已保存，请按需要开启自动恢复"); return true;
        }

        bool ResolveQuotaEdits()
        {
            if (quotaPromptEditor == null || quotaPromptEditor.IsDisposed || quotaPromptEditor.Text.Replace("\r\n", "\n") == preferences.ContinuePrompt) return true;
            DialogResult result = UiDialog.Show(this, "继续指令有未保存的修改，是否保存？\n选择“否”放弃修改，选择“取消”继续编辑", "未保存的修改", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (result == DialogResult.Cancel) return false;
            if (result == DialogResult.Yes) return SaveQuotaPrompt();
            quotaPromptEditor.Text = preferences.ContinuePrompt; UiText.Set(quotaPromptFeedback, ""); return true;
        }
    }
}
