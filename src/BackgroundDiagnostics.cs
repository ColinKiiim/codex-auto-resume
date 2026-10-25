using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexAutoResume
{
    // The portable EXE carries its own fixture. This never clicks a Codex window.
    internal static class BackgroundDiagnostics
    {
        static readonly CaptureSampler standaloneSampler = new CaptureSampler(WindowCapture.Snapshot);
        sealed class Fixture : Form
        {
            public readonly IconButton Button = new IconButton { Size = new Size(64, 64) };
            public int Clicks;
            public Fixture(Rectangle area)
            {
                Text = "CodexAutoResume 后台自测目标"; ShowInTaskbar = false;
                Icon = DesktopIdentity.LoadIcon(); AutoScaleMode = AutoScaleMode.None;
                StartPosition = FormStartPosition.Manual; ClientSize = new Size(540, 330);
                Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
                BackColor = Color.FromArgb(24, 24, 24);
                Button.Location = new Point(ClientSize.Width - 100, ClientSize.Height - 90);
                Controls.Add(Button);
                Button.Click += delegate { Clicks++; Button.State = "方块"; Button.Invalidate(); };
            }
        }

        static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        static Detection Detect(WindowFrame frame, Target target, Recognizer recognizer, out Rectangle crop)
        {
            string reason;
            Require(target.LocateBackground(frame, recognizer, null, out crop, out reason), "自测无法定位按钮：" + reason);
            Rectangle local = crop; local.Offset(-frame.Bounds.X, -frame.Bounds.Y);
            using (Bitmap button = frame.Image.Clone(local, System.Drawing.Imaging.PixelFormat.Format32bppRgb)) return recognizer.Classify(button);
        }

        public static async Task<string> RunAsync(Form owner, CancellationToken cancellation, CaptureSampler sharedSampler = null)
        {
            cancellation.ThrowIfCancellationRequested();
            Require(WindowCapture.Supported, "当前系统不支持后台窗口捕获，需要 Windows 10 1903+ / Windows 11。");
            Rectangle area = owner == null ? Screen.PrimaryScreen.WorkingArea : Screen.FromControl(owner).WorkingArea;
            Recognizer recognizer = new Recognizer();
            CaptureSampler sampler = sharedSampler ?? standaloneSampler;
            ResumeGate gate = new ResumeGate();
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            using (Fixture fixture = new Fixture(area))
            using (Form cover = new Form { Text = "CodexAutoResume 后台自测", StartPosition = FormStartPosition.Manual,
                BackColor = Color.FromArgb(28, 54, 80), ForeColor = Color.White, ShowInTaskbar = false, TopMost = true })
            {
                try
                {
                    Label guide = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                        Text = "正在完全遮挡自测窗口，并验证后台恢复。\n通常只需几秒；请暂勿操作鼠标或切换窗口。\n\n关闭此窗口可取消自测。", Font = new Font("Microsoft YaHei UI", 11) };
                    cover.Controls.Add(guide);
                    cover.FormClosing += delegate(object sender, FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing) linked.Cancel(); };
                    fixture.FormClosing += delegate(object sender, FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing) linked.Cancel(); };
                    if (owner == null) fixture.Show(); else fixture.Show(owner);
                    await Task.Delay(120, linked.Token);
                    using (Process process = Process.GetCurrentProcess())
                    {
                        Target target = new Target { Window = fixture.Handle, ProcessId = process.Id, ProcessStart = process.StartTime.ToUniversalTime().Ticks,
                            ProcessPath = process.MainModule.FileName, Automatic = true, Demo = true };
                        cover.Bounds = Rectangle.Inflate(fixture.Bounds, 22, 22); cover.Show(fixture); cover.Activate();
                        await Task.Delay(150, linked.Token);
                        IntPtr foreground = Native.GetForegroundWindow();
                        Require(foreground != fixture.Handle, "自测目标仍在前台，请关闭干扰窗口后重试。");
                        Rectangle previous = Rectangle.Empty;
                        // Four live-session samples; static content need not emit
                        // four separate dirty frames. Main monitoring spaces samples.
                        for (int i = 0; i < 4; i++)
                        using (WindowFrame frame = await sampler.ReadAsync(fixture.Handle))
                        {
                            linked.Token.ThrowIfCancellationRequested();
                            Rectangle crop; Detection d = Detect(frame, target, recognizer, out crop);
                            Require(d.State == ButtonState.Resume, "自测三角形未正确识别。");
                            if (!previous.IsEmpty) Require(previous == crop, "自测窗口被移动，请重试。");
                            previous = crop;
                            bool ready = gate.Observe(d.State, DateTime.UtcNow);
                            Require(ready == (i == 3), "自测连续稳定帧检查失败。");
                            using (Bitmap screen = Native.Capture(crop))
                                Require(recognizer.Classify(screen).State == ButtonState.Unknown, "自测遮挡不完整，请勿移动自测窗口。");
                            if (i < 3) await Task.Delay(320, linked.Token);
                        }
                        using (WindowFrame fresh = await sampler.ReadAsync(fixture.Handle))
                        {
                            linked.Token.ThrowIfCancellationRequested();
                            Rectangle crop; Detection d = Detect(fresh, target, recognizer, out crop);
                            Require(crop == previous && d.State == ButtonState.Resume, "后台点击前自测图标或位置已改变。");
                            Point center = new Point(crop.Left + d.ButtonBounds.Left + d.ButtonBounds.Width / 2, crop.Top + d.ButtonBounds.Top + d.ButtonBounds.Height / 2);
                            Native.POINT before; Require(Native.GetCursorPos(out before), "无法读取鼠标位置。");
                            Require(Native.GetForegroundWindow() == foreground, "自测期间前台窗口变化，请暂勿操作后重试。");
                            Require(BackgroundClick.Send(fixture.Handle, fresh.Bounds, center, delegate
                            { string reason; return !linked.IsCancellationRequested && target.ValidateIdentity(out reason) && (DateTime.UtcNow - fresh.CapturedAt).TotalSeconds <= 2; }), "自测后台点击被取消。");
                            gate.MarkAttempt(DateTime.UtcNow);
                            await Task.Delay(80, linked.Token);
                            int stable = 0; DateTime squareDeadline = DateTime.UtcNow.AddSeconds(3);
                            while (stable < 3 && DateTime.UtcNow < squareDeadline)
                            using (WindowFrame running = await sampler.ReadAsync(fixture.Handle))
                            {
                                linked.Token.ThrowIfCancellationRequested();
                                Rectangle runningCrop; Detection after = Detect(running, target, recognizer, out runningCrop);
                                stable = after.State == ButtonState.Running && runningCrop == crop ? stable + 1 : 0;
                                gate.ObserveCaptured(after.State, DateTime.UtcNow, running.ContentAt);
                                if (stable < 3) await Task.Delay(80, linked.Token);
                            }
                            Require(stable == 3, "后台点击后未识别到稳定方块。");
                            Native.POINT pointer; Require(Native.GetCursorPos(out pointer), "无法复核鼠标位置。");
                            Require(before.X == pointer.X && before.Y == pointer.Y, "自测期间鼠标位置变化，请暂勿操作后重试。");
                            Require(Native.GetForegroundWindow() == foreground, "自测期间前台窗口变化，请暂勿操作后重试。");
                        }
                        Require(fixture.Clicks == 1 && gate.Attempts == 1 && gate.Verified == 1, "自测点击计数或恢复确认失败。");
                        return "PASS: fully occluded background diagnostic" + Environment.NewLine +
                            "完全遮挡窗口：持续会话识别三角形→后台点击一次→连续方块取样确认。" + Environment.NewLine +
                            "已点击 1；已确认恢复 1；鼠标与前台窗口不变。" + Environment.NewLine +
                            "仅测试工具自带窗口，不会点击 Codex 或发送消息。" + Environment.NewLine +
                            "实际 Codex 是否接受后台操作，仍需在它的暂停场景中验证。";
                    }
                }
                finally
                {
                    // Close sends the normal FormClosed lifecycle, removing the
                    // modeless forms from Application.OpenForms before Dispose.
                    try { if (!cover.IsDisposed) cover.Close(); }
                    finally { if (!fixture.IsDisposed) fixture.Close(); WindowCapture.StopCapture(); }
                }
            }
        }

        public static int RunCommand(string reportPath)
        {
            int code = 1;
            using (ApplicationContext context = new ApplicationContext())
            {
                EventHandler start = null;
                start = async delegate
                {
                    Application.Idle -= start;
                    try { string report = await RunAsync(null, CancellationToken.None); File.WriteAllText(reportPath, report); code = 0; }
                    catch (Exception e) { File.WriteAllText(reportPath, "FAIL: " + e.Message); }
                    finally { context.ExitThread(); }
                };
                Application.Idle += start; Application.Run(context);
            }
            return code;
        }
    }
}
