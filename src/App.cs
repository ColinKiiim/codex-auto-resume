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
            bool created;
            using (System.Threading.Mutex mutex = new System.Threading.Mutex(true, "Local\\CodexAutoResume-v1", out created))
            {
                if (!created) { MessageBox.Show("CodexAutoResume 已在运行，请使用现有窗口。", "CodexAutoResume"); return 0; }
                Application.Run(new MainForm());
            }
            return 0;
        }
    }

    internal sealed class MainForm : Form
    {
        readonly Recognizer recognizer = new Recognizer();
        readonly CaptureSampler sampler = new CaptureSampler(WindowCapture.Snapshot);
        readonly QuestionDetector questions = new QuestionDetector();
        DateTime nextMonitor;
        bool questionReported;
        ResumeGate gate = new ResumeGate();
        Target target;
        readonly Timer timer = new Timer();
        readonly CheckBox automatic = new CheckBox { Text = "允许自动点击（勾选即开始）", AutoSize = true };
        readonly CheckBox background = new CheckBox { Text = "后台模式（实验）：允许完全遮挡，保持窗口未最小化", AutoSize = true };
        readonly NumericUpDown cropSize = new NumericUpDown { Minimum = 36, Maximum = 160, Value = 64, Increment = 4, Width = 70 };
        readonly PictureBox preview = new PictureBox { Width = 128, Height = 128, BackColor = Color.FromArgb(35, 35, 35), SizeMode = PictureBoxSizeMode.Zoom };
        readonly Label status = new Label { AutoSize = false, Height = 54, Dock = DockStyle.Fill, Text = "自动查找 Codex；勾选允许自动点击即可开始。" };
        readonly Label instructions = new Label { Dock = DockStyle.Fill };
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
        string shortcut = "Ctrl+Alt";
        Rectangle lastBadge;

        public MainForm()
        {
            Text = "CodexAutoResume " + Portable.Version + " · 后台模式（实验） · 单文件便携版";
            ShowInTaskbar = true; ShowIcon = true; Icon = DesktopIdentity.LoadIcon();
            Font = new Font("Microsoft YaHei UI", 10);
            ClientSize = new Size(760, 720); MinimumSize = new Size(720, 720);
            StartPosition = FormStartPosition.CenterScreen;
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 7 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 140));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(new Label { Text = "Codex Auto Resume", Font = new Font(Font.FontFamily, 20, FontStyle.Bold), AutoSize = true }, 0, 0);
            layout.Controls.Add(instructions, 0, 1);
            FlowLayoutPanel controls = new FlowLayoutPanel { Dock = DockStyle.Fill };
            controls.Controls.Add(toggle);
            Button stop = new Button { Text = "停止", AutoSize = true };
            Button showDemo = new Button { Text = "打开演示", AutoSize = true };
            Button save = new Button { Text = "保存局部诊断图", AutoSize = true };
            Button help = new Button { Text = "使用说明", AutoSize = true };
            Button check = new Button { Text = "本机自检", AutoSize = true };
            Button backgroundCheck = new Button { Text = "后台自测 (F4)", AutoSize = true };
            Button chooseWindow = new Button { Text = "选择窗口", AutoSize = true };
            controls.Controls.Add(stop); controls.Controls.Add(showDemo); controls.Controls.Add(save);
            controls.Controls.Add(help);
            controls.Controls.Add(check);
            controls.Controls.Add(backgroundCheck);
            controls.Controls.Add(chooseWindow);
            layout.Controls.Add(controls, 0, 2);
            FlowLayoutPanel options = new FlowLayoutPanel { Dock = DockStyle.Fill };
            options.Controls.Add(automatic); options.Controls.Add(new Label { Text = "手动取样(px)", AutoSize = true, Padding = new Padding(12, 4, 0, 0) }); options.Controls.Add(cropSize);
            options.SetFlowBreak(cropSize, true); options.Controls.Add(background);
            layout.Controls.Add(options, 0, 3);
            FlowLayoutPanel images = new FlowLayoutPanel { Dock = DockStyle.Fill };
            images.Controls.Add(preview); images.Controls.Add(scores); layout.Controls.Add(images, 0, 4);
            layout.Controls.Add(status, 0, 5); layout.Controls.Add(logBox, 0, 6); Controls.Add(layout);
            toggle.Click += delegate { Toggle(); };
            stop.Click += delegate { Stop("已手动停止"); };
            showDemo.Click += delegate { if (diagnosing) return; if (demo == null || demo.IsDisposed) demo = new DemoForm(shortcut); demo.Show(); demo.Activate(); Connect(Target.FromWindow(demo.Handle, true)); };
            save.Click += delegate { SaveDiagnostic(); };
            help.Click += delegate { ShowHelp(); };
            check.Click += delegate { ShowSelfCheck(); };
            backgroundCheck.Click += delegate { ShowBackgroundSelfCheck(); };
            chooseWindow.Click += delegate { if (!diagnosing) { generation++; target = null; waitingForChoice = false; monitoring = true; nextDiscovery = DateTime.MinValue; Discover(); } };
            KeyPreview = true;
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.F1) { ShowHelp(); e.Handled = true; } else if (e.KeyCode == Keys.F2) { ShowSelfCheck(); e.Handled = true; } else if (e.KeyCode == Keys.F3) { ShowLicense(); e.Handled = true; } else if (e.KeyCode == Keys.F4) { ShowBackgroundSelfCheck(); e.Handled = true; } };
            automatic.CheckedChanged += delegate { ActivateOptions(automatic.Checked ? "自动点击已开启：持续检测三角形，点击间隔至少 5 秒" : "仅观察：持续捕获，不会点击"); };
            background.CheckedChanged += delegate { if (!background.Checked) WindowCapture.StopCapture(); ActivateOptions(background.Checked ? "后台持续捕获已开启，允许完全遮挡，不需要快捷键" : "前台模式已开启：等待目标窗口在前台可见"); };
            cropSize.ValueChanged += delegate { if (target != null && !target.Automatic) { generation++; target.CropSize = (int)cropSize.Value; gate.BreakStreak(); } };
            timer.Interval = 400; timer.Tick += delegate { TickMonitor(); }; timer.Start();
            Shown += delegate
            {
                bool registered = false;
                uint[] modifiers = { 0x4003, 0x4007, 0x4006 };
                string[] names = { "Ctrl+Alt", "Ctrl+Alt+Shift", "Ctrl+Shift" };
                for (int i = 0; i < modifiers.Length; i++)
                {
                    bool a = Native.RegisterHotKey(Handle, 1, modifiers[i], (uint)Keys.F8);
                    bool b = Native.RegisterHotKey(Handle, 2, modifiers[i], (uint)Keys.F9);
                    bool c = Native.RegisterHotKey(Handle, 3, modifiers[i], (uint)Keys.F10);
                    bool d = Native.RegisterHotKey(Handle, 4, modifiers[i], (uint)Keys.F7);
                    if (a && b && c && d) { shortcut = names[i]; registered = true; break; }
                    for (int j = 1; j <= 4; j++) Native.UnregisterHotKey(Handle, j);
                }
                instructions.Text = "1. 自动寻找 Codex 并持续观察；勾选“允许自动点击”即可开始，无需快捷键。\n2. 后台模式允许完全遮挡；多窗口或多个按钮区域须手动选择确认。\n3. 暂时未知、拖动窗口或未确认方块均继续捕获；只在你手动停止后暂停。\n备用：" + shortcut + "+F8 连接前台窗口；F7 手动标定；F9 开始/暂停；F10 停止。";
                monitoring = true; toggle.Text = "暂停监测";
                SetStatus(registered ? "正在自动寻找 Codex；勾选自动点击即可开始，快捷键可选" : "快捷键被占用；按钮和自动检测仍可使用");
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
                gate = new ResumeGate(); lastBadge = Rectangle.Empty;
                SetStatus("备用手动标定成功；正在观察。按 " + shortcut + "+F8 可切回自动定位。");
                Rectangle r; string reason;
                if (target.Locate(recognizer, null, out r, out reason)) using (Bitmap b = Native.Capture(r)) UpdatePreview(b, recognizer.Classify(b));
                monitoring = true; toggle.Text = "停止观察";
            }
            catch (Exception e) { target = null; SetStatus("标定失败：" + e.Message); }
        }
        void AutoStart()
        {
            Stop("正在连接前台 Codex 窗口");
            try
            {
                target = Target.AttachForeground(demo != null && !demo.IsDisposed);
                gate = new ResumeGate(); lastBadge = Rectangle.Empty;
                monitoring = true; toggle.Text = "停止观察";
                SetStatus("自动定位已启用，正在寻找按钮；窗口移动或调整大小后会重新定位");
            }
            catch (Exception e) { target = null; SetStatus(e.Message); }
        }
        void Connect(Target connected)
        {
            questions.Reset(); questionReported = false; nextMonitor = DateTime.MinValue;
            generation++; target = connected; gate = new ResumeGate(); lastBadge = Rectangle.Empty;
            waitingForChoice = false; monitoring = true; toggle.Text = "暂停监测";
            SetStatus(automatic.Checked ? "已连接窗口，自动点击中；暂时捕获不到也会持续监测" : "已连接窗口，持续观察；勾选允许自动点击即可生效");
        }
        void ActivateOptions(string message)
        {
            if (diagnosing) return;
            generation++; gate.BreakStreak(); monitoring = true; toggle.Text = "暂停监测";
            questions.Reset(); nextMonitor = DateTime.MinValue;
            waitingForChoice = false; nextDiscovery = DateTime.MinValue;
            SetStatus(message);
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
            int revision = generation;
            using (RegionPicker picker = new RegionPicker(scan, candidates))
            { DialogResult result = picker.ShowDialog(this); return result == DialogResult.OK && revision == generation && monitoring ? picker.SelectedIndex : -1; }
        }
        void Toggle()
        {
            if (diagnosing) return;
            if (monitoring) { Stop("已停止观察"); return; }
            gate.BreakStreak(); monitoring = true; waitingForChoice = false; nextDiscovery = DateTime.MinValue; toggle.Text = "暂停监测";
            questions.Reset(); nextMonitor = DateTime.MinValue;
            SetStatus(automatic.Checked ? "监控中：允许自动点击" : "监控中：仅观察，不会点击");
        }
        void Stop(string message)
        {
            if (diagnosticCancellation != null) diagnosticCancellation.Cancel();
            WindowCapture.StopCapture();
            generation++; monitoring = false; gate.BreakStreak(); toggle.Text = "开始观察"; SetStatus(message);
            questions.Reset();
        }
        async void TickMonitor()
        {
            if (!monitoring || updating || diagnosing || DateTime.UtcNow < nextMonitor) return;
            if (target == null)
            { updating = true; try { Discover(); } catch (Exception e) { SetStatus("窗口检测暂不可用，继续等待：" + e.Message); } finally { updating = false; } return; }
            if (!Native.IsWindow(target.Window)) { generation++; target = null; waitingForChoice = false; nextDiscovery = DateTime.MinValue; WindowCapture.StopCapture(); return; }
            if (background.Checked) { TickBackground(); return; }
            updating = true;
            Target connected = target; int revision = generation;
            try
            {
                if (questions.Waiting && await CheckForegroundQuestion(connected, revision)) return;
                if (!monitoring || target != connected || generation != revision) return;
                Rectangle crop; string reason;
                if (!target.Locate(recognizer, ChooseRegion, out crop, out reason))
                {
                    if (!monitoring || target != connected || generation != revision) return;
                    gate.BreakStreak();
                    if (reason == "已取消区域选择") { waitingForChoice = true; target = null; SetStatus("区域选择已取消；点击“选择窗口”重新选择，等待期间不会点击"); return; }
                    gate.Observe(ButtonState.Unknown, DateTime.UtcNow);
                    if (reason == "正在自动寻找蓝色圆按钮；找不到时可用手动标定备用" && await CheckForegroundQuestion(connected, revision)) return;
                    if (!monitoring || target != connected || generation != revision) return;
                    SetStatus(reason);
                    return;
                }
                Detection d;
                using (Bitmap b = Native.Capture(crop)) { d = recognizer.Classify(b); UpdatePreview(b, d); }
                Rectangle badgeScreen = d.ButtonBounds; badgeScreen.Offset(crop.Location);
                if (!d.ButtonBounds.IsEmpty && !lastBadge.IsEmpty &&
                    (Math.Abs(badgeScreen.X - lastBadge.X) > 2 || Math.Abs(badgeScreen.Y - lastBadge.Y) > 2)) gate.BreakStreak();
                lastBadge = badgeScreen;
                bool ready = gate.Observe(d.State, DateTime.UtcNow);
                RefreshScores(d);
                if (gate.AwaitingRunning) { SetStatus("已点击一次，等待白色方块确认运行中…"); return; }
                if (!ready)
                {
                    SetStatus(d.State == ButtonState.Running ? "识别到白色方块：运行中，保持观察" : d.State == ButtonState.Resume ? "识别到向右三角形，等待连续稳定帧…" : d.State == ButtonState.Send ? "识别到向上箭头：发送模式，保持观察，不点击" : d.State == ButtonState.Voice ? "识别到声波图标：语音模式，保持观察，不点击" : "当前图形未知，保持观察，不点击");
                    return;
                }
                if (!automatic.Checked) { SetStatus("仅观察：已确认向右三角形，可以恢复（未点击）"); return; }
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
            catch (Exception e) { gate.BreakStreak(); SetStatus("本次捕获或点击暂不可用，持续监测：" + e.Message); }
            finally { updating = false; }
        }

        bool Current(Target connected, int revision)
        { return !IsDisposed && monitoring && background.Checked && target == connected && generation == revision; }

        bool RecordQuestion(bool waiting)
        {
            if (!waiting)
            {
                if (questionReported) { questionReported = false; scores.Text = "问答面板已消失，恢复正常图标检测"; SetStatus("问答面板已消失，恢复正常图标检测"); }
                return false;
            }
            questionReported = true;
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
            using (Bitmap image = Native.Capture(rect.Rectangle)) waiting = await questions.CheckAsync(image);
            if (!monitoring || target != connected || generation != revision) return false;
            return RecordQuestion(waiting);
        }

        void BackgroundWait(string reason)
        {
            gate.BreakStreak(); gate.Observe(ButtonState.Unknown, DateTime.UtcNow);
            if (reason == "已取消区域选择") { waitingForChoice = true; target = null; SetStatus("区域选择已取消；点击“选择窗口”重新选择，确认前不会点击"); }
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
                try { captured = await sampler.ReadAsync(connected.Window); }
                catch (Exception e) { if (Current(connected, revision)) BackgroundWait(e.Message); return; }
                using (WindowFrame frame = captured)
                {
                    if (!Current(connected, revision)) return;
                    bool questionChecked = questions.Waiting;
                    if (questionChecked)
                    {
                        bool waiting = await questions.CheckAsync(frame.Image);
                        if (!Current(connected, revision)) return;
                        if (RecordQuestion(waiting)) return;
                    }
                    Rectangle crop;
                    if (!connected.LocateBackground(frame, recognizer, ChooseRegion, out crop, out reason))
                    {
                        if (!Current(connected, revision)) return;
                        if (!questionChecked && reason == "后台捕获正常，正在寻找蓝色圆按钮")
                        {
                            bool waiting = await questions.CheckAsync(frame.Image);
                            if (!Current(connected, revision)) return;
                            if (RecordQuestion(waiting)) return;
                        }
                        BackgroundWait(reason); return;
                    }
                    Rectangle local = crop; local.Offset(-frame.Bounds.X, -frame.Bounds.Y);
                    Detection d;
                    using (Bitmap b = frame.Image.Clone(local, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
                    { d = recognizer.Classify(b); UpdatePreview(b, d); }
                    Rectangle badge = d.ButtonBounds; badge.Offset(crop.Location);
                    if (!lastBadge.IsEmpty && (Math.Abs(badge.X - lastBadge.X) > 2 || Math.Abs(badge.Y - lastBadge.Y) > 2)) gate.BreakStreak();
                    lastBadge = badge;
                    bool ready = gate.Observe(d.State, DateTime.UtcNow);
                    RefreshScores(d);
                    if (gate.AwaitingRunning) { SetStatus("后台操作已投递一次，等待新捕获帧中的白色方块…"); return; }
                    if (!ready)
                    { SetStatus("后台捕获：" + Portable.StateName(d.State) + (d.State == ButtonState.Resume ? "，等待连续稳定取样" : "，保持观察")); return; }
                    if (!automatic.Checked) { SetStatus("后台仅观察：已确认三角形，可勾选允许自动点击进行测试"); return; }
                    using (WindowFrame fresh = await sampler.ReadAsync(connected.Window))
                    {
                        if (!Current(connected, revision) || !automatic.Checked) return;
                        Rectangle freshCrop;
                        if (fresh.Sequence <= frame.Sequence || !connected.LocateBackground(fresh, recognizer, null, out freshCrop, out reason) || freshCrop != crop)
                        { BackgroundWait("后台点击前位置变化，等待重新定位"); return; }
                        Rectangle freshLocal = freshCrop; freshLocal.Offset(-fresh.Bounds.X, -fresh.Bounds.Y);
                        Detection again;
                        using (Bitmap b = fresh.Image.Clone(freshLocal, System.Drawing.Imaging.PixelFormat.Format32bppRgb)) again = recognizer.Classify(b);
                        if (again.State != ButtonState.Resume || Math.Abs(again.ButtonBounds.X - d.ButtonBounds.X) > 2 || Math.Abs(again.ButtonBounds.Y - d.ButtonBounds.Y) > 2)
                        { BackgroundWait("后台点击前图标变化，已取消本次点击"); return; }
                        Point center = new Point(freshCrop.Left + again.ButtonBounds.Left + again.ButtonBounds.Width / 2, freshCrop.Top + again.ButtonBounds.Top + again.ButtonBounds.Height / 2);
                        bool sent = BackgroundClick.Send(connected.Window, fresh.Bounds, center, delegate
                        { string why; return Current(connected, revision) && automatic.Checked && connected.ValidateIdentity(out why) && (DateTime.UtcNow - fresh.CapturedAt).TotalSeconds <= 2; });
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
            Image previous = preview.Image; preview.Image = new Bitmap(b); if (previous != null) previous.Dispose();
            RefreshScores(d);
        }
        void RefreshScores(Detection d)
        {
            scores.Text = String.Format("{0} · {1}\n三角形 {2:P0}   方块 {3:P0}   发送 {4:P0}   语音 {5:P0}\n已点击：{6}    已确认恢复：{7}\n{8}",
                (background.Checked ? "后台 · " : "前台 · ") + (target != null && target.Automatic ? "自动定位" : "手动标定"), Portable.StateName(d.State), d.ResumeScore, d.RunningScore, d.SendScore, d.VoiceScore, gate.Attempts, gate.Verified, d.Detail);
        }
        void ShowHelp()
        {
            MessageBox.Show(this, "CodexAutoResume " + Portable.Version + " · 单文件便携版\n\n只复制这一个 EXE 到其他 Windows 10/11 x64 电脑即可使用。\n识别样本、图标、MIT 许可和说明均已内置，无需安装开发工具。\n依赖系统 .NET Framework 4.x；后台功能需要 Windows 10 1903+。\n\n启动后自动寻找 Codex 并观察；勾选允许自动点击即可生效。\n后台模式同样无需快捷键；多个窗口或区域须人工确认。\n“选择窗口”可重新连接；移动窗口后自动重新定位。\n备用：" + shortcut + "+F8 连接前台窗口；F7 鼠标手动标定；\nF9 开始/暂停；F10 停止。快捷键可选。\n\n后台模式（实验）允许完全遮挡，目标须保持未最小化。\n使用持续系统捕获会话，减少反复启动造成的黄色边框闪烁；\n系统仍可能保留黄色指示边框，不保证无边框。\n不主动激活目标或移动鼠标；兼容性取决于目标版本和权限。\n临时捕获失败、拖动、未知或未确认方块均持续等待。\n四次稳定三角形取样后点击，点击前复查，至少间隔5秒。\n收到严格方块才计为确认；超时后持续观察并限速重试。\n方块、发送、语音、问答面板不会被点击。\n\n等待用户回答：识别问答状态，约每2秒检查一次；\n兼容跳过和提交两种面板，不选择、不跳过、不提交。\n面板消失自动恢复检测；依赖本机 Windows OCR 语言。\n缺少语言时仅影响问答识别，仍持续图标监测且不猜测点击。\n扫描图和问答文字仅在内存处理，不保存对话。\n\n日志及主动保存的按钮局部图：\n" + logDirectory + "\nF2 本机自检；F3 MIT 许可；F4 完全遮挡后台自测。\n自测会暂停监控，请暂勿操作鼠标；完成后点开始观察。", "使用说明", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        void ShowSelfCheck()
        {
            if (diagnosing) return;
            Stop("正在本机自检，监控已停止");
            try
            {
                string report = Portable.BuildReport();
                SetStatus("本机自检通过：系统接口、窗口控件、内嵌图片和数据目录正常");
                MessageBox.Show(this, "本机自检通过。\n此检查不会发送鼠标点击；自动恢复请用演示窗口验证。\n\n" + report, "本机自检", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception e) { SetStatus("本机自检失败：" + e.Message); MessageBox.Show(this, e.ToString(), "本机自检失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
        async void ShowBackgroundSelfCheck()
        {
            if (diagnosing) return;
            if (updating || sampler.IsBusy) { Stop("上一次捕获尚未结束；请稍后再点后台自测，若持续等待请重启工具"); return; }
            Stop("正在后台自测；监控已停止，请暂勿移动鼠标或切换窗口");
            diagnosing = true;
            diagnosticCancellation = new System.Threading.CancellationTokenSource();
            try
            {
                string report = await BackgroundDiagnostics.RunAsync(this, diagnosticCancellation.Token, sampler);
                if (IsDisposed) return;
                SetStatus("后台自测通过：完全遮挡下恢复一次，鼠标和前台窗口保持不变");
                MessageBox.Show(this, report.Replace("PASS: fully occluded background diagnostic", "后台自测通过。"), "后台自测通过", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (OperationCanceledException) { if (!IsDisposed) SetStatus("后台自测已取消；监控保持停止"); }
            catch (Exception e)
            {
                if (!IsDisposed) { SetStatus("后台自测未通过：" + e.Message); MessageBox.Show(this, e.Message, "后台自测未通过", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
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
                dialog.ShowDialog(this);
            }
        }
        void SetStatus(string text)
        {
            status.Text = text;
            if (text == lastStatus) return; lastStatus = text;
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine;
            if (logBox.TextLength > 24000) logBox.Clear(); logBox.AppendText(line);
            try
            {
                Directory.CreateDirectory(logDirectory);
                string file = Path.Combine(logDirectory, "events.log");
                if (File.Exists(file) && new FileInfo(file).Length > 1024 * 1024) File.WriteAllText(file, "");
                File.AppendAllText(file, line);
            }
            catch { /* A read-only launch directory must not prevent monitoring. */ }
        }
        void SaveDiagnostic()
        {
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
            if (diagnosticCancellation != null) diagnosticCancellation.Cancel();
            WindowCapture.StopCapture();
            timer.Stop(); timer.Dispose();
            for (int i = 1; i <= 4; i++) Native.UnregisterHotKey(Handle, i);
            if (preview.Image != null) preview.Image.Dispose(); if (demo != null) demo.Dispose(); base.OnFormClosed(e);
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
            second.Click += delegate { clicks++; count.Text = "按钮收到的点击：" + clicks; second.State = "方块"; second.Invalidate(); };
            choices.Controls.Add(multiple);
            Button move = new Button { Text = "换位置", AutoSize = true, ForeColor = Color.Black, BackColor = Color.WhiteSmoke };
            move.Click += delegate { icon.Left = icon.Left > ClientSize.Width / 2 ? ClientSize.Width / 2 - 80 : ClientSize.Width - 130; };
            choices.Controls.Add(move);
            Label guide = new Label { Dock = DockStyle.Top, Height = 90, Text = "演示已自动连接，无需快捷键。\n在主窗口勾选允许自动点击即可开始。\n后台模式可切到其他应用，完全盖住此窗口。\n三角形恢复后变为方块，计数只增加一次。" };
            icon.Location = new Point(630, 320); icon.Size = new Size(64, 64); icon.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            icon.Click += delegate { clicks++; count.Text = "按钮收到的点击：" + clicks; icon.State = "方块"; icon.Invalidate(); };
            Controls.Add(second); Controls.Add(icon); Controls.Add(count); Controls.Add(guide); Controls.Add(choices);
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

