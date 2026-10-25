using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexAutoResume
{
    internal static class ChromiumTests
    {
        delegate bool EnumProc(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr parameter);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, uint message, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
        static void Require(bool value, string description) { if (!value) throw new Exception(description); Console.WriteLine("PASS: " + description); }
        static void Pump(int ms) { DateTime end = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < end) { Application.DoEvents(); Thread.Sleep(10); } }
        static WindowFrame Capture(IntPtr window)
        {
            Task<WindowFrame> task = Task.Run(delegate { return WindowCapture.Snapshot(window); });
            while (!task.IsCompleted) Pump(10);
            return task.GetAwaiter().GetResult();
        }
        static WindowFrame CaptureButton(IntPtr window, Target target, Recognizer recognizer, ButtonState expected)
        {
            DateTime end = DateTime.UtcNow.AddSeconds(6);
            do
            {
                WindowFrame frame = Capture(window); Rectangle crop; string reason;
                if (target.LocateBackground(frame, recognizer, null, out crop, out reason))
                {
                    Rectangle local = crop; local.Offset(-frame.Bounds.X, -frame.Bounds.Y);
                    using (Bitmap b = frame.Image.Clone(local, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
                        if (recognizer.Classify(b).State == expected) return frame;
                }
                if (DateTime.UtcNow.AddMilliseconds(200) >= end)
                {
                    frame.Image.Save(Path.Combine(Path.GetTempPath(), "CodexAutoResume-chromium-fixture.png"));
                    Rectangle inner = frame.Bounds; inner.Inflate(-20,-45);
                    if (Native.Uncovered(window, inner)) using (Bitmap visible = Native.Capture(inner)) visible.Save(Path.Combine(Path.GetTempPath(), "CodexAutoResume-chromium-visible.png"));
                    Console.WriteLine("Fixture sample: content=" + frame.ContentSequence + "; age=" + (DateTime.UtcNow-frame.ContentAt).TotalSeconds.ToString("F1") + "; foreground=" + (Native.GetForegroundWindow()==window));
                }
                frame.Dispose(); Pump(100);
            } while (DateTime.UtcNow < end);
            throw new Exception("Chromium fixture did not render expected " + expected);
        }
        [STAThread] static int Main()
        {
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
            IntPtr browser = IntPtr.Zero;
            try
            {
                Native.SetDpi(); Application.EnableVisualStyles();
                string folder = Path.Combine(Path.GetTempPath(), "CodexAutoResume-chromium-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(folder);
                string title = "CodexAutoResume Fixture " + Guid.NewGuid().ToString("N");
                string page = Path.Combine(folder, "fixture.html");
                File.WriteAllText(page, "<!doctype html><meta charset='utf-8'><title>" + title + "</title><style>body{background:#181818;margin:0}button{border:0;background:#2b67cb;border-radius:50%;position:fixed;right:60px;bottom:55px;width:36px;height:36px}#glyph{display:block;width:0;height:0;margin-left:8px;border-top:6px solid transparent;border-bottom:6px solid transparent;border-left:10px solid white}.running #glyph{width:10px;height:10px;border:0;background:white;margin-left:7px}</style><button aria-label='Resume' onclick=\"this.className='running';document.title='" + title + " clicked once';this.onclick=null\"><i id='glyph'></i></button>");
                string chrome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe");
                Process.Start(new ProcessStartInfo(chrome, "--user-data-dir=\"" + Path.Combine(folder, "profile") + "\" --app=\"" + new Uri(page).AbsoluteUri + "\" --window-size=780,520 --window-position=60,60 --no-first-run --no-default-browser-check --disable-sync --disable-background-networking --disable-backgrounding-occluded-windows") { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
                DateTime deadline = DateTime.UtcNow.AddSeconds(15);
                while (browser == IntPtr.Zero && DateTime.UtcNow < deadline)
                {
                    EnumWindows(delegate(IntPtr window, IntPtr unused) { if (Native.Title(window) == title) browser = window; return true; }, IntPtr.Zero);
                    Pump(100);
                }
                Require(browser != IntPtr.Zero, "isolated Chromium fixture launched");
                // A freshly launched hidden Chromium can defer its very first
                // compositor surface. Show only our isolated fixture before
                // covering it, as a user would first open their target app.
                ShowWindow(browser, 5); Pump(1000);
                using (Process process = Process.GetProcessById(Native.Pid(browser)))
                using (Form cover = new Form { Text = "CodexAutoResume Chromium occlusion test", TopMost = true, BackColor = Color.Magenta, StartPosition = FormStartPosition.Manual })
                {
                    Target target = new Target { Window = browser, ProcessId = process.Id, ProcessStart = process.StartTime.ToUniversalTime().Ticks, ProcessPath = process.MainModule.FileName, Automatic = true };
                    Recognizer recognizer = new Recognizer(); Rectangle crop; string why;
                    using (WindowFrame initial = CaptureButton(browser, target, recognizer, ButtonState.Resume)) Require(true, "Chromium fixture rendered before occlusion");
                    Native.RECT bounds; Native.GetWindowRect(browser, out bounds);
                    cover.Bounds = Rectangle.Inflate(bounds.Rectangle, 25, 25); cover.Show(); cover.Activate(); Pump(300);
                    IntPtr foreground = Native.GetForegroundWindow();
                    Require(foreground != browser, "Chromium fixture has no focus");
                    using (WindowFrame frame = CaptureButton(browser, target, recognizer, ButtonState.Resume))
                    {
                        Require(target.LocateBackground(frame, recognizer, null, out crop, out why), "locate occluded Chromium button: " + why);
                        using (Bitmap screen = Native.Capture(crop)) Require(recognizer.Classify(screen).State == ButtonState.Unknown, "screen capture sees opaque covering window");
                        Native.POINT before; Native.GetCursorPos(out before);
                        Require(BackgroundClick.Send(browser, frame.Bounds, new Point(crop.Left + crop.Width / 2, crop.Top + crop.Height / 2), delegate { return target.ValidateIdentity(out why); }), "Chromium click queued via " + BackgroundClick.LastRoute);
                        Pump(500);
                        Native.POINT after; Native.GetCursorPos(out after);
                        Require(before.X == after.X && before.Y == after.Y, "Chromium test pointer unchanged");
                        Require(Native.GetForegroundWindow() == foreground, "Chromium test foreground unchanged");
                    }
                    using (WindowFrame frame = CaptureButton(browser, target, recognizer, ButtonState.Running))
                    {
                        Require(target.LocateBackground(frame, recognizer, null, out crop, out why), "locate Chromium post-click button");
                        Rectangle local = crop; local.Offset(-frame.Bounds.X, -frame.Bounds.Y);
                        using (Bitmap button = frame.Image.Clone(local, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
                            Require(recognizer.Classify(button).State == ButtonState.Running, "occluded Chromium DOM changed triangle to square");
                    }
                    Require(Native.Title(browser) == title + " clicked once", "Chromium DOM click handler executed");
                }
                Console.WriteLine("Fixture data retained in task-specific temp folder: " + folder);
                return 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
            finally { if (browser != IntPtr.Zero) PostMessage(browser, 0x0010, IntPtr.Zero, IntPtr.Zero); }
        }
    }
}
