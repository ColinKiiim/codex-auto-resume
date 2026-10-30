using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexAutoResume
{
    internal sealed partial class MainForm
    {
        readonly UserPreferences preferences = UserPreferences.Load();
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        readonly Label targetName = new LinkLabel { Name = "TargetName", Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, LinkBehavior = LinkBehavior.HoverUnderline, LinkColor = SystemColors.ControlText };
        readonly Label modeName = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.DimGray };
        readonly Label recoveryCount = new LinkLabel { AutoSize = true, LinkBehavior = LinkBehavior.HoverUnderline, LinkColor = UiStyle.Secondary };
        readonly ProgressHistory recentLog = new ProgressHistory { PinLeft = true, Name = "RecentLog", Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, Margin = Padding.Empty, TabStop = true, ScrollBars = ScrollBars.Vertical, MaxLength = 0, HideSelection = false };
        readonly Queue<string> recentEvents = new Queue<string>();
        readonly MonitorPulse progressPulse = new MonitorPulse(DateTime.UtcNow);
        readonly System.Windows.Forms.Timer stabilityTimer = new System.Windows.Forms.Timer { Interval = 200 };
        readonly System.Windows.Forms.Timer questionTimer = new System.Windows.Forms.Timer { Interval = 2100 };
        readonly System.Windows.Forms.Timer frameTimer = new System.Windows.Forms.Timer { Interval = 100 };
        bool frameDirty;
        readonly LinkLabel regionAction = new LinkLabel { AutoSize = true, Text = "选择区域", Visible = false };
        readonly Label previewCaption = new Label { Dock = DockStyle.Fill, AutoEllipsis = false, TextAlign = ContentAlignment.MiddleLeft, Text = "等待识别", Margin = Padding.Empty };
        readonly Label previewTime = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty, ForeColor = Color.DimGray };
        TableLayoutPanel stateLayout;
        bool questionPreview;
        DateTime previewContentAt;
        readonly ToolTip tips = new ToolTip();
        Form settingsWindow;
        Panel recordsPage;
        ProgressHistory recordsView;
        Button recordsPause, settingsPause;
        bool showingRecoveryRecords;
        int recoveryRecordPosition, activityRecordPosition, recoveryRecordFirst, activityRecordFirst, recoveryRecordSelection, activityRecordSelection;
        int recoveryRecordHorizontal, activityRecordHorizontal;
        bool loadingOptions, checkingUpdate, installingUpdate, shownOnce;
        readonly HashSet<string> announcedVersions = new HashSet<string>();
        DateTime nextUpdateCheck = DateTime.UtcNow.AddSeconds(5), nextFrameDispatch;
        int frameWakePending;
        readonly string startupMode, updateJobPath;

        static Button UiButton(string text, EventHandler action)
        {
            Button button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(76, 32), Padding = new Padding(6, 2, 6, 2), Margin = new Padding(3), FlatStyle = FlatStyle.System };
            button.Click += action; return button;
        }
        static Label UiLabel(string text)
        { return new Label { Text = text, AutoSize = true, Margin = new Padding(0, 5, 0, 5) }; }

        void WatchResume(ButtonState state)
        {
            recoveryWindow.Observe(state, DateTime.UtcNow);
            lastObservedState = state;
            if (monitoring && automatic.Checked && (state == ButtonState.Resume || QuotaSquareWake && state == ButtonState.Running)) stabilityTimer.Start(); else stabilityTimer.Stop();
        }
        void RecentProgress(string text)
        {
            string entry = DateTime.Now.ToString("HH:mm:ss") + "  " + text;
            recentEvents.Enqueue(entry); recentLog.UpdateHistory(UiText.Get(entry), false);
        }

        void ResetPreview()
        {
            controlPreview = false;
            previewContentAt = DateTime.MinValue;
            Image old = preview.Image; preview.Image = null; if (old != null) old.Dispose();
            recoveryWindow.Reset(); lastObservedState = ButtonState.Unknown;
            SetPreviewKind(false); UiText.Set(previewCaption, "采样图"); previewTime.Text = "";
        }
        void UpdateQuestionPreview(Bitmap image, DateTime contentAt)
        {
            Rectangle bounds = Rectangle.Intersect(new Rectangle(Point.Empty, image.Size), questions.PreviewBounds);
            if (bounds.IsEmpty) return;
            Bitmap cropped = image.Clone(bounds, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            Image old = preview.Image; preview.Image = cropped; if (old != null) old.Dispose(); previewContentAt = contentAt; SetPreviewKind(true);
        }
        void ShowPreviewDetail()
        {
            if (preview.Image == null) return;
            using (Form detail = new Form { Text = questionPreview ? "最近识别 · 问答区域" : controlPreview ? "后台控件状态" : "最近识别 · 采样图", Font = Font, ClientSize = questionPreview ? new Size(760, 440) : new Size(240, 240), StartPosition = FormStartPosition.CenterParent, Icon = Icon })
            using (Bitmap snapshot = new Bitmap(preview.Image))
            {
                detail.Controls.Add(new PictureBox { Dock = DockStyle.Fill, Image = snapshot, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(35, 35, 35) });
                detail.Controls.Add(WindowUi.Guide(UiText.Get(previewCaption.Text) + " · " + (previewContentAt == DateTime.MinValue ? "—" : previewContentAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")), 32, 8));
                UiText.Apply(detail); WindowUi.Modal(detail, this);
            }
        }

        void RefreshUi()
        {
            if (IsDisposed) return;
            RefreshSchedule();
            UiText.Set(health, diagnosing ? "● 自测中" : !monitoring ? "● 已暂停" : automatic.Checked ? "● 正在监测" : "● 正在监测 · 仅观察");
            health.ForeColor = SystemInformation.HighContrast ? SystemColors.WindowText : diagnosing ? Color.SteelBlue : monitoring ? Color.FromArgb(36, 113, 83) : UiStyle.Secondary;
            UiText.Set(toggle, monitoring ? "暂停监测" : "继续监测"); toggle.Enabled = !diagnosing;
            string title = target == null ? "正在寻找 Codex" : Native.Title(target.Window);
            targetName.Text = target == null ? UiText.Get(title) : String.IsNullOrWhiteSpace(title) ? "Codex" : title;
            tips.SetToolTip(targetName, title);
            UiText.Set(modeName, background.Checked ? "后台监测" : "前台监测");
            UiText.Set(recoveryCount, "本轮已恢复 " + gate.Verified + " 次");
            if (recordsPause != null) { UiText.Set(recordsPause, monitoring ? "暂停监测" : "继续监测"); recordsPause.Enabled = toggle.Enabled; }
            if (settingsPause != null && !settingsPause.IsDisposed) { UiText.Set(settingsPause, monitoring ? "暂停监测" : "继续监测"); settingsPause.Enabled = toggle.Enabled; }
            if (settingsHealth != null && !settingsHealth.IsDisposed) settingsHealth.Text = UiText.Get(diagnosing ? "自测中" : monitoring ? "监测中" : "已暂停");
            previewTime.Text = preview.Image == null ? "" : previewContentAt == DateTime.MinValue ? "—" : previewContentAt.ToLocalTime().ToString("HH:mm:ss");
            tips.SetToolTip(status, UiText.Get(statusSource));
            FitHome();
        }

        void RefreshLanguage()
        {
            if (IsDisposed) return;
            preferences.Language = UiText.Language;
            UiText.Apply(this); RefreshUi(); UiText.Set(status, statusSource);
            SetPreviewKind(questionPreview);
            recentLog.UpdateHistory(UiText.Get(String.Join(Environment.NewLine, recentEvents.ToArray())), true);
            RefreshSettingsLanguage(); RefreshRecordLanguage(); FitHome(); RenderUsage(); SavePreferences();
        }

        void ChangeWindow()
        {
            if (diagnosing || installingUpdate) return;
            if (!monitoring) { SetStatus("请先继续监测，再更换窗口"); return; }
            CancelSchedule(true); CancelQuota();
            generation++; WindowCapture.StopCapture(); target = null; activity.ResetSource(); ResetPreview();
            waitingForChoice = false; nextDiscovery = DateTime.MinValue; Discover(); RefreshUi();
        }
        void RequestRegion()
        {
            if (diagnosing || installingUpdate || target == null) { SetStatus("请先连接 Codex 窗口"); return; }
            if (!monitoring) { SetStatus("请先继续监测，再选择区域"); return; }
            target.RequestRegionChoice(); regionChoiceRequested = true; nextMonitor = DateTime.MinValue;
            SetStatus("请选择要监测的按钮");
        }
        void OpenDemo()
        {
            if (diagnosing || installingUpdate) return;
            if (demo == null || demo.IsDisposed) demo = new DemoForm(shortcut);
            demo.Show(); demo.Activate(); Connect(Target.FromWindow(demo.Handle, true));
        }
        void OpenLogs()
        { try { Directory.CreateDirectory(logDirectory); Process.Start(new ProcessStartInfo(logDirectory) { UseShellExecute = true }); } catch (Exception e) { UiDialog.Show(this, e.Message, "无法打开日志"); } }
        async Task ExportCaptureComparison()
        {
            if (diagnosing || updating || sampler.IsBusy || target == null) { SetStatus("请先连接窗口并等待当前检查完成"); return; }
            Target connected = target; bool resume = monitoring;
            Stop("正在导出捕获对照"); diagnosing = true; int revision = generation; RefreshUi();
            string directory = Path.Combine(logDirectory, "capture-comparison-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
            try
            {
                await CaptureComparison.RunAsync(connected, sampler, directory);
                if (!IsDisposed) UiDialog.Show(UpdateDialogOwner(), UiText.English ? "Saved window images and a comparison report locally:\n" + directory : "已在本地保存窗口画面与对照报告：\n" + directory, UiText.English ? "Capture comparison" : "捕获对照");
            }
            catch (Exception error) { if (!IsDisposed) UiDialog.Show(UpdateDialogOwner(), error.Message, UiText.English ? "Capture comparison failed" : "捕获对照失败"); }
            finally
            {
                diagnosing = false;
                if (!IsDisposed && resume && generation == revision) { monitoring = true; recoveryWindow.Reset(); nextMonitor = DateTime.MinValue; TickMonitor(); }
                RefreshUi();
            }
        }

        bool SavePreferences()
        {
            preferences.Background = background.Checked; preferences.CropSize = (int)cropSize.Value;
            try { preferences.Save(); return true; } catch (Exception e) { journal.Write("diagnostics.log", "settings-error", e.GetType().Name, DateTime.UtcNow); return false; }
        }
        string RecoveryHistoryPath { get { return Path.Combine(Portable.DataDirectory, "recovery-history.txt"); } }
        void SaveRecovery(DateTime now)
        {
            try { Directory.CreateDirectory(Portable.DataDirectory); File.AppendAllText(RecoveryHistoryPath, now.ToUniversalTime().ToString("O") + Environment.NewLine); }
            catch (Exception e) { journal.Write("diagnostics.log", "recovery-history-error", e.GetType().Name, DateTime.UtcNow); }
        }
        System.Collections.Generic.List<DateTime> LoadRecoveries()
        {
            var result = new System.Collections.Generic.List<DateTime>();
            try
            {
                if (File.Exists(RecoveryHistoryPath)) foreach (string line in File.ReadLines(RecoveryHistoryPath))
                { DateTime value; if (DateTime.TryParse(line, null, System.Globalization.DateTimeStyles.RoundtripKind, out value)) result.Add(value.ToUniversalTime()); }
            }
            catch (Exception e) { journal.Write("diagnostics.log", "recovery-history-error", e.GetType().Name, DateTime.UtcNow); }
            result.Sort(); result.Reverse(); return result;
        }
        string RecoveryPreview()
        {
            var lines = new System.Collections.Generic.List<string>(); DateTime cutoff = DateTime.UtcNow.AddHours(-24);
            foreach (DateTime item in LoadRecoveries())
                if (item >= cutoff && item <= DateTime.UtcNow) { lines.Add(item.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + "  已恢复"); if (lines.Count == 4) break; }
            return lines.Count == 0 ? "最近 24 小时没有恢复记录" : "最近 24 小时的恢复记录\n" + String.Join("\n", lines.ToArray());
        }
        void InitializeRecordsPage()
        {
            recordsPage = new Panel { Dock = DockStyle.Fill, BackColor = BackColor, Padding = new Padding(24, 18, 24, 18), Visible = false };
            var heading = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, MinimumSize = new Size(0, 42), WrapContents = true };
            heading.Controls.Add(UiButton("返回监测", delegate { recordsPage.Visible = false; }));
            recordsFilter = new ComboBox { Name = "RecordsFilter", DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, Margin = new Padding(8, 6, 8, 0) };
            recordsFilter.Items.Add(UiText.Get("恢复记录")); recordsFilter.Items.Add(UiText.Get("全部活动")); recordsFilter.SelectedIndex = 1;
            recordsFilter.SelectedIndexChanged += delegate { if (!changingRecordFilter) ShowRecords(recordsFilter.SelectedIndex == 0); }; heading.Controls.Add(recordsFilter);
            recordsPause = UiButton("暂停监测", delegate { Toggle(); RefreshUi(); }); heading.Controls.Add(recordsPause);
            recordsView = new ProgressHistory { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false };
            recordsPage.Controls.Add(recordsView); recordsPage.Controls.Add(heading); Controls.Add(recordsPage);
        }
        void ShowRecords(bool recovery)
        {
            changingRecordFilter = true; recordsFilter.SelectedIndex = recovery ? 0 : 1; changingRecordFilter = false;
            if (recordsView.IsHandleCreated)
            {
                if (showingRecoveryRecords) { recoveryRecordPosition = recordsView.SelectionStart; recoveryRecordFirst = recordsView.FirstVisibleLine; recoveryRecordSelection = recordsView.SelectionLength; recoveryRecordHorizontal = recordsView.HorizontalOffset; }
                else { activityRecordPosition = recordsView.SelectionStart; activityRecordFirst = recordsView.FirstVisibleLine; activityRecordSelection = recordsView.SelectionLength; activityRecordHorizontal = recordsView.HorizontalOffset; }
            }
            showingRecoveryRecords = recovery;
            if (recovery)
            {
                var entries = LoadRecoveries(); var lines = new System.Collections.Generic.List<string>();
                foreach (DateTime item in entries) lines.Add(item.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + "  已恢复");
                recordsView.Text = lines.Count == 0 ? "暂无恢复记录" : String.Join(Environment.NewLine, lines.ToArray());
            }
            else recordsView.Text = UiText.Get(logBox.Text);
            recordsPage.Visible = true; recordsPage.BringToFront();
            recordsView.RestoreView(recovery ? recoveryRecordFirst : activityRecordFirst, recovery ? recoveryRecordPosition : activityRecordPosition, recovery ? recoveryRecordSelection : activityRecordSelection);
            recordsView.RestoreHorizontal(recovery ? recoveryRecordHorizontal : activityRecordHorizontal);
            RefreshUi();
        }
        void ShowActivity() { ShowRecords(false); }
        void OnCaptureFrame()
        {
            if (!IsHandleCreated || IsDisposed || Interlocked.Exchange(ref frameWakePending, 1) != 0) return;
            try
            {
                BeginInvoke((Action)delegate
                {
                    Interlocked.Exchange(ref frameWakePending, 0);
                    if (IsDisposed) return;
                    frameDirty = true; frameTimer.Start(); DispatchFrame();
                });
            }
            catch (InvalidOperationException) { Interlocked.Exchange(ref frameWakePending, 0); }
        }

        void DispatchFrame()
        {
            if (!monitoring || !background.Checked || diagnosing) { frameDirty = false; frameTimer.Stop(); return; }
            if (!frameDirty) { frameTimer.Stop(); return; }
            // Keep a trailing wake when the last change arrives during a read or
            // inside the throttle window; it must not wait for the 3s fallback.
            if (updating || DateTime.UtcNow < nextFrameDispatch || DateTime.UtcNow < nextMonitor) return;
            frameDirty = false; nextFrameDispatch = DateTime.UtcNow.AddMilliseconds(100); TickMonitor();
            if (!frameDirty) frameTimer.Stop();
        }

        static string FriendlyStatus(string message)
        {
            if (message.Contains("空闲 1.5 秒")) return "等待你完成操作";
            if (message.Contains("等待用户回答") || message.StartsWith("等待你的回答")) return "等待你的回答";
            if (message.Contains("问答面板已消失")) return "继续监测";
            if (message.Contains("最小化") && (message.Contains("还原") || message.Contains("接口不可用"))) return "等待可核验的后台接口";
            if (message.Contains("已隐藏")) return "显示 Codex 后自动继续";
            if (message.Contains("多个候选")) return "发现多个按钮，暂不自动恢复";
            if (message.Contains("原选区域")) return "请重新选择按钮区域";
            if (message.Contains("停止观察") || message.Contains("手动停止") || message.Contains("紧急停止")) return "已暂停";
            if (message.Contains("等待选择窗口")) return "请选择要监测的窗口";
            if (message.Contains("自动寻找 Codex") || message.Contains("等待 Codex 窗口")) return "正在寻找 Codex";
            if (message.Contains("已投递") || message.Contains("等待白色方块") || message.Contains("等待新捕获帧中的白色方块") || message.Contains("已发送一次鼠标点击")) return "正在恢复…";
            if (message.Contains("多个按钮")) return message;
            if (message.Contains("前台且按钮") || message.Contains("前台可见")) return "切回 Codex 后自动继续";
            if (message.Contains("捕获") && (message.Contains("无法") || message.Contains("超时") || message.Contains("尚未结束") || message.Contains("未收到") || message.Contains("耗时较长") || message.Contains("会话已关闭"))) return "暂时无法获取画面，正在重试";
            if (message.Contains("正在寻找蓝色") || message.Contains("正在自动寻找蓝色")) return "正在识别 Codex";
            if (message.Contains("位置变化") || message.Contains("窗口状态变化") || message.Contains("图标变化") || message.Contains("窗口大小或位置")) return "界面正在变化，继续监测";
            if (message.StartsWith("已连接窗口")) return "已连接 Codex";
            if (message.Contains("已取消手动区域选择")) return "继续监测";
            return message;
        }
        static bool Significant(string message)
        {
            foreach (string word in new[] { "恢复", "暂停", "继续监测", "已连接", "选择", "重试", "自检", "自测", "更新", "等待你的回答", "保存", "失败", "Codex 正在运行", "等待操作", "等待发送", "正在识别 Codex", "正在寻找 Codex" })
                if (message.Contains(word)) return true;
            return false;
        }
        void ObserveStatus(ButtonState state)
        {
            SetStatus(state == ButtonState.Running ? "Codex 正在运行" : state == ButtonState.Resume ? "检测到暂停" : state == ButtonState.Send ? "等待发送" : state == ButtonState.VoiceClose ? "语音模式 · 等待操作" : state == ButtonState.Voice ? "等待操作" : "正在识别 Codex");
        }
        void RecordVerified(int previous)
        { if (gate.Verified > previous) { if (target != null && !target.Demo) SaveRecovery(DateTime.UtcNow); CancelSchedule(false, "completed"); AppendVisible("已恢复", "recovery"); } RefreshUi(); }

        Form UpdateDialogOwner() { return settingsWindow != null && !settingsWindow.IsDisposed && settingsWindow.Visible ? settingsWindow : this; }

        async Task CheckUpdatesAsync(bool manual)
        {
            if (checkingUpdate || installingUpdate || diagnosing || IsDisposed || (!manual && !preferences.CheckUpdates)) return;
            checkingUpdate = true; nextUpdateCheck = DateTime.UtcNow.Add(Updates.CheckInterval);
            if (updateCheckButton != null && !updateCheckButton.IsDisposed) updateCheckButton.Enabled = false;
            if (manual && updateFeedback != null) UiText.Set(updateFeedback, "正在检查更新…");
            try
            {
                ReleaseCheck check = await Updates.CheckLatestAsync(lifetime.Token);
                AvailableUpdate update = check.Update;
                if (IsDisposed || lifetime.IsCancellationRequested) return;
                if (update == null) { if (manual) ShowUpdateFeedback(check.Message(Portable.Version, UiText.English)); return; }
                if (!manual && (preferences.SkippedVersion == update.Version || announcedVersions.Contains(update.Version))) return;
                announcedVersions.Add(update.Version);
                using (UpdatePrompt prompt = new UpdatePrompt(update, Portable.Version))
                {
                    DialogResult result = WindowUi.Modal(prompt, UpdateDialogOwner());
                    if (result == DialogResult.Ignore) { preferences.SkippedVersion = update.Version; SavePreferences(); }
                    if (result == DialogResult.OK) await InstallUpdateAsync(update);
                }
            }
            catch (OperationCanceledException) { if (manual && !IsDisposed && !lifetime.IsCancellationRequested) ShowUpdateFeedback(UiText.Get("检查更新超时，请稍后重试。")); }
            catch (Exception e)
            {
                journal.Write("diagnostics.log", "update-check", e.GetType().Name + ": " + e.Message, DateTime.UtcNow);
                if (manual && !IsDisposed) ShowUpdateFeedback(UiText.Get("暂时无法检查更新，请稍后重试。") + "\n" + e.Message);
            }
            finally { checkingUpdate = false; if (updateCheckButton != null && !updateCheckButton.IsDisposed) updateCheckButton.Enabled = true; }
        }
        void ShowUpdateFeedback(string message)
        {
            if (updateFeedback != null && !updateFeedback.IsDisposed && settingsWindow.Visible && settingsCategory == 5) updateFeedback.Text = message;
            else UiDialog.Show(this, message, "检查更新");
        }
        ResumeMemory RememberForUpdate()
        {
            ResumeMemory memory = new ResumeMemory { Monitoring = monitoring, Automatic = automatic.Checked && !resumeSchedule.Active, Background = background.Checked };
            if (target != null && !target.Demo)
            {
                memory.Window = target.Window.ToInt64(); memory.ProcessId = target.ProcessId; memory.ProcessStart = target.ProcessStart; memory.ProcessPath = target.ProcessPath;
                memory.AutoLocate = target.Automatic; memory.Width = target.Width; memory.Height = target.Height; memory.OffsetX = target.OffsetX; memory.OffsetY = target.OffsetY; memory.CropSize = target.CropSize; memory.Dpi = target.Dpi; memory.Region = target.selection.Remember();
            }
            return memory;
        }
        async Task InstallUpdateAsync(AvailableUpdate update)
        {
            installingUpdate = true; RefreshUi();
            using (CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            using (Form progress = new Form { Text = "正在更新", Font = Font, ClientSize = new Size(420, 150), StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false })
            {
                bool finished = false;
                Label message = WindowUi.Guide("正在下载并校验更新…", 54, 14);
                progress.Controls.Add(new ProgressBar { Dock = DockStyle.Top, Height = 20, Style = ProgressBarStyle.Marquee }); progress.Controls.Add(message);
                Button cancelButton = UiButton("取消", delegate { cancel.Cancel(); }); cancelButton.Dock = DockStyle.Bottom; progress.Controls.Add(cancelButton);
                progress.FormClosing += delegate { if (!finished) cancel.Cancel(); }; UiText.Apply(progress); WindowUi.Show(progress, this);
                try
                {
                    string candidate = await Updates.DownloadAsync(update, cancel.Token);
                    cancel.Token.ThrowIfCancellationRequested(); if (IsDisposed) return;
                    UiText.Set(message, "正在准备替换并重启…"); SavePreferences();
                    UpdateJob job = Updates.Prepare(candidate, update.Version, RememberForUpdate());
                    using (Process helper = Updates.StartHelper(job)) { if (helper == null) throw new IOException("更新助手未能启动"); }
                    finished = true; Stop("正在更新…"); progress.Close(); Close();
                }
                catch (OperationCanceledException) { }
                catch (Exception e)
                { if (!IsDisposed) UiDialog.Show(progress, "更新未完成，当前版本保持可用。\n" + e.Message, "更新失败", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                finally { finished = true; progress.Close(); installingUpdate = false; RefreshUi(); }
            }
        }
        void RestoreAfterUpdate()
        {
            if (String.IsNullOrEmpty(updateJobPath)) return;
            try
            {
                UpdateJob job = Updates.LoadJob(updateJobPath);
                if (!String.Equals(job.Destination, Updates.Executable, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新目标不匹配");
                if (startupMode == "--updated")
                {
                    if (job.Version != Portable.Version || Updates.Hash(Updates.Executable) != job.NewHash) throw new InvalidDataException("新版文件不匹配");
                    ResumeMemory memory = job.Resume; Target connected = memory == null ? null : memory.Reconnect();
                    loadingOptions = true;
                    try { if (memory != null) background.Checked = memory.Background; automatic.Checked = connected != null && memory.Automatic; }
                    finally { loadingOptions = false; }
                    if (connected != null) Connect(connected);
                    if (memory != null && !memory.Monitoring) Stop("已暂停");
                    if (connected == null) { automatic.Checked = false; SetStatus("更新完成，已切换为仅观察"); }
                    else SetStatus(memory.Monitoring ? "更新完成，继续监测" : "更新完成，已暂停");
                    File.WriteAllText(Path.Combine(job.Directory, "ready"), "ready");
                }
                else if (startupMode == "--update-failed")
                {
                    automatic.Checked = false;
                    UiDialog.Show(this, Updates.FailureSummary(job) + "\n" + Updates.FailureDetail(job), "更新结果");
                }
            }
            catch (Exception e) { automatic.Checked = false; journal.Write("diagnostics.log", "update-startup", e.Message, DateTime.UtcNow); }
        }
    }
}
