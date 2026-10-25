using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace CodexAutoResume
{
    internal static class Program
    {
        [STAThread] public static int Main(string[] args)
        {
            DesktopIdentity.Initialize();
            Native.SetDpi();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 2 && args[0] == "--self-check") return Portable.SelfCheck(args[1]);
            if (args.Length == 2 && args[0] == "--background-self-check") return BackgroundDiagnostics.RunCommand(args[1]);
            if (args.Length == 2 && args[0] == "--apply-update") return Updates.Apply(args[1]);
            bool created;
            using (System.Threading.Mutex mutex = new System.Threading.Mutex(true, "Local\\CodexAutoResume-v1", out created))
            {
                if (!created) { UiText.Language = UserPreferences.Load().Language; MessageBox.Show(UiText.Get("CodexAutoResume 已在运行，请使用现有窗口。"), "CodexAutoResume"); return 0; }
                Application.Run(new MainForm(args));
            }
            return 0;
        }
    }

    internal sealed partial class MainForm : Form
    {
        readonly Recognizer recognizer = new Recognizer();
        readonly CaptureSampler sampler = new CaptureSampler(WindowCapture.Snapshot, WindowCapture.ActionSnapshot);
        readonly QuestionDetector questions = new QuestionDetector();
        readonly MonitorActivity activity = new MonitorActivity();
        readonly MonitorPulse filePulse = new MonitorPulse(DateTime.UtcNow);
        readonly Timer healthTimer = new Timer();
        readonly Label health = new Label { AutoSize = false, Dock = DockStyle.Bottom, Height = 28, ForeColor = Color.DimGray };
        readonly MonitorLog journal = new MonitorLog(Path.Combine(Portable.DataDirectory, "logs"));
        bool regionChoiceRequested;
        string lastCaptureError;
        DateTime lastCaptureErrorAt;
        DateTime nextMonitor;
        DateTime nextActionRefresh;
        long lastObservedContent = -1;
        ButtonState lastObservedState = ButtonState.Unknown;
        bool questionReported;
        ResumeGate gate = new ResumeGate();
        Target target;
        readonly Timer timer = new Timer();
        readonly CheckBox automatic = new CheckBox { Text = "允许自动点击（勾选即开始）", AutoSize = true };
        readonly CheckBox background = new CheckBox { Text = "后台监测", AutoSize = true };
        readonly NumericUpDown cropSize = new NumericUpDown { Minimum = 36, Maximum = 160, Value = 64, Increment = 4, Width = 70 };
        readonly PictureBox preview = new PictureBox { Width = 128, Height = 128, BackColor = Color.FromArgb(35, 35, 35), SizeMode = PictureBoxSizeMode.Zoom };
        readonly Label status = new Label { AutoSize = false, Height = 54, Dock = DockStyle.Fill, Text = "自动查找 Codex；勾选允许自动点击即可开始。" };
        readonly Label scores = new Label { AutoSize = false, Width = 430, Height = 120 };
        readonly TextBox logBox = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
        readonly Button toggle = new Button { Text = "开始观察", AutoSize = true };
        readonly string logDirectory = Path.Combine(Portable.DataDirectory, "logs");
        DemoForm demo;
        bool monitoring, updating;
        bool diagnosing;
        System.Threading.CancellationTokenSource diagnosticCancellation;
        int generation;
        bool waitingForChoice;
        DateTime nextDiscovery;
        string lastStatus = "";
        string statusSource = "正在寻找 Codex";
        string shortcut = "Ctrl+Alt";
        Rectangle lastBadge;

        public MainForm() : this(new string[0]) { }
        public MainForm(string[] args)
        {
            if (args.Length == 2) { startupMode = args[0]; updateJobPath = args[1]; }
            ShowInTaskbar = true; ShowIcon = true; Icon = DesktopIdentity.LoadIcon();
            Font = new Font("Microsoft YaHei UI", 10);
            StartPosition = FormStartPosition.CenterScreen;
            loadingOptions = true; background.Checked = preferences.Background; cropSize.Value = preferences.CropSize; loadingOptions = false;
            UiText.Language = preferences.Language;
            InitializeCompactUi(); UiText.Apply(this); SetPreviewKind(false);
            UiText.Changed += RefreshLanguage;
            KeyPreview = true;
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.F1) { ShowHelp(); e.Handled = true; } else if (e.KeyCode == Keys.F2) { ShowSelfCheck(); e.Handled = true; } else if (e.KeyCode == Keys.F3) { ShowLicense(); e.Handled = true; } else if (e.KeyCode == Keys.F4) { ShowBackgroundSelfCheck(); e.Handled = true; } };
            automatic.CheckedChanged += delegate { if (!loadingOptions) ActivateOptions(automatic.Checked ? "自动恢复已开启" : "仅观察"); };
            background.CheckedChanged += delegate { if (loadingOptions) return; WindowCapture.StopCapture(); ActivateOptions(background.Checked ? "后台监测已开启" : "前台监测已开启"); SavePreferences(); };
            cropSize.ValueChanged += delegate { if (target != null && !target.Automatic) { generation++; target.CropSize = (int)cropSize.Value; gate.BreakStreak(); } };
            timer.Interval = 3000; timer.Tick += delegate { TickMonitor(); }; timer.Start();
            healthTimer.Interval = 1000; healthTimer.Tick += delegate { HealthTick(); }; healthTimer.Start();
            journal.Write("diagnostics.log", "startup", "version=" + Portable.Version + "; detail=10sec", DateTime.UtcNow);
            WindowCapture.FrameAvailable += OnCaptureFrame;
            Shown += delegate
            {
                uint[] modifiers = { 0x4003, 0x4007, 0x4006 };
                string[] names = { "Ctrl+Alt", "Ctrl+Alt+Shift", "Ctrl+Shift" };
                for (int i = 0; i < modifiers.Length; i++)
                {
                    bool a = Native.RegisterHotKey(Handle, 1, modifiers[i], (uint)Keys.F8);
                    bool b = Native.RegisterHotKey(Handle, 2, modifiers[i], (uint)Keys.F9);
                    bool c = Native.RegisterHotKey(Handle, 3, modifiers[i], (uint)Keys.F10);
                    bool d = Native.RegisterHotKey(Handle, 4, modifiers[i], (uint)Keys.F7);
                    if (a && b && c && d) { shortcut = names[i]; break; }
                    for (int j = 1; j <= 4; j++) Native.UnregisterHotKey(Handle, j);
                }
                monitoring = true;
                SetStatus("正在寻找 Codex");
                RestoreAfterUpdate(); shownOnce = true; RefreshUi();
                Task.Run((Action)Updates.CleanOldJobs);
                BeginInvoke((Action)TickMonitor);
            };
        }
        protected override void OnHandleCreated(EventArgs e)
        { base.OnHandleCreated(e); DesktopIdentity.ApplyToWindow(Handle); }
        protected override void DestroyHandle()
        { try { if (IsHandleCreated) DesktopIdentity.ClearWindow(Handle); } finally { base.DestroyHandle(); } }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312)
            {
                int id = m.WParam.ToInt32();
                if (diagnosing)
                {
                    if (id == 3 && diagnosticCancellation != null) diagnosticCancellation.Cancel();
                    base.WndProc(ref m); return;
                }
                if (id == 1 && toggle.Enabled) AutoStart();
                else if (id == 2 && toggle.Enabled) Toggle();
                else if (id == 3) Stop("紧急停止已触发");
                else if (id == 4 && toggle.Enabled) Calibrate();
            }
            base.WndProc(ref m);
        }
        void Calibrate()
        {
            Stop("正在标定");
            try
            {
                target = Target.Calibrate(demo != null && !demo.IsDisposed, (int)cropSize.Value);
                activity.ResetSource(); ResetPreview();
                gate = new ResumeGate(); lastBadge = Rectangle.Empty;
                SetStatus("备用手动标定成功；正在观察。按 " + shortcut + "+F8 可切回自动定位。");
                Rectangle r; string reason;
                if (target.Locate(recognizer, null, out r, out reason)) using (Bitmap b = Native.Capture(r)) UpdatePreview(b, recognizer.Classify(b));
                monitoring = true; toggle.Text = "暂停";
            }
            catch (Exception e) { target = null; SetStatus("标定失败：" + e.Message); }
        }
        void AutoStart()
        {
            Stop("正在连接前台 Codex 窗口");
            try
            {
                target = Target.AttachForeground(demo != null && !demo.IsDisposed);
                activity.ResetSource(); ResetPreview();
                gate = new ResumeGate(); lastBadge = Rectangle.Empty;
                monitoring = true; toggle.Text = "暂停";
                SetStatus("自动定位已启用，正在寻找按钮；窗口移动或调整大小后会重新定位");
            }
            catch (Exception e) { target = null; SetStatus(e.Message); }
        }
        void Connect(Target connected)
        {
            questions.Reset(); questionReported = false; nextMonitor = DateTime.MinValue;
            regionChoiceRequested = false; activity.ResetSource(); ResetPreview(); lastObservedContent = -1; nextActionRefresh = DateTime.MinValue;
            generation++; target = connected; gate = new ResumeGate(); lastBadge = Rectangle.Empty;
            waitingForChoice = false; monitoring = true; toggle.Text = "暂停监测";
            SetStatus(automatic.Checked ? "已连接窗口，自动点击中；暂时捕获不到也会持续监测" : "已连接窗口，持续观察；勾选允许自动点击即可生效");
        }
        void ActivateOptions(string message)
        {
            if (diagnosing) return;
            generation++; gate.BreakStreak();
            WatchResume(lastObservedState);
            questions.Reset(); nextMonitor = DateTime.MinValue;
            questionTimer.Stop();
            waitingForChoice = false; nextDiscovery = DateTime.MinValue;
            SetStatus(monitoring ? message : "已暂停");
        }
        void Discover()
        {
            if (waitingForChoice || DateTime.UtcNow < nextDiscovery) return;
            nextDiscovery = DateTime.UtcNow.AddSeconds(2);
            var windows = Target.FindAvailable(demo != null && !demo.IsDisposed);
            if (windows.Count == 0) { SetStatus("等待 Codex 窗口出现；检测会持续进行，无需快捷键"); return; }
            int index = 0;
            if (windows.Count > 1)
            {
                int revision = generation;
                using (WindowPicker picker = new WindowPicker(windows))
                {
                    DialogResult result = picker.ShowDialog(this);
                    if (revision != generation || !monitoring) return;
                    if (result != DialogResult.OK) { waitingForChoice = true; SetStatus("等待选择窗口；点击“选择窗口”继续，确认前不会点击"); return; }
                    index = picker.SelectedIndex;
                }
            }
            if (index >= 0 && index < windows.Count) Connect(windows[index]);
        }
        int ChooseRegion(Bitmap scan, System.Collections.Generic.List<LocatedButton> candidates)
        {
            gate.BreakStreak();
            regionChoiceRequested = false;
            int revision = generation;
            using (RegionPicker picker = new RegionPicker(scan, candidates))
            { DialogResult result = picker.ShowDialog(this); return result == DialogResult.OK && revision == generation && monitoring ? picker.SelectedIndex : -1; }
        }
        void Toggle()
        {
            if (diagnosing) return;
            if (monitoring) { Stop("已暂停"); return; }
            gate.BreakStreak(); monitoring = true; waitingForChoice = false; nextDiscovery = DateTime.MinValue; toggle.Text = "暂停监测";
            questions.Reset(); nextMonitor = DateTime.MinValue;
            SetStatus(automatic.Checked ? "监控中：允许自动点击" : "监控中：仅观察，不会点击");
        }
        void Stop(string message)
        {
            if (diagnosticCancellation != null) diagnosticCancellation.Cancel();
            WindowCapture.StopCapture();
            stabilityTimer.Stop();
            questionTimer.Stop();
            frameDirty = false; frameTimer.Stop();
            generation++; monitoring = false; gate.BreakStreak(); toggle.Text = "开始观察"; SetStatus(message);
            questions.Reset();
            regionChoiceRequested = false; if (target != null) target.CancelRegionChoice();
            HealthTick();
        }
        async void TickMonitor()
        {
            if (!monitoring || updating || diagnosing || DateTime.UtcNow < nextMonitor) return;
            if (target == null)
            { updating = true; try { Discover(); } catch (Exception e) { SetStatus("窗口检测暂不可用，继续等待：" + e.Message); } finally { updating = false; } return; }
            if (!Native.IsWindow(target.Window)) { generation++; target = null; ResetPreview(); waitingForChoice = false; nextDiscovery = DateTime.MinValue; WindowCapture.StopCapture(); SetStatus("正在寻找 Codex"); return; }
            if (background.Checked) { TickBackground(); return; }
            updating = true;
            Target connected = target; int revision = generation;
            try
            {
                if (questions.Waiting && await CheckForegroundQuestion(connected, revision)) return;
                if (!monitoring || target != connected || generation != revision) return;
                Rectangle crop; string reason;
                activity.Start(DateTime.UtcNow);
                if (!target.Locate(recognizer, regionChoiceRequested ? (Func<Bitmap, System.Collections.Generic.List<LocatedButton>, int>)ChooseRegion : null, out crop, out reason))
                {
                    if (!monitoring || target != connected || generation != revision) return;
                    if (reason == "正在自动寻找蓝色圆按钮；找不到时可用手动标定备用" || reason == "候选区域需要用户确认") activity.Foreground(DateTime.UtcNow);
                    gate.BreakStreak();
                    if (reason == "已取消区域选择") { SetStatus("已取消手动区域选择；保持连接，持续扫描，恢复单候选后自动继续"); return; }
                    if (reason == "候选区域需要用户确认") { CandidateWait(); return; }
                    gate.Observe(ButtonState.Unknown, DateTime.UtcNow);
                    if (reason == "正在自动寻找蓝色圆按钮；找不到时可用手动标定备用" && await CheckForegroundQuestion(connected, revision)) return;
                    if (!monitoring || target != connected || generation != revision) return;
                    SetStatus(reason);
                    return;
                }
                Detection d;
                using (Bitmap b = Native.Capture(crop)) { d = recognizer.Classify(b); UpdatePreview(b, d); }
                activity.Foreground(DateTime.UtcNow); activity.Recognized(d.State);
                Rectangle badgeScreen = d.ButtonBounds; badgeScreen.Offset(crop.Location);
                if (!d.ButtonBounds.IsEmpty && !lastBadge.IsEmpty &&
                    (Math.Abs(badgeScreen.X - lastBadge.X) > 2 || Math.Abs(badgeScreen.Y - lastBadge.Y) > 2)) gate.BreakStreak();
                lastBadge = badgeScreen;
                int previousVerified = gate.Verified;
                bool ready = gate.Observe(d.State, DateTime.UtcNow); RecordVerified(previousVerified);
                RefreshScores(d);
                if (gate.AwaitingRunning) { SetStatus("已点击一次，等待白色方块确认运行中…"); return; }
                if (!ready)
                {
                    ObserveStatus(d.State);
                    return;
                }
                if (!automatic.Checked) { SetStatus("检测到暂停 · 仅观察"); return; }
                if (!Native.UserIdle()) { SetStatus("三角形已确认；等待鼠标、键盘空闲 0.7 秒"); return; }
                Point center = new Point(crop.Left + d.ButtonBounds.Left + d.ButtonBounds.Width / 2, crop.Top + d.ButtonBounds.Top + d.ButtonBounds.Height / 2);
                bool clicked = Native.Click(target.Window, crop, center, delegate
                {
                    Rectangle fresh; string why;
                    if (!target.Locate(recognizer, null, out fresh, out why) || fresh != crop) return false;
                    using (Bitmap b = Native.Capture(fresh))
                    {
                        Detection again = recognizer.Classify(b);
                        return again.State == ButtonState.Resume && Math.Abs(again.ButtonBounds.X - d.ButtonBounds.X) <= 2 && Math.Abs(again.ButtonBounds.Y - d.ButtonBounds.Y) <= 2;
                    }
                });
                if (clicked) { gate.MarkAttempt(DateTime.UtcNow); RefreshScores(d); SetStatus("已发送一次鼠标点击，正在检查是否变为方块"); }
                else { gate.BreakStreak(); SetStatus("点击前状态变化，已取消本次点击并重新观察"); }
            }
            catch (Exception e) { activity.Errors++; gate.BreakStreak(); SetStatus("本次捕获或点击暂不可用，持续监测：" + e.Message); }
            finally { updating = false; }
        }

        bool Current(Target connected, int revision)
        { return !IsDisposed && monitoring && background.Checked && target == connected && generation == revision; }

        void CandidateWait()
        {
            stabilityTimer.Stop();
            gate.BreakStreak(); gate.Observe(ButtonState.Unknown, DateTime.UtcNow);
            int count = target == null ? -1 : target.CandidateCount;
            SetStatus(count > 1 ? "发现多个候选，暂停点击但持续扫描；恢复单候选后自动继续，也可点“选择区域”" : "原选区域已变化，持续扫描；原区域恢复后继续，或点“选择区域”重选");
        }
        async Task<WindowFrame> ReadBackground(Target connected, int revision, bool forAction = false)
        {
            activity.Start(DateTime.UtcNow);
            try
            {
                WindowFrame frame = forAction ? await sampler.ReadActionAsync(connected.Window) : await sampler.ReadAsync(connected.Window);
                if (Current(connected, revision)) activity.Background(frame);
                return frame;
            }
            catch (Exception e)
            {
                if (Current(connected, revision))
                {
                    activity.Errors++; string error = e.GetType().Name + ": " + e.Message; DateTime now = DateTime.UtcNow;
                    if (error != lastCaptureError || (now - lastCaptureErrorAt).TotalSeconds >= 10) { lastCaptureError = error; lastCaptureErrorAt = now; journal.Write("diagnostics.log", "capture-error", error, now); }
                }
                throw;
            }
        }
        void HealthTick()
        {
            DateTime now = DateTime.UtcNow;
            string summary = activity.Summary(now, monitoring, updating, background.Checked, target == null ? -1 : target.CandidateCount, gate.Attempts, gate.Verified) + "；当前状态=" + statusSource;
            RefreshUi();
            if (monitoring && progressPulse.Due(now, TimeSpan.FromSeconds(10)))
            {
                bool checkedRecently = activity.LastSample != DateTime.MinValue && (now - activity.LastSample).TotalSeconds < 8;
                RecentProgress((target == null ? "" : updating ? "正在检查 · " : checkedRecently ? "已检查 · " : "等待检查 · ") + statusSource);
            }
            if (shownOnce && preferences.CheckUpdates && DateTime.UtcNow >= nextUpdateCheck) { Task updateCheck = CheckUpdatesAsync(false); }
            if (filePulse.Due(now, TimeSpan.FromSeconds(10)))
            {
                string identity = target == null ? "unbound" : "hwnd=" + target.Window.ToInt64().ToString("X") + "; pid=" + target.ProcessId;
                journal.Write("diagnostics.log", "heartbeat", summary + "; " + identity + "; last-sample-utc=" + activity.LastSample.ToString("O") + "; last-content-utc=" + activity.LastContent.ToString("O") + "; sessions=" + WindowCapture.SessionStarts + "; pool-refresh=" + WindowCapture.PoolRefreshes + "; native-worker-busy=" + sampler.IsBusy, now);
            }
        }

        bool RecordQuestion(bool waiting)
        {
            if (!waiting)
            {
                questionTimer.Stop();
                if (questionReported) { questionReported = false; scores.Text = "问答面板已消失，恢复正常图标检测"; SetStatus("问答面板已消失，恢复正常图标检测"); }
                return false;
            }
            questionReported = true;
            stabilityTimer.Stop();
            activity.LastIcon = "等待用户回答";
            questionTimer.Start();
            gate.BreakStreak(); gate.Observe(ButtonState.Unknown, DateTime.UtcNow);
            nextMonitor = DateTime.UtcNow.AddSeconds(2);
            scores.Text = "等待用户回答\n问答面板已识别，降低检测频率\n不会选择、跳过或提交答案\n已点击：" + gate.Attempts + "    已确认恢复：" + gate.Verified;
            SetStatus("等待用户回答：每 2 秒检查一次，问答面板消失后自动恢复图标检测");
            return true;
        }
        async Task<bool> CheckForegroundQuestion(Target connected, int revision)
        {
            if (Native.GetForegroundWindow() != connected.Window || Native.IsIconic(connected.Window)) return questions.Waiting;
            Native.RECT rect;
            if (!Native.GetWindowRect(connected.Window, out rect) || !SystemInformation.VirtualScreen.Contains(rect.Rectangle)) return false;
            bool waiting;
            using (Bitmap image = Native.Capture(rect.Rectangle)) { waiting = await questions.CheckAsync(image); if (monitoring && target == connected && generation == revision && waiting) UpdateQuestionPreview(image); }
            if (!monitoring || target != connected || generation != revision) return false;
            return RecordQuestion(waiting);
        }

        void BackgroundWait(string reason)
        {
            stabilityTimer.Stop();
            gate.BreakStreak(); gate.ObserveCaptured(ButtonState.Unknown, DateTime.UtcNow, DateTime.MinValue);
            if (reason == "候选区域需要用户确认") CandidateWait();
            else if (reason == "已取消区域选择") SetStatus("继续监测");
            else SetStatus(reason);
        }

        async void TickBackground()
        {
            updating = true;
            Target connected = target; int revision = generation;
            try
            {
                string reason;
                if (!connected.ValidateIdentity(out reason)) { WindowCapture.StopCapture(); BackgroundWait(reason); return; }
                WindowFrame captured;
                try { captured = await ReadBackground(connected, revision); }
                catch (Exception e) { if (Current(connected, revision)) BackgroundWait(e.Message); return; }
                using (WindowFrame frame = captured)
                {
                    if (!Current(connected, revision)) return;
                    bool changed = frame.ContentSequence != lastObservedContent;
                    if (!changed && !regionChoiceRequested && lastObservedState != ButtonState.Resume)
                    {
                        // Silence is normal: retain the last observation without
                        // re-running OCR or promoting cached pixels to new proof.
                        return;
                    }
                    bool questionChecked = questions.Waiting;
                    if (questionChecked)
                    {
                        bool waiting = await questions.CheckAsync(frame.Image);
                        if (!Current(connected, revision)) return;
                        if (waiting) UpdateQuestionPreview(frame.Image);
                        if (RecordQuestion(waiting)) return;
                    }
                    Rectangle crop;
                    if (!connected.LocateBackground(frame, recognizer, regionChoiceRequested ? (Func<Bitmap, System.Collections.Generic.List<LocatedButton>, int>)ChooseRegion : null, out crop, out reason, false))
                    {
                        if (!Current(connected, revision)) return;
                        if (!questionChecked && reason == "后台捕获正常，正在寻找蓝色圆按钮")
                        {
                            bool waiting = await questions.CheckAsync(frame.Image);
                            if (!Current(connected, revision)) return;
                            if (waiting) UpdateQuestionPreview(frame.Image);
                            if (RecordQuestion(waiting)) return;
                        }
                        BackgroundWait(reason); return;
                    }
                    Rectangle local = crop; local.Offset(-frame.Bounds.X, -frame.Bounds.Y);
                    Detection d;
                    using (Bitmap b = frame.Image.Clone(local, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
                    { d = recognizer.Classify(b); UpdatePreview(b, d); }
                    activity.Recognized(d.State); lastObservedContent = frame.ContentSequence; lastObservedState = d.State;
                    Rectangle badge = d.ButtonBounds; badge.Offset(crop.Location);
                    if (!lastBadge.IsEmpty && (Math.Abs(badge.X - lastBadge.X) > 2 || Math.Abs(badge.Y - lastBadge.Y) > 2)) gate.BreakStreak();
                    lastBadge = badge;
                    int previousVerified = gate.Verified;
                    bool ready = gate.ObserveCaptured(d.State, DateTime.UtcNow, frame.ContentAt); RecordVerified(previousVerified);
                    RefreshScores(d);
                    if (gate.AwaitingRunning) { SetStatus("后台操作已投递一次，等待新捕获帧中的白色方块…"); return; }
                    if (!ready)
                    { ObserveStatus(d.State); return; }
                    if (!automatic.Checked) { SetStatus("检测到暂停 · 仅观察"); return; }
                    if (DateTime.UtcNow < nextActionRefresh) return;
                    nextActionRefresh = DateTime.UtcNow.AddSeconds(5);
                    // An action needs an independent acquisition, including when
                    // the triangle has been sitting still for a long time.
                    using (WindowFrame fresh = await ReadBackground(connected, revision, true))
                    {
                        if (!Current(connected, revision) || !automatic.Checked) return;
                        Rectangle freshCrop;
                        if (fresh.ContentSequence <= frame.ContentSequence || !connected.LocateBackground(fresh, recognizer, null, out freshCrop, out reason) || freshCrop != crop)
                        { BackgroundWait("后台点击前位置变化，等待重新定位"); return; }
                        Rectangle freshLocal = freshCrop; freshLocal.Offset(-fresh.Bounds.X, -fresh.Bounds.Y);
                        Detection again;
                        using (Bitmap b = fresh.Image.Clone(freshLocal, System.Drawing.Imaging.PixelFormat.Format32bppRgb)) again = recognizer.Classify(b);
                        if (again.State != ButtonState.Resume || Math.Abs(again.ButtonBounds.X - d.ButtonBounds.X) > 2 || Math.Abs(again.ButtonBounds.Y - d.ButtonBounds.Y) > 2)
                        { BackgroundWait("后台点击前图标变化，已取消本次点击"); return; }
                        Point center = new Point(freshCrop.Left + again.ButtonBounds.Left + again.ButtonBounds.Width / 2, freshCrop.Top + again.ButtonBounds.Top + again.ButtonBounds.Height / 2);
                        bool sent = BackgroundClick.Send(connected.Window, fresh.Bounds, center, delegate
                        { string why; return Current(connected, revision) && automatic.Checked && connected.ValidateIdentity(out why) && fresh.ContentFresh(DateTime.UtcNow) && (DateTime.UtcNow - fresh.CapturedAt).TotalSeconds <= 2; });
                        if (sent) { gate.MarkAttempt(DateTime.UtcNow); RefreshScores(d); SetStatus("后台操作已投递：" + BackgroundClick.LastRoute + "；等待方块确认，尚不计为成功"); }
                        else BackgroundWait("后台点击前窗口状态变化，已取消本次点击");
                    }
                }
            }
            catch (Exception e) { if (Current(connected, revision)) BackgroundWait("暂时无法捕获或点击，继续监测：" + e.Message); }
            finally { updating = false; }
        }
        void UpdatePreview(Bitmap b, Detection d)
        {
            WatchResume(d.State);
            Rectangle bounds = d.ButtonBounds.IsEmpty ? new Rectangle(Point.Empty, b.Size) : Rectangle.Inflate(d.ButtonBounds, 3, 3);
            bounds.Intersect(new Rectangle(Point.Empty, b.Size));
            Image previous = preview.Image; preview.Image = b.Clone(bounds, System.Drawing.Imaging.PixelFormat.Format32bppRgb); if (previous != null) previous.Dispose();
            SetPreviewKind(false);
            RefreshScores(d);
        }
        void RefreshScores(Detection d)
        {
            scores.Text = String.Format("{0} · {1}\n三角形 {2:P0}   方块 {3:P0}   发送 {4:P0}   语音 {5:P0}\n已点击：{6}    已确认恢复：{7}\n{8}",
                (background.Checked ? "后台 · " : "前台 · ") + (target != null && target.Automatic ? "自动定位" : "手动标定"), Portable.StateName(d.State), d.ResumeScore, d.RunningScore, d.SendScore, d.VoiceScore, gate.Attempts, gate.Verified, d.Detail);
        }
        void ShowHelp()
        {
            UiDialog.Show(this, UiText.English ? "Codex Auto Resume " + Portable.Version + "\n\nFinds Codex automatically; starts in observation mode.\nEnable Auto-resume after confirming the target. Pause stops monitoring.\nBackground monitoring works while covered; keep Codex unminimized.\nQuestion panels wait for your answer and are never answered automatically.\nFor multiple buttons, use Select area to confirm the target.\n\nSettings contains diagnostics, demos, logs and updates.\nUpdates ask for confirmation, then download, verify, install and restart.\n\nFallback shortcuts: " + shortcut + "+F8 connects; F7 calibrates;\nF9 pauses/resumes; F10 stops immediately.\nF2 self-check; F4 background test.\n\nLogs: " + logDirectory : "Codex Auto Resume " + Portable.Version + "\n\n自动寻找 Codex，启动时仅观察。\n确认目标后打开“自动恢复”；暂停可停止监测。\n后台监测允许窗口被遮挡，但 Codex 不能最小化。\n需要你回答问题时，工具会等待，不会代答。\n多个按钮时持续扫描，可通过“选择区域”指定目标。\n\n设置中可查看诊断、演示、日志和更新。\n自动更新会先征求确认，再下载、校验、替换并重启。\n\n备用快捷键：" + shortcut + "+F8 连接窗口；\nF7 手动标定；F9 暂停/继续；F10 紧急停止。\nF2 本机自检；F4 后台自测。\n\n日志目录：" + logDirectory, "使用说明", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        void ShowSelfCheck()
        {
            if (diagnosing || installingUpdate) return;
            Stop("正在本机自检，监控已停止");
            try
            {
                string report = Portable.BuildReport();
                SetStatus("本机自检通过：系统接口、窗口控件、内嵌图片和数据目录正常");
                UiDialog.Show(this, "本机自检通过。\n此检查不会发送鼠标点击；自动恢复请用演示窗口验证。\n\n" + report, "本机自检", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception e) { SetStatus("本机自检失败：" + e.Message); UiDialog.Show(this, e.ToString(), "本机自检失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
        async void ShowBackgroundSelfCheck()
        {
            if (diagnosing || installingUpdate) return;
            if (updating || sampler.IsBusy) { Stop("上一次捕获尚未结束；请稍后再点后台自测，若持续等待请重启工具"); return; }
            Stop("正在后台自测；监控已停止，请暂勿移动鼠标或切换窗口");
            diagnosing = true;
            diagnosticCancellation = new System.Threading.CancellationTokenSource();
            try
            {
                string report = await BackgroundDiagnostics.RunAsync(this, diagnosticCancellation.Token, sampler);
                if (IsDisposed) return;
                SetStatus("后台自测通过：完全遮挡下恢复一次，鼠标和前台窗口保持不变");
                UiDialog.Show(this, report.Replace("PASS: fully occluded background diagnostic", "后台自测通过。"), "后台自测通过", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (OperationCanceledException) { if (!IsDisposed) SetStatus("后台自测已取消；监控保持停止"); }
            catch (Exception e)
            {
                if (!IsDisposed) { SetStatus("后台自测未通过：" + e.Message); UiDialog.Show(this, e.Message, "后台自测未通过", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
            finally { diagnosticCancellation.Dispose(); diagnosticCancellation = null; diagnosing = false; }
        }
        void ShowLicense()
        {
            using (Form dialog = new Form { Text = "CodexAutoResume · MIT License", ClientSize = new Size(780, 460), StartPosition = FormStartPosition.CenterParent, Icon = DesktopIdentity.LoadIcon(), ShowInTaskbar = false })
            {
                TextBox document = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Text = DesktopIdentity.LicenseText().Replace("\r\n", "\n").Replace("\n", Environment.NewLine), Font = new Font("Consolas", 10) };
                dialog.Controls.Add(document);
                dialog.Shown += delegate { document.Focus(); document.Select(0, 0); };
                UiText.Apply(dialog); dialog.ShowDialog(this);
            }
        }
        void SetStatus(string text)
        {
            string friendly = FriendlyStatus(text);
            bool changed = statusSource != friendly;
            statusSource = friendly; UiText.Set(status, friendly);
            regionAction.Visible = text.Contains("多个候选") || text.Contains("原选区域") || text.Contains("候选区域需要用户确认");
            RefreshUi();
            if (text == lastStatus) return; lastStatus = text;
            if (changed && Significant(friendly)) AppendVisible(friendly, "state");
            journal.Write("diagnostics.log", "state", text, DateTime.UtcNow);
        }
        void AppendVisible(string text, string category)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine;
            if (logBox.TextLength > 24000) logBox.Clear(); logBox.AppendText(line);
            RecentProgress(text);
            if (activityView != null && !activityView.IsDisposed) { activityView.Text = UiText.Get(logBox.Text); activityView.SelectionStart = activityView.TextLength; activityView.ScrollToCaret(); }
            journal.Write("events.log", category, text, DateTime.UtcNow);
        }
        void SaveDiagnostic()
        {
            if (questionPreview) { SetStatus("问答区域仅供查看；请在按钮状态下保存按钮图"); return; }
            if (preview.Image == null) { SetStatus("尚无局部取样图，请先标定。"); return; }
            try
            {
                Directory.CreateDirectory(logDirectory);
                string path = Path.Combine(logDirectory, "button-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".png");
                preview.Image.Save(path, System.Drawing.Imaging.ImageFormat.Png); SetStatus("已保存上一帧局部图：" + path);
            }
            catch (Exception e) { SetStatus("保存失败：" + e.Message); }
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            monitoring = false; generation++;
            if (diagnosticCancellation != null) diagnosticCancellation.Cancel();
            lifetime.Cancel(); WindowCapture.FrameAvailable -= OnCaptureFrame;
            stabilityTimer.Stop(); stabilityTimer.Dispose();
            questionTimer.Stop(); questionTimer.Dispose();
            frameTimer.Stop(); frameTimer.Dispose();
            UiText.Changed -= RefreshLanguage; SavePreferences(); tips.Dispose();
            WindowCapture.StopCapture();
            timer.Stop(); timer.Dispose();
            healthTimer.Stop(); healthTimer.Dispose();
            journal.Write("diagnostics.log", "shutdown", "monitoring=" + monitoring + "; reads=" + activity.Reads + "; completed=" + activity.Completed, DateTime.UtcNow);
            for (int i = 1; i <= 4; i++) Native.UnregisterHotKey(Handle, i);
            if (preview.Image != null) preview.Image.Dispose(); preview.Dispose(); scores.Dispose(); logBox.Dispose(); background.Dispose(); cropSize.Dispose();
            if (demo != null) demo.Dispose(); base.OnFormClosed(e);
        }
    }

    internal sealed class DemoForm : Form
    {
        public const string WindowTitle = "CodexAutoResume Demo";
        readonly IconButton icon = new IconButton();
        readonly Label count = new Label { Dock = DockStyle.Top, Height = 36, Text = "按钮收到的点击：0" };
        int clicks;
        protected override void OnHandleCreated(EventArgs e)
        { base.OnHandleCreated(e); DesktopIdentity.ApplyToWindow(Handle); }
        protected override void DestroyHandle()
        { try { if (IsHandleCreated) DesktopIdentity.ClearWindow(Handle); } finally { base.DestroyHandle(); } }
        public DemoForm(string shortcut)
        {
            Text = WindowTitle; ClientSize = new Size(760, 420); BackColor = Color.FromArgb(24, 24, 24); ForeColor = Color.White;
            Icon = DesktopIdentity.LoadIcon(); ShowIcon = true;
            MinimumSize = new Size(660, 400);
            Font = new Font("Microsoft YaHei UI", 10);
            FlowLayoutPanel choices = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44 };
            foreach (string name in new string[] { "三角形", "方块", "向上箭头", "语音", "未知符号" })
            {
                string state = name; Button b = new Button { Text = name, AutoSize = true, ForeColor = Color.Black, BackColor = Color.FromArgb(235, 235, 235), UseVisualStyleBackColor = true };
                b.Click += delegate { icon.State = state; icon.Invalidate(); }; choices.Controls.Add(b);
            }
            IconButton second = new IconButton { Location = new Point(150, 320), Size = new Size(64, 64), Visible = false, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
            CheckBox multiple = new CheckBox { Text = "双区域", AutoSize = true, ForeColor = Color.White };
            multiple.CheckedChanged += delegate { second.Visible = multiple.Checked; };
            second.Click += delegate { clicks++; UiText.Set(count, "按钮收到的点击：" + clicks); second.State = "方块"; second.Invalidate(); };
            choices.Controls.Add(multiple);
            Button move = new Button { Text = "换位置", AutoSize = true, ForeColor = Color.Black, BackColor = Color.WhiteSmoke };
            move.Click += delegate { icon.Left = icon.Left > ClientSize.Width / 2 ? ClientSize.Width / 2 - 80 : ClientSize.Width - 130; };
            choices.Controls.Add(move);
            Label guide = new Label { Dock = DockStyle.Top, Height = 90, Text = "演示已自动连接，无需快捷键。\n在主窗口勾选允许自动点击即可开始。\n后台模式可切到其他应用，完全盖住此窗口。\n三角形恢复后变为方块，计数只增加一次。" };
            icon.Location = new Point(630, 320); icon.Size = new Size(64, 64); icon.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            icon.Click += delegate { clicks++; UiText.Set(count, "按钮收到的点击：" + clicks); icon.State = "方块"; icon.Invalidate(); };
            Controls.Add(second); Controls.Add(icon); Controls.Add(count); Controls.Add(guide); Controls.Add(choices);
            Shown += delegate { UiText.Apply(this); };
        }
    }

    internal sealed class IconButton : Button
    {
        public string State = "三角形";
        public IconButton() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); Cursor = Cursors.Hand; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; BackColor = Color.FromArgb(24, 24, 24); AccessibleName = "Resume demo"; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor); Draw(e.Graphics, ClientRectangle, State);
        }
        public static void Draw(Graphics g, Rectangle r, string state)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f, scale = r.Width / 64f;
            using (Brush blue = new SolidBrush(Color.FromArgb(43, 103, 203))) g.FillEllipse(blue, cx - 18 * scale, cy - 18 * scale, 36 * scale, 36 * scale);
            if (state == "三角形") g.FillPolygon(Brushes.White, new PointF[] { new PointF(cx - 4 * scale, cy - 6 * scale), new PointF(cx + 6 * scale, cy), new PointF(cx - 4 * scale, cy + 6 * scale) });
            else if (state == "方块") g.FillRectangle(Brushes.White, cx - 5 * scale, cy - 5 * scale, 10 * scale, 10 * scale);
            else if (state == "向上箭头")
            {
                using (Pen pen = new Pen(Color.White, 1.5f * scale))
                { g.DrawLines(pen, new PointF[] { new PointF(cx - 6 * scale, cy), new PointF(cx, cy - 6 * scale), new PointF(cx + 6 * scale, cy) }); g.DrawLine(pen, cx, cy - 6 * scale, cx, cy + 7 * scale); }
            }
            else if (state == "语音")
            {
                using (Pen pen = new Pen(Color.White, 1.5f * scale))
                {
                    pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                    float[] heights = { 3, 11, 6, 9, 3 };
                    for (int i = 0; i < heights.Length; i++)
                    { float x = cx + (i - 2) * 3.4f * scale; float h = heights[i] * scale / 2; g.DrawLine(pen, x, cy - h, x, cy + h); }
                }
            }
            else using (Pen pen = new Pen(Color.White, 2 * scale)) g.DrawEllipse(pen, cx - 6 * scale, cy - 6 * scale, 12 * scale, 12 * scale);
        }
    }
}

