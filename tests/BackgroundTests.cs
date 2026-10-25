using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexAutoResume
{
    internal static class BackgroundTests
    {
        static void Require(bool value, string description) { if (!value) throw new Exception(description); Console.WriteLine("PASS: " + description); }
        static void Pump(int ms) { DateTime end = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < end) { Application.DoEvents(); Thread.Sleep(10); } }
        static WindowFrame Capture(IntPtr window)
        {
            Task<WindowFrame> task = Task.Run(delegate { return WindowCapture.Snapshot(window); });
            while (!task.IsCompleted) Pump(10);
            return task.GetAwaiter().GetResult();
        }
        [STAThread] public static int Main()
        {
            try
            {
                Native.SetDpi(); DesktopIdentity.Initialize(); Application.EnableVisualStyles();
                Require(WindowCapture.Supported, "Windows Graphics Capture supported");
                using (DemoForm demo = new DemoForm("Ctrl+Alt+Shift"))
                using (Form cover = new Form { Text = "CodexAutoResume occlusion test", BackColor = Color.Magenta, StartPosition = FormStartPosition.Manual, TopMost = true })
                {
                    demo.StartPosition = FormStartPosition.Manual; demo.Location = new Point(40, 40); demo.Show(); demo.Activate(); Pump(300);
                    System.Diagnostics.Process process = System.Diagnostics.Process.GetCurrentProcess();
                    Target target = new Target { Window = demo.Handle, ProcessId = process.Id, ProcessStart = process.StartTime.ToUniversalTime().Ticks, ProcessPath = process.MainModule.FileName, Automatic = true, Demo = true };
                    cover.Bounds = Rectangle.Inflate(demo.Bounds, 25, 25); cover.Show(); cover.Activate(); Pump(300);
                    IntPtr foregroundBefore = Native.GetForegroundWindow();
                    Require(foregroundBefore != demo.Handle, "target does not have focus");
                    Require(!Native.Uncovered(demo.Handle, demo.Bounds), "target is occluded");
                    Recognizer recognizer = new Recognizer(); Rectangle crop; string why;
                    using (WindowFrame frame = Capture(demo.Handle))
                    {
                        Require(target.LocateBackground(frame, recognizer, null, out crop, out why), "locate triangle under opaque cover: " + why);
                        Rectangle local = crop; local.Offset(-frame.Bounds.X, -frame.Bounds.Y);
                        using (Bitmap button = frame.Image.Clone(local, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
                            Require(recognizer.Classify(button).State == ButtonState.Resume, "fresh background capture shows triangle");
                        using (Bitmap screen = Native.Capture(crop)) Require(recognizer.Classify(screen).State != ButtonState.Resume, "screen pixels show cover, not triangle");
                        Native.POINT before; Native.GetCursorPos(out before);
                        Require(BackgroundClick.Send(demo.Handle, frame.Bounds, new Point(crop.Left + crop.Width / 2, crop.Top + crop.Height / 2), delegate { return target.ValidateIdentity(out why); }), "one mouse-message pair queued");
                        Pump(300);
                        Native.POINT after; Native.GetCursorPos(out after);
                        Require(before.X == after.X && before.Y == after.Y, "pointer unchanged");
                        Require(Native.GetForegroundWindow() == foregroundBefore, "foreground window unchanged");
                    }
                    using (WindowFrame frame = Capture(demo.Handle))
                    {
                        Require(target.LocateBackground(frame, recognizer, null, out crop, out why), "relocate after background click");
                        Rectangle local = crop; local.Offset(-frame.Bounds.X, -frame.Bounds.Y);
                        using (Bitmap button = frame.Image.Clone(local, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
                            Require(recognizer.Classify(button).State == ButtonState.Running, "background click changed triangle to square");
                    }
                    Require(demo.Controls.OfType<Label>().Any(l => l.Text == "按钮收到的点击：1"), "target received exactly one click");
                    using (WindowFrame frame = Capture(demo.Handle))
                    {
                        Require(target.LocateBackground(frame, recognizer, null, out crop, out why), "fresh frame remains usable");
                        Require(!BackgroundClick.Send(demo.Handle, frame.Bounds, crop.Location, delegate { return false; }), "cancelled validation prevents click");
                        frame.CapturedAt = DateTime.UtcNow.AddSeconds(-3);
                        Require(!target.LocateBackground(frame, recognizer, null, out crop, out why), "expired frame rejected");
                    }
                    using (WindowFrame frame = Capture(demo.Handle))
                    {
                        demo.Left += 40; cover.Bounds = Rectangle.Inflate(demo.Bounds, 25, 25); Pump(50);
                        Require(!target.LocateBackground(frame, recognizer, null, out crop, out why), "window move invalidates old frame");
                        Require(!BackgroundClick.Send(demo.Handle, frame.Bounds, frame.Bounds.Location, delegate { return true; }), "window move cancels old click coordinates");
                    }
                    demo.WindowState = FormWindowState.Minimized; Pump(100);
                    Require(!target.ValidateIdentity(out why), "minimized window rejected");
                    demo.WindowState = FormWindowState.Normal; Pump(100);
                    demo.Hide(); Require(!target.ValidateIdentity(out why), "hidden window rejected"); demo.Show(); Pump(100);
                    cover.Bounds = Rectangle.Inflate(demo.Bounds, 25, 25);
                    // A repeated-capture resource check detects retained WinRT/D3D handles.
                    for (int i = 0; i < 5; i++) using (WindowFrame frame = Capture(demo.Handle)) { }
                    process.Refresh(); int handles = process.HandleCount;
                    for (int i = 0; i < 30; i++) using (WindowFrame frame = Capture(demo.Handle)) { }
                    process.Refresh(); Console.WriteLine("Capture stress handles: " + handles + " -> " + process.HandleCount);
                    Require(process.HandleCount - handles < 80, "repeated capture releases native handles without waiting for GC");
                    handles = process.HandleCount;
                    for (int i = 0; i < 30; i++) using (WindowFrame frame = Capture(demo.Handle)) { }
                    Pump(500); process.Refresh(); Console.WriteLine("Second capture stress handles: " + handles + " -> " + process.HandleCount);
                    Require(process.HandleCount - handles < 30, "capture handles stabilize after warmup");
                }
                return 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        }
    }
}
