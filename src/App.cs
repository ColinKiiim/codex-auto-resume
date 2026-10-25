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
        ResumeGate gate = new ResumeGate();
        Target target;
        readonly Timer timer = new Timer();
        readonly CheckBox automatic = new CheckBox { Text = "允许自动点击（默认仅观察）", AutoSize = true };
        readonly CheckBox background = new CheckBox { Text = "后台模式（实验）：允许完全遮挡，保持窗口未最小化", AutoSize = true };
        readonly NumericUpDown cropSize = new NumericUpDown { Minimum = 36, Maximum = 160, Value = 64, Increment = 4, Width = 70 };
        readonly PictureBox preview = new PictureBox { Width = 128, Height = 128, BackColor = Color.FromArgb(35, 35, 35), SizeMode = PictureBoxSizeMode.Zoom };
        readonly Label status = new Label { AutoSize = false, Height = 54, Dock = DockStyle.Fill, Text = "切到 Codex，按自动定位快捷键即可开始。" };
        readonly Label instructions = new Label { Dock = DockStyle.Fill };
        readonly Label scores = new Label { AutoSize = false, Width = 430, Height = 120 };
        readonly TextBox logBox = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
        readonly Button toggle = new Button { Text = "开始观察", AutoSize = true };
        readonly string logDirectory = Path.Combine(Portable.DataDirectory, "logs");
        DemoForm demo;
        bool monitoring, updating;
        int generation;
        string lastStatus = "";
        string shortcut = "Ctrl+Alt";
        Rectangle lastBadge;

        public MainForm()
        {
            Text = "CodexAutoResume 1.1.1 · 后台模式（实验） · 单文件便携版";
            ShowInTaskbar = true; ShowIcon = true; Icon = DesktopIdentity.LoadIcon();
            Font = new Font("Microsoft YaHei UI", 10);
            ClientSize = new Size(760, 680); MinimumSize = new Size(720, 680);
            StartPosition = FormStartPosition.CenterScreen;
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 7 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
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
            controls.Controls.Add(stop); controls.Controls.Add(showDemo); controls.Controls.Add(save);
            controls.Controls.Add(help);
            controls.Controls.Add(check);
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
            showDemo.Click += delegate { if (demo == null || demo.IsDisposed) demo = new DemoForm(shortcut); demo.Show(); demo.Activate(); };
            save.Click += delegate { SaveDiagnostic(); };
            help.Click += delegate { ShowHelp(); };
            check.Click += delegate { ShowSelfCheck(); };
            KeyPreview = true;
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.F1) { ShowHelp(); e.Handled = true; } else if (e.KeyCode == Keys.F2) { ShowSelfCheck(); e.Handled = true; } else if (e.KeyCode == Keys.F3) { ShowLicense(); e.Handled = true; } };
            automatic.CheckedChanged += delegate { generation++; gate.BreakStreak(); SetStatus(automatic.Checked ? "自动点击已允许；后台模式可在被遮挡时尝试恢复" : "仅观察模式：不会点击"); };
            background.CheckedChanged += delegate { target = null; Stop(background.Checked ? "后台模式已开启（实验）；先将 Codex 置于前台按 F8 组合键连接，再切到其他窗口" : "已切回前台模式，请按 F8 组合键重新连接"); };
            cropSize.ValueChanged += delegate { if (target != null && !target.Automatic) { target = null; Stop("手动取样尺寸已改变，请重新标定"); } };
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
                instructions.Text = "1. 选择前台 / 后台模式；切到 Codex，按 " + shortcut + "+F8 连接并观察。\n2. 核对识别后勾选“允许自动点击”；后台模式允许切到其他应用，完全遮挡也可尝试。\n3. 多候选须选择确认；自动定位跟随窗口移动。" + shortcut + "+F9 开始/停止。\n备用：鼠标指向按钮后按 " + shortcut + "+F7 标定。紧急停止：" + shortcut + "+F10。";
                if (!registered) { toggle.Enabled = false; SetStatus("三组快捷键都被占用，请关闭冲突程序后重新打开本工具。"); }
                else SetStatus("就绪：" + shortcut + "+F8 自动定位；F7 手动标定；F9 开始/停止；F10 停止");
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
        int ChooseRegion(Bitmap scan, System.Collections.Generic.List<LocatedButton> candidates)
        {
            gate.BreakStreak();
            using (RegionPicker picker = new RegionPicker(scan, candidates))
                return picker.ShowDialog(this) == DialogResult.OK ? picker.SelectedIndex : -1;
        }
        void Toggle()
        {
            if (monitoring) { Stop("已停止观察"); return; }
            if (target == null) { SetStatus("请切到 Codex，按 " + shortcut + "+F8 自动定位，或 F7 备用手动标定。"); return; }
            if (gate.Failed) { SetStatus("上次点击未确认成功；检查后按 " + shortcut + "+F8 重新定位，或 F7 手动标定。"); return; }
            gate.BreakStreak(); monitoring = true; toggle.Text = "停止观察";
            SetStatus(automatic.Checked ? "监控中：允许自动点击" : "监控中：仅观察，不会点击");
        }
        void Stop(string message)
        {
            generation++; monitoring = false; gate.BreakStreak(); toggle.Text = "开始观察"; SetStatus(message);
        }
        void TickMonitor()
        {
            if (!monitoring || updating || target == null) return;
            if (background.Checked) { TickBackground(); return; }
            updating = true;
            try
            {
                Rectangle crop; string reason;
                if (!target.Locate(recognizer, ChooseRegion, out crop, out reason))
                {
                    gate.BreakStreak();
                    if (reason == "已取消区域选择") { Stop("已取消区域选择，监控已停止"); return; }
                    gate.Observe(ButtonState.Unknown, DateTime.UtcNow);
                    if (gate.Failed) Stop("点击后 8 秒未确认运行方块；已停止，请检查并重新标定。");
                    else SetStatus(reason);
                    return;
                }
                Detection d;
                using (Bitmap b = Native.Capture(crop)) { d = recognizer.Classify(b); UpdatePreview(b, d); }
                Rectangle badgeScreen = d.ButtonBounds; badgeScreen.Offset(crop.Location);
                if (!d.ButtonBounds.IsEmpty && !lastBadge.IsEmpty &&
                    (Math.Abs(badgeScreen.X - lastBadge.X) > 2 || Math.Abs(badgeScreen.Y - lastBadge.Y) > 2)) gate.BreakStreak();
                lastBadge = badgeScreen;
                bool ready = gate.Observe(d.State, DateTime.UtcNow);
                if (gate.Failed) { Stop("点击后 8 秒未确认运行方块；已停止，请检查并重新标定。"); return; }
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
                if (clicked) { gate.MarkAttempt(DateTime.UtcNow); SetStatus("已发送一次鼠标点击，正在检查是否变为方块"); }
                else { gate.BreakStreak(); SetStatus("点击前状态变化，已取消本次点击并重新观察"); }
            }
            catch (Exception e) { Stop("监控已停止：" + e.Message); }
            finally { updating = false; }
        }

        bool Current(Target connected, int revision)
        { return !IsDisposed && monitoring && background.Checked && target == connected && generation == revision; }

        void BackgroundWait(string reason)
        {
            gate.BreakStreak(); gate.Observe(ButtonState.Unknown, DateTime.UtcNow);
            if (reason == "已取消区域选择") Stop("已取消区域选择，监控已停止");
            else if (gate.Failed) Stop("后台点击后 8 秒未确认方块；此窗口可能不接受后台点击，请检查后重新连接");
            else SetStatus(reason);
        }

        async void TickBackground()
        {
            updating = true;
            Target connected = target; int revision = generation;
            try
            {
                string reason;
                if (!connected.ValidateIdentity(out reason)) { BackgroundWait(reason); return; }
                WindowFrame captured;
                try { captured = await Task.Run(delegate { return WindowCapture.Snapshot(connected.Window); }); }
                catch (Exception e) { if (Current(connected, revision)) BackgroundWait(e.Message); return; }
                using (WindowFrame frame = captured)
                {
                    if (!Current(connected, revision)) return;
                    Rectangle crop;
                    if (!connected.LocateBackground(frame, recognizer, ChooseRegion, out crop, out reason)) { if (Current(connected, revision)) BackgroundWait(reason); return; }
                    Rectangle local = crop; local.Offset(-frame.Bounds.X, -frame.Bounds.Y);
                    Detection d;
                    using (Bitmap b = frame.Image.Clone(local, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
                    { d = recognizer.Classify(b); UpdatePreview(b, d); }
                    Rectangle badge = d.ButtonBounds; badge.Offset(crop.Location);
                    if (!lastBadge.IsEmpty && (Math.Abs(badge.X - lastBadge.X) > 2 || Math.Abs(badge.Y - lastBadge.Y) > 2)) gate.BreakStreak();
                    lastBadge = badge;
                    bool ready = gate.Observe(d.State, DateTime.UtcNow);
                    if (gate.Failed) { Stop("后台点击未确认方块；已停止，不会重复点击"); return; }
                    if (gate.AwaitingRunning) { SetStatus("后台操作已投递一次，等待新捕获帧中的白色方块…"); return; }
                    if (!ready)
                    { SetStatus("后台捕获：" + Portable.StateName(d.State) + (d.State == ButtonState.Resume ? "，等待连续稳定新帧" : "，保持观察")); return; }
                    if (!automatic.Checked) { SetStatus("后台仅观察：已确认三角形，可勾选允许自动点击进行测试"); return; }
                    using (WindowFrame fresh = await Task.Run(delegate { return WindowCapture.Snapshot(connected.Window); }))
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
                        if (sent) { gate.MarkAttempt(DateTime.UtcNow); SetStatus("后台操作已投递：" + BackgroundClick.LastRoute + "；等待方块确认，尚不计为成功"); }
                        else BackgroundWait("后台点击前窗口状态变化，已取消本次点击");
                    }
                }
            }
            catch (Exception e) { if (Current(connected, revision)) Stop("后台监控已停止：" + e.Message); }
            finally { updating = false; }
        }
        void UpdatePreview(Bitmap b, Detection d)
        {
            Image previous = preview.Image; preview.Image = new Bitmap(b); if (previous != null) previous.Dispose();
            scores.Text = String.Format("{0} · {1}\n三角形 {2:P0}   方块 {3:P0}   发送 {4:P0}   语音 {5:P0}\n已点击：{6}    已确认恢复：{7}\n{8}",
                (background.Checked ? "后台 · " : "前台 · ") + (target != null && target.Automatic ? "自动定位" : "手动标定"), Portable.StateName(d.State), d.ResumeScore, d.RunningScore, d.SendScore, d.VoiceScore, gate.Attempts, gate.Verified, d.Detail);
        }
        void ShowHelp()
        {
            MessageBox.Show(this, "CodexAutoResume 1.1.1 · 单文件便携版\n\n只复制这一个 EXE 到其他 Windows 10/11 x64 电脑即可使用。\n识别样本、图标、MIT 许可和本说明均已内置。\n依赖系统 .NET Framework 4.x；后台功能需要 Windows 10 1903+。\n\n先选前台 / 后台模式，再切到 Codex 按 " + shortcut + "+F8 连接。\n多个候选必须手动选择确认。窗口位置和大小可以变化。\n备用标定：鼠标放到按钮中心，按 " + shortcut + "+F7。\n" + shortcut + "+F9 开始/停止；" + shortcut + "+F10 紧急停止。\n先用默认只观察核对识别，再勾选允许自动点击。\n\n后台模式（实验）：可切到其他应用，允许窗口完全被盖住。\n使用系统窗口捕获，可能显示系统黄色捕获边框。\n不主动激活目标或移动鼠标；应用是否接受后台消息取决于版本。\n已验证演示和 Chromium 测试页后台点击、真实 Codex 遮挡捕获；\n真实 Codex 的暂停到恢复尚需实际暂停场景验证。\n最小化时暂停；黑屏、无新帧或未知图形均不点击。\n8 秒未确认运行方块则停止，不会循环重试。\n前台模式仍要求按钮可见且目标在前台。\n\n三角形可恢复；方块运行中；向上箭头发送；声波语音。\n每次重启后需要按快捷键连接 Codex。日志及主动保存局部图：\n" + logDirectory + "\nF2 本机自检；F3 查看 MIT 许可。", "使用说明", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        void ShowSelfCheck()
        {
            Stop("正在本机自检，监控已停止");
            try
            {
                string report = Portable.BuildReport();
                SetStatus("本机自检通过：系统接口、窗口控件、内嵌图片和数据目录正常");
                MessageBox.Show(this, "本机自检通过。\n此检查不会发送鼠标点击；自动恢复请用演示窗口验证。\n\n" + report, "本机自检", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception e) { SetStatus("本机自检失败：" + e.Message); MessageBox.Show(this, e.ToString(), "本机自检失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
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
            Label guide = new Label { Dock = DockStyle.Top, Height = 90, Text = "演示使用与实图相同的蓝色圆按钮。\n按 " + shortcut + "+F8 自动定位；F7 鼠标手动标定备用。\n先观察识别，再去主窗口勾选自动点击并切回这里。\n自动点击后应变为方块，计数只增加一次。" };
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

