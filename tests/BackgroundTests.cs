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
        static readonly CaptureSampler sampler = new CaptureSampler(WindowCapture.Snapshot, WindowCapture.ActionSnapshot);
        static void Require(bool value, string description) { if (!value) throw new Exception(description); Console.WriteLine("PASS: " + description); }
        static void Pump(int ms) { DateTime end = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < end) { Application.DoEvents(); Thread.Sleep(10); } }
        static WindowFrame Capture(IntPtr window)
        {
            Task<WindowFrame> task = sampler.ReadAsync(window);
            while (!task.IsCompleted) Pump(10);
            return task.GetAwaiter().GetResult();
        }
        static WindowFrame WaitFor(IntPtr window, Func<WindowFrame, bool> predicate)
        {
            DateTime end = DateTime.UtcNow.AddSeconds(5);
            do
            {
                WindowFrame frame = null;
                try { frame = Capture(window); if (predicate(frame)) return frame; }
                catch (TimeoutException) { }
                if (frame != null) frame.Dispose();
                Pump(80);
            } while (DateTime.UtcNow < end);
            throw new Exception("Expected fixture pixels did not arrive within 5 seconds");
        }
        [STAThread] public static int Main()
        {
            Native.SetDpi(); DesktopIdentity.Initialize(); Application.EnableVisualStyles();
            int result = 1;
            using (ApplicationContext context = new ApplicationContext())
            {
                EventHandler start = null;
                start = delegate { Application.Idle -= start; try { result = Run(); } finally { context.ExitThread(); } };
                Application.Idle += start; Application.Run(context);
            }
            return result;
        }
        static int Run()
        {
            try
            {
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
                        Require(before.X == after.X && before.Y == after.Y, "pointer unchanged (" + before.X + "," + before.Y + " -> " + after.X + "," + after.Y + ")");
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
                        DateTime nativeTime = frame.ContentAt;
                        frame.ContentAt = DateTime.UtcNow.AddHours(-10);
                        Require(!target.LocateBackground(frame, recognizer, null, out crop, out why) && why.Contains("操作前"), "fresh read of ten-hour cached pixels cannot locate an actionable button");
                        Require(target.LocateBackground(frame, recognizer, null, out crop, out why, false), "static cached picture remains usable for observation without authorizing action");
                        frame.ContentAt = nativeTime;
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
                    CheckBox multiple = demo.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<CheckBox>().Single();
                    multiple.Checked = true; Pump(100);
                    using (WindowFrame frame = WaitFor(demo.Handle, f => recognizer.FindButtons(f.Image).Count == 2))
                    {
                        Require(!target.LocateBackground(frame, recognizer, null, out crop, out why) && why == "候选区域需要用户确认", "background multiple candidates cannot auto-select");
                        int offered = 0;
                        Require(!target.LocateBackground(frame, recognizer, delegate(Bitmap scan, System.Collections.Generic.List<LocatedButton> candidates) { offered = candidates.Count; return -1; }, out crop, out why)
                            && offered == 2 && why == "已取消区域选择", "background candidate cancellation stops selection");
                        Require(!target.LocateBackground(frame, recognizer, delegate(Bitmap scan, System.Collections.Generic.List<LocatedButton> candidates) { return candidates.FindIndex(b => b.Detection.State == ButtonState.Resume); }, out crop, out why)
                            && why == "区域已确认，等待新的后台帧", "background confirmation requires a fresh frame before clicking");
                    }
                    Rectangle selected;
                    using (WindowFrame frame = Capture(demo.Handle))
                    {
                        Require(target.LocateBackground(frame, recognizer, null, out selected, out why), "confirmed background candidate retained");
                        Rectangle local = selected; local.Offset(-frame.Bounds.X, -frame.Bounds.Y);
                        Detection chosen;
                        using (Bitmap b = frame.Image.Clone(local, System.Drawing.Imaging.PixelFormat.Format32bppRgb)) chosen = recognizer.Classify(b);
                        Require(chosen.State == ButtonState.Resume, "selected background candidate is intended triangle");
                        Point center = new Point(selected.Left + chosen.ButtonBounds.Left + chosen.ButtonBounds.Width / 2, selected.Top + chosen.ButtonBounds.Top + chosen.ButtonBounds.Height / 2);
                        Require(BackgroundClick.Send(demo.Handle, frame.Bounds, center, delegate { return target.ValidateIdentity(out why); }), "selected background candidate receives click");
                    }
                    Pump(100);
                    using (WindowFrame frame = Capture(demo.Handle))
                    {
                        Require(target.LocateBackground(frame, recognizer, null, out crop, out why) && crop == selected, "selected candidate stays selected after glyph changes");
                        Rectangle local = crop; local.Offset(-frame.Bounds.X, -frame.Bounds.Y);
                        using (Bitmap b = frame.Image.Clone(local, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
                            Require(recognizer.Classify(b).State == ButtonState.Running, "selected background candidate changed to square");
                    }
                    Require(demo.Controls.OfType<Label>().Any(l => l.Text == "按钮收到的点击：2"), "exactly two total clicks across two explicitly selected buttons");
                    multiple.Checked = false; Pump(100);
                    using (WindowFrame frame = Capture(demo.Handle))
                        Require(!target.LocateBackground(frame, recognizer, null, out crop, out why) && why == "候选区域需要用户确认", "missing selected region never switches to remaining candidate");
                    Target automatic = new Target { Window = demo.Handle, ProcessId = process.Id, ProcessStart = target.ProcessStart, ProcessPath = target.ProcessPath, Automatic = true, Demo = true };
                    Target manual;
                    Rectangle calibrated;
                    using (WindowFrame frame = Capture(demo.Handle))
                    {
                        Require(automatic.LocateBackground(frame, recognizer, null, out calibrated, out why), "locate remaining button for manual fallback test");
                        Native.RECT rect; Native.GetWindowRect(demo.Handle, out rect);
                        manual = new Target { Window = demo.Handle, ProcessId = process.Id, ProcessStart = target.ProcessStart, ProcessPath = target.ProcessPath, Automatic = false, Demo = true,
                            Width = rect.Right - rect.Left, Height = rect.Bottom - rect.Top, Dpi = Native.Dpi(demo.Handle), CropSize = calibrated.Width,
                            OffsetX = calibrated.Left + calibrated.Width / 2 - rect.Left, OffsetY = calibrated.Top + calibrated.Height / 2 - rect.Top };
                        Require(manual.LocateBackground(frame, recognizer, null, out crop, out why) && crop == calibrated, "manual calibration maps into occluded window frame");
                    }
                    demo.Left += 20; cover.Bounds = Rectangle.Inflate(demo.Bounds, 25, 25); Pump(100);
                    using (WindowFrame frame = Capture(demo.Handle))
                    { calibrated.Offset(20, 0); Require(manual.LocateBackground(frame, recognizer, null, out crop, out why) && crop == calibrated, "manual background coordinates follow window movement"); }
                    demo.Width += 24; cover.Bounds = Rectangle.Inflate(demo.Bounds, 25, 25); Pump(100);
                    using (WindowFrame frame = WaitFor(demo.Handle, f => f.Bounds.Size == WindowCapture.Bounds(demo.Handle).Size))
                        Require(!manual.LocateBackground(frame, recognizer, null, out crop, out why) && why == "窗口大小或缩放已变化，请重新标定", "manual background calibration rejects resized window");
                    Native.POINT comparisonPointer; Native.GetCursorPos(out comparisonPointer);
                    IntPtr comparisonForeground = Native.GetForegroundWindow();
                    long persistentStarts = WindowCapture.SessionStarts;
                    Task<WindowFrame> probeRead = sampler.ReadActionAsync(demo.Handle);
                    while (!probeRead.IsCompleted) Pump(10);
                    using (WindowFrame independent = probeRead.GetAwaiter().GetResult())
                    using (WindowFrame following = Capture(demo.Handle))
                    {
                        Require(independent.Source == "independent" && WindowCapture.SessionStarts == persistentStarts + 1, "independent probe adds one isolated session without replacing the live session");
                        Require(following.ContentAt >= independent.ContentAt && following.ContentSequence >= independent.ContentSequence, "live cache never rolls back to an older frame after an independent probe");
                    }
                    string comparisonDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CodexAutoResume-comparison-test-" + Guid.NewGuid().ToString("N"));
                    Task comparison = CaptureComparison.RunAsync(target, sampler, comparisonDirectory);
                    while (!comparison.IsCompleted) Pump(10);
                    comparison.GetAwaiter().GetResult();
                    Require(System.IO.File.Exists(System.IO.Path.Combine(comparisonDirectory, "02-independent.png")) && System.IO.File.ReadAllText(System.IO.Path.Combine(comparisonDirectory, "comparison.txt")).Contains("source=independent"), "capture comparison exports persistent and independent images with timestamps, hashes and coordinates");
                    Native.POINT comparisonAfter; Native.GetCursorPos(out comparisonAfter);
                    Require(comparisonPointer.X == comparisonAfter.X && comparisonPointer.Y == comparisonAfter.Y && comparisonForeground == Native.GetForegroundWindow(), "read-only capture comparison leaves pointer and foreground unchanged");
                    foreach (string file in System.IO.Directory.GetFiles(comparisonDirectory)) System.IO.File.Delete(file);
                    System.IO.Directory.Delete(comparisonDirectory);
                    // A repeated-capture resource check detects retained WinRT/D3D handles.
                    for (int i = 0; i < 5; i++) using (WindowFrame frame = Capture(demo.Handle)) { }
                    long starts = WindowCapture.SessionStarts;
                    process.Refresh(); int handles = process.HandleCount;
                    for (int i = 0; i < 30; i++) using (WindowFrame frame = Capture(demo.Handle)) { }
                    process.Refresh(); Console.WriteLine("Capture stress handles: " + handles + " -> " + process.HandleCount);
                    Require(process.HandleCount - handles < 80, "repeated capture releases native handles without waiting for GC");
                    handles = process.HandleCount;
                    for (int i = 0; i < 30; i++) using (WindowFrame frame = Capture(demo.Handle)) { }
                    Pump(500); process.Refresh(); Console.WriteLine("Second capture stress handles: " + handles + " -> " + process.HandleCount);
                    Require(process.HandleCount - handles < 30, "capture handles stabilize after warmup");
                    Require(WindowCapture.SessionStarts == starts, "same window reuses persistent session without border restart on each read");
                    int forms = Application.OpenForms.Count;
                    using (CancellationTokenSource cancelled = new CancellationTokenSource())
                    {
                        cancelled.Cancel();
                        Task<string> diagnostic = BackgroundDiagnostics.RunAsync(null, cancelled.Token);
                        try { diagnostic.GetAwaiter().GetResult(); Require(false, "pre-cancelled diagnostic"); }
                        catch (OperationCanceledException) { Require(Application.OpenForms.Count == forms, "cancelled diagnostic does not open test windows"); }
                    }
                    using (CancellationTokenSource cancelDuring = new CancellationTokenSource())
                    {
                        cancelDuring.CancelAfter(180);
                        Task<string> diagnostic = BackgroundDiagnostics.RunAsync(null, cancelDuring.Token);
                        DateTime end = DateTime.UtcNow.AddSeconds(5);
                        while (!diagnostic.IsCompleted && DateTime.UtcNow < end) Pump(10);
                        Require(diagnostic.IsCompleted, "in-progress diagnostic cancellation completes promptly");
                        try { diagnostic.GetAwaiter().GetResult(); Require(false, "in-progress diagnostic cancelled"); }
                        catch (OperationCanceledException)
                        {
                            Require(Application.OpenForms.Count == forms, "diagnostic cancellation disposes all fixture windows (" + forms + " -> " + Application.OpenForms.Count + ")");
                        }
                    }
                    Task<string> completedDiagnostic = BackgroundDiagnostics.RunAsync(null, CancellationToken.None);
                    DateTime completedBy = DateTime.UtcNow.AddSeconds(15);
                    while (!completedDiagnostic.IsCompleted && DateTime.UtcNow < completedBy) Pump(10);
                    Require(completedDiagnostic.IsCompleted && completedDiagnostic.GetAwaiter().GetResult().Contains("PASS: fully occluded background diagnostic"), "diagnostic succeeds after cancellation");
                    Require(Application.OpenForms.Count == forms, "successful diagnostic also removes all fixture windows");
                }
                return 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        }
    }
}
