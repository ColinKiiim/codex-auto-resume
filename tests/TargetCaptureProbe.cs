using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexAutoResume
{
    // Read-only compatibility probe: no messages or input sent to the target app.
    internal static class TargetCaptureProbe
    {
        sealed class Cover : Form
        {
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override CreateParams CreateParams { get { CreateParams p = base.CreateParams; p.ExStyle |= 0x08000000; return p; } }
        }
        [STAThread] static int Main()
        {
            Native.SetDpi(); Application.EnableVisualStyles();
            string report = "Read-only actual-target occlusion probe; " + DateTime.Now.ToString("o") + Environment.NewLine;
            int count = 0;
            try
            {
                foreach (string processName in new string[] { "ChatGPT", "Codex" })
                foreach (Process process in Process.GetProcessesByName(processName))
                using (process)
                {
                    IntPtr window = process.MainWindowHandle;
                    if (window == IntPtr.Zero || Native.IsIconic(window)) continue;
                    string path = process.MainModule.FileName;
                    FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
                    if (!Target.IsCodexIdentity(process.ProcessName, path, info.CompanyName, info.ProductName)) continue;
                    Target target = new Target { Window = window, ProcessId = process.Id, ProcessStart = process.StartTime.ToUniversalTime().Ticks, ProcessPath = path, Automatic = true };
                    Native.RECT bounds; Native.GetWindowRect(window, out bounds);
                    using (Cover cover = new Cover { Text = "CodexAutoResume read-only occlusion probe", StartPosition = FormStartPosition.Manual, Bounds = Rectangle.Inflate(bounds.Rectangle, 20, 20), BackColor = Color.Magenta, TopMost = true, FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false })
                    {
                        cover.Show(); Pump(300); IntPtr foreground = Native.GetForegroundWindow();
                        for (int i = 0; i < 3; i++)
                        {
                            Task<WindowFrame> task = Task.Run(delegate { return WindowCapture.Snapshot(window); });
                            while (!task.IsCompleted) Pump(10);
                            using (WindowFrame frame = task.GetAwaiter().GetResult())
                            {
                                int top = Math.Max(0, frame.Image.Height - Math.Max(200, (int)(frame.Image.Height * .45)));
                                Rectangle search = new Rectangle(0, top, frame.Image.Width, frame.Image.Height - top);
                                using (Bitmap scan = frame.Image.Clone(search, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
                                {
                                    var buttons = new Recognizer().FindButtons(scan);
                                    report += "Frame " + frame.Sequence + ": " + frame.Image.Width + "x" + frame.Image.Height + "; age(ms)=" + (DateTime.UtcNow - frame.CapturedAt).TotalMilliseconds.ToString("F0") + "; candidates=" + buttons.Count + "; states=";
                                    foreach (LocatedButton button in buttons) report += button.Detection.State + " ";
                                    report += "; target covered=" + !Native.Uncovered(window, bounds.Rectangle) + "; foreground unchanged=" + (foreground == Native.GetForegroundWindow()) + Environment.NewLine;
                                }
                            }
                            Pump(400);
                        }
                        count++;
                    }
                }
                if (count == 0) throw new Exception("No non-minimized validated Codex window found.");
                report += "PASS: captured actual target while covered. No target click attempted.";
                Console.WriteLine(report); File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "target-capture-result.txt"), report); return 0;
            }
            catch (Exception e) { Console.WriteLine(report + e); return 1; }
        }
        static void Pump(int ms) { DateTime end = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < end) { Application.DoEvents(); Thread.Sleep(10); } }
    }
}
