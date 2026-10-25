using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace CodexAutoResume
{
    internal static class Tests
    {
        static int checks, failures;
        static void Check(bool ok, string name)
        {
            checks++; if (!ok) { failures++; Console.WriteLine("FAIL " + name); }
        }
        [STAThread] public static int Main(string[] args)
        {
            DesktopIdentity.Initialize();
            Check(QuestionDetector.Matches("正在询问 问题\n正在等待你的回答", ""), "active question status without action OCR");
            Check(QuestionDetector.Matches("1 of 3", "跳过"), "question pager with skip");
            Check(QuestionDetector.Matches("3 of 3", "提交"), "question pager with typed submit");
            Check(QuestionDetector.Matches("Waiting for your answer", "Submit"), "English waiting panel");
            Check(!QuestionDetector.Matches("普通消息中提到等待你的回答", ""), "quoted waiting text alone is insufficient");
            Check(!QuestionDetector.Matches("1 of 3", ""), "pager alone is insufficient");
            Check(!QuestionDetector.Matches("一般输入框", "提交"), "submit alone is insufficient");
            Check(!QuestionDetector.Matches("新增群组（推荐）", "跳过"), "recommended choice alone is insufficient");
            Check(DesktopIdentity.CurrentId() == DesktopIdentity.AppId, "explicit standalone taskbar identity");
            Check(DesktopIdentity.AppId.IndexOf("OpenAI", StringComparison.OrdinalIgnoreCase) < 0, "own identity distinct from host application");
            using (Icon icon = DesktopIdentity.LoadIcon()) Check(icon.Width == 32 && icon.Height == 32, "embedded taskbar icon");
            string license = DesktopIdentity.LicenseText();
            Check(license.StartsWith("MIT License"), "embedded MIT license title");
            Check(license.Contains("Copyright (c) 2026 CodexAutoResume contributors") && license.Contains("THE SOFTWARE IS PROVIDED"), "embedded copyright and license terms");
            using (System.Windows.Forms.Form window = new System.Windows.Forms.Form())
            {
                DesktopIdentity.ApplyToWindow(window.Handle);
                Check(DesktopIdentity.WindowId(window.Handle) == DesktopIdentity.AppId, "window-level explicit taskbar identity");
                DesktopIdentity.ClearWindow(window.Handle);
                Check(String.IsNullOrEmpty(DesktopIdentity.WindowId(window.Handle)), "window shell properties cleaned before close");
            }
            string root = args.Length > 0 ? args[0] : Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
            string screenshot = Path.Combine(root, "tests", "send-screenshot.png");
            string sendReference = Path.Combine(root, "assets", "send-reference.png");
            if (File.Exists(screenshot))
            {
                using (Bitmap full = new Bitmap(screenshot))
                using (Bitmap crop = full.Clone(new Rectangle((int)(1538.5 / 2048 * full.Width) - 32,
                    (int)(1061.5 / 1104 * full.Height) - 32, 64, 64), full.PixelFormat)) crop.Save(sendReference);
            }
            Recognizer r = new Recognizer();
            foreach (string name in new string[] { "resume", "running", "send", "send-current", "voice" })
            {
                ButtonState expected = name == "resume" ? ButtonState.Resume : name == "running" ? ButtonState.Running : name == "voice" ? ButtonState.Voice : ButtonState.Send;
                using (Bitmap sample = new Bitmap(Path.Combine(root, "assets", name + "-reference.png")))
                {
                    Detection d = r.Classify(sample); Console.WriteLine(name + " real sample: " + d);
                    Check(d.State == expected, name + " original");
                    foreach (double scale in new double[] { .65, .8, 1, 1.25, 1.5, 2 })
                    foreach (int offsetX in new int[] { -4, 0, 4 }) foreach (int offsetY in new int[] { -4, 0, 4 })
                    {
                        int side = (int)Math.Ceiling(84 * scale);
                        using (Bitmap canvas = new Bitmap(side, side))
                        using (Graphics g = Graphics.FromImage(canvas))
                        {
                            g.Clear(Color.FromArgb(35, 35, 35)); g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            int sw = (int)Math.Round(sample.Width * scale), sh = (int)Math.Round(sample.Height * scale);
                            g.DrawImage(sample, new Rectangle((side - sw) / 2 + offsetX, (side - sh) / 2 + offsetY, sw, sh));
                            Detection changed = r.Classify(canvas);
                            Check(changed.State == expected, name + " scale=" + scale + " offset=" + offsetX + "," + offsetY + ": " + changed);
                        }
                    }
                }
            }
            foreach (string name in new string[] { "三角形", "方块", "向上箭头", "语音", "未知符号" })
            using (Bitmap b = new Bitmap(64, 64))
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(Color.FromArgb(24, 24, 24)); IconButton.Draw(g, new Rectangle(0, 0, 64, 64), name);
                Detection d = r.Classify(b);
                ButtonState expected = name == "三角形" ? ButtonState.Resume : name == "方块" ? ButtonState.Running : name == "向上箭头" ? ButtonState.Send : name == "语音" ? ButtonState.Voice : ButtonState.Unknown;
                Console.WriteLine("demo " + name + ": " + d); Check(d.State == expected, "demo " + name);
                b.Save(Path.Combine(root, "tests", "demo-" + expected + (name == "向上箭头" ? "-arrow" : "") + ".png"));
            }
            // Other icons that could otherwise be confused with play.
            foreach (string shape in new string[] { "left", "up", "down", "chevron", "pause", "empty", "text", "flat-blue", "white-circle" })
            using (Bitmap b = new Bitmap(64, 64)) using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(Color.FromArgb(24, 24, 24)); g.SmoothingMode = SmoothingMode.AntiAlias;
                using (Brush blue = new SolidBrush(Color.FromArgb(43, 103, 203))) g.FillEllipse(blue, 14, 14, 36, 36);
                if (shape == "left") g.FillPolygon(Brushes.White, new Point[] { new Point(36, 26), new Point(26, 32), new Point(36, 38) });
                if (shape == "up") g.FillPolygon(Brushes.White, new Point[] { new Point(26, 36), new Point(32, 26), new Point(38, 36) });
                if (shape == "down") g.FillPolygon(Brushes.White, new Point[] { new Point(26, 28), new Point(32, 38), new Point(38, 28) });
                if (shape == "chevron") using (Pen pen = new Pen(Color.White, 2)) g.DrawLines(pen, new Point[] { new Point(28, 26), new Point(36, 32), new Point(28, 38) });
                if (shape == "pause") { g.FillRectangle(Brushes.White, 27, 26, 3, 12); g.FillRectangle(Brushes.White, 34, 26, 3, 12); }
                if (shape == "text") g.DrawString("A", SystemFonts.DefaultFont, Brushes.White, 26, 25);
                if (shape == "flat-blue") g.Clear(Color.FromArgb(43, 103, 203));
                if (shape == "white-circle") { g.Clear(Color.FromArgb(24, 24, 24)); g.FillEllipse(Brushes.White, 14, 14, 36, 36); }
                Check(r.Classify(b).State == ButtonState.Unknown, "reject " + shape + ": " + r.Classify(b));
            }
            GateTests();
            MonitoringTests();
            SamplerTests();
            LocatorTests(r, root);
            IdentityTests();
            string result = "Checks: " + checks + "; failures: " + failures;
            Console.WriteLine(result); File.WriteAllText(Path.Combine(root, "tests", "last-result.txt"), DateTime.Now.ToString("O") + Environment.NewLine + result + Environment.NewLine);
            return failures == 0 ? 0 : 1;
        }
        static void IdentityTests()
        {
            Check(Target.IsCodexIdentity("ChatGPT", @"C:\Program Files\WindowsApps\OpenAI.Codex_1_x64__abc\app\ChatGPT.exe", "", ""), "store install on C");
            Check(Target.IsCodexIdentity("ChatGPT", @"D:\Apps\WindowsApps\OpenAI.Codex_9_x64__abc\app\ChatGPT.exe", "", ""), "store install on other drive");
            Check(Target.IsCodexIdentity("Codex", @"E:\用户\OpenAI\Codex\Codex.exe", "", ""), "unicode traditional install");
            Check(Target.IsCodexIdentity("ChatGPT", @"D:\CustomApps\ChatGPT.exe", "OpenAI OpCo, LLC", "Codex"), "custom path metadata fallback");
            Check(Target.IsCodexIdentity("ChatGPT", @"D:\NewInstall\ChatGPT.exe", "OpenAI", "ChatGPT"), "renamed product metadata fallback");
            Check(!Target.IsCodexIdentity("OtherApp", @"D:\Apps\OtherApp.exe", "OpenAI", "Codex"), "reject other process name");
            Check(!Target.IsCodexIdentity("ChatGPT", @"D:\Other\ChatGPT.exe", "Other", "Other"), "reject unknown app identity");
            Check(!Target.IsCodexIdentity("CodexAutoResume", @"D:\Tools\CodexAutoResume.exe", "OpenAI", "Codex"), "monitor not mistaken for target");
            Check(!Target.IsCodexIdentity("ChatGPT", null, null, null), "missing metadata handled");
        }
        static void LocatorTests(Recognizer r, string root)
        {
            foreach (string name in new string[] { "resume", "running", "send-current", "voice" })
            using (Bitmap sample = new Bitmap(Path.Combine(root, "assets", name + "-reference.png")))
            foreach (int x in new int[] { 60, 420, 780 })
            using (Bitmap area = new Bitmap(900, 300)) using (Graphics g = Graphics.FromImage(area))
            {
                g.Clear(Color.FromArgb(24, 24, 24)); g.DrawImageUnscaled(sample, x, 180);
                using (Brush blue = new SolidBrush(Color.FromArgb(43, 103, 203))) g.FillRectangle(blue, 200, 30, 250, 50);
                var buttons = r.FindButtons(area);
                Check(buttons.Count == 1, "auto locate " + name + " x=" + x);
                if (buttons.Count == 1) Check(buttons[0].Badge.Left >= x && buttons[0].Badge.Right <= x + sample.Width, "fresh position " + name + " x=" + x);
            }
            using (Bitmap area = new Bitmap(900, 300)) using (Graphics g = Graphics.FromImage(area))
            using (Bitmap sample = new Bitmap(Path.Combine(root, "assets", "resume-reference.png")))
            {
                g.Clear(Color.FromArgb(24, 24, 24));
                var none = r.FindButtons(area); Check(none.Count == 0, "empty scan");
                g.DrawImageUnscaled(sample, 80, 190); g.DrawImageUnscaled(sample, 700, 190);
                var multiple = r.FindButtons(area); Check(multiple.Count == 2, "two candidates both retained");
                RegionSelection selection = new RegionSelection();
                Check(selection.Resolve(multiple, area.Size) == -1, "multiple requires confirmation");
                var transientSingle = new System.Collections.Generic.List<LocatedButton> { multiple[0] };
                Check(selection.Resolve(transientSingle, area.Size) == 0, "transient ambiguity resumes automatically when only one candidate remains");
                selection.RequestChoice(); Check(selection.Resolve(transientSingle, area.Size) == -1, "manual selection can request confirmation even for one candidate");
                selection.CancelRequest(); Check(selection.Resolve(transientSingle, area.Size) == 0, "manual cancellation does not latch automatic selection off");
                selection.Confirm(1, multiple, area.Size);
                Check(selection.Resolve(multiple, area.Size) == 1, "confirmed second candidate retained");
                multiple.Reverse(); Check(selection.Resolve(multiple, area.Size) == 0, "selection follows location, not list order");
                var onlyOther = new System.Collections.Generic.List<LocatedButton> { multiple[1] };
                Check(selection.Resolve(onlyOther, area.Size) == -1, "selected missing never silently chooses another");
                Check(selection.Resolve(multiple, new Size(950, 320)) == -1, "resize after multi-choice requests reconfirmation");
                selection = new RegionSelection();
                Check(selection.Resolve(onlyOther, area.Size) == 0, "single candidate needs no calibration");
                Check(selection.Resolve(onlyOther, new Size(1100, 500)) == 0, "single candidate auto adapts to resize");
            }
            using (Bitmap large = new Bitmap(2560, 600)) using (Graphics g = Graphics.FromImage(large))
            using (Bitmap sample = new Bitmap(Path.Combine(root, "assets", "resume-reference.png")))
            {
                g.Clear(Color.FromArgb(24, 24, 24)); g.DrawImageUnscaled(sample, 1900, 500);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                Check(r.FindButtons(large).Count == 1, "wide screen locator"); watch.Stop();
                Console.WriteLine("2560x600 scan elapsed: " + watch.ElapsedMilliseconds + " ms");
            }
            using (Bitmap edge = new Bitmap(300, 180)) using (Graphics g = Graphics.FromImage(edge))
            {
                g.Clear(Color.FromArgb(24, 24, 24));
                IconButton.Draw(g, new Rectangle(248, 122, 64, 64), "三角形");
                var nearEdge = r.FindButtons(edge);
                Check(nearEdge.Count == 1 && nearEdge[0].Detection.State == ButtonState.Resume, "button near window edge located with clipped padding");
            }
        }
        static void SamplerTests()
        {
            using (System.Threading.ManualResetEvent entered = new System.Threading.ManualResetEvent(false))
            using (System.Threading.ManualResetEvent release = new System.Threading.ManualResetEvent(false))
            {
                int calls = 0; WindowFrame late = null;
                CaptureSampler sampler = new CaptureSampler(delegate(IntPtr window)
                {
                    System.Threading.Interlocked.Increment(ref calls); entered.Set(); release.WaitOne();
                    late = new WindowFrame { Image = new Bitmap(8, 8) }; return late;
                });
                var read = sampler.ReadAsync(IntPtr.Zero, 40);
                Check(entered.WaitOne(1000), "capture worker started");
                try { read.GetAwaiter().GetResult(); Check(false, "stalled capture deadline"); }
                catch (CaptureDeadlineException) { Check(true, "stalled capture has end-to-end deadline"); }
                Check(sampler.IsBusy, "native worker quarantined after deadline");
                try { sampler.ReadAsync(IntPtr.Zero, 40).GetAwaiter().GetResult(); Check(false, "no second stalled worker"); }
                catch (CaptureBusyException) { Check(calls == 1, "second capture rejected without creating a worker"); }
                release.Set();
                Check(System.Threading.SpinWait.SpinUntil(delegate { return !sampler.IsBusy; }, 1000), "quarantine clears when native capture returns");
                Check(late != null && late.Image == null, "late frame disposed and never returned for clicking");
                using (WindowFrame fresh = sampler.ReadAsync(IntPtr.Zero).GetAwaiter().GetResult())
                    Check(fresh.Image != null && calls == 2, "subsequent capture works after native worker finishes");
            }
            CaptureSampler failing = new CaptureSampler(delegate(IntPtr window) { throw new System.IO.IOException("fixture failure"); });
            try { failing.ReadAsync(IntPtr.Zero).GetAwaiter().GetResult(); Check(false, "capture failure propagated"); }
            catch (System.IO.IOException) { Check(!failing.IsBusy, "provider failure clears pending worker"); }
            using (System.Threading.ManualResetEvent release = new System.Threading.ManualResetEvent(false))
            {
                CaptureSampler delayedFailure = new CaptureSampler(delegate(IntPtr window) { release.WaitOne(); throw new System.IO.IOException("late failure"); });
                try { delayedFailure.ReadAsync(IntPtr.Zero, 20).GetAwaiter().GetResult(); Check(false, "late failure deadline"); }
                catch (CaptureDeadlineException) { Check(true, "late failing worker times out"); }
                release.Set();
                Check(System.Threading.SpinWait.SpinUntil(delegate { return !delayedFailure.IsBusy; }, 1000), "late fault observed and quarantine cleared");
            }
        }
        static void MonitoringTests()
        {
            DateTime now = new DateTime(2026, 10, 25, 1, 0, 0, DateTimeKind.Utc);
            using (WindowFrame frame = new WindowFrame { CapturedAt = now, ContentAt = now.AddHours(-10), ContentSequence = 1, Sequence = 1 })
            {
                Check(!frame.ContentFresh(now), "fresh read timestamp cannot make ten-hour cached pixels fresh");
                frame.ContentAt = now.AddSeconds(-4); Check(frame.ContentFresh(now), "recent native content usable");
                Check(!frame.ContentFresh(now.AddSeconds(2)), "cached native content expires despite active read loop");
                frame.ContentAt = DateTime.MinValue; Check(!frame.ContentFresh(now), "missing pixel timestamp rejected");
                frame.ContentAt = now.AddSeconds(5); Check(!frame.ContentFresh(now), "implausible future content timestamp rejected");
                frame.ContentAt = now;
                MonitorActivity activity = new MonitorActivity(); activity.Start(now); activity.Background(frame); activity.Recognized(ButtonState.Running);
                frame.CapturedAt = now.AddSeconds(20); frame.Sequence++;
                activity.Start(frame.CapturedAt); activity.Background(frame);
                Check(activity.NewContent == 1 && activity.Cached == 1 && activity.Completed == 2, "telemetry separates repeated cache reads from new content reads");
                Check(activity.LastContent == now && activity.LastSample == now.AddSeconds(20), "cache read never refreshes native-content time");
                Check(activity.Summary(now.AddSeconds(20), true, true, true, 2, 0, 0).Contains("20秒") && activity.LastIcon.Contains("方块"), "heartbeat exposes pixel age and last observed icon");
                activity.ResetSource(); Check(activity.LastContent == DateTime.MinValue && activity.LastSample == DateTime.MinValue, "new target resets prior target timestamps");
            }
            MonitorPulse pulse = new MonitorPulse(now);
            Check(!pulse.Due(now.AddSeconds(59), TimeSpan.FromMinutes(1)), "no early one-minute heartbeat");
            Check(pulse.Due(now.AddMinutes(1), TimeSpan.FromMinutes(1)), "one-minute heartbeat arrives without icon change");
            Check(!pulse.Due(now.AddMinutes(1), TimeSpan.FromMinutes(1)), "heartbeat does not repeat every timer tick");
            pulse.Reset(now);
            Check(!pulse.Due(now.AddMinutes(4), TimeSpan.FromMinutes(5)) && pulse.Due(now.AddMinutes(5), TimeSpan.FromMinutes(5)), "five-minute reminder interval respected");
            Check(!pulse.Due(now.AddMinutes(-1), TimeSpan.FromMinutes(5)), "clock moving backwards does not flood log");
            string folder = Path.Combine(Path.GetTempPath(), "CodexAutoResume-log-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            MonitorLog log = new MonitorLog(folder, 128);
            log.Write("events.log", "state", new string('x', 200), now);
            log.Write("events.log", "heartbeat", "same icon, loop alive\r\nsecond line", now.AddMinutes(1));
            Check(File.Exists(Path.Combine(folder, "events.log.1")), "bounded log rotates previous file");
            string contents = File.ReadAllText(Path.Combine(folder, "events.log"));
            Check(contents.Contains("[heartbeat]") && contents.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).Length == 1, "heartbeat has category and remains one record per line");
            log.Write("events.log", "detail", new string('y', 200), now);
            log.Write("events.log", "detail", "rotated again", now);
            Check(Directory.GetFiles(folder).Length == 2, "rotation retains only current and one previous file");
            string blocked = Path.Combine(folder, "blocked"); File.WriteAllText(blocked, "not a directory");
            MonitorLog failing = new MonitorLog(blocked); failing.Write("events.log", "state", "alive", now);
            Check(failing.WriteFailed, "log write failure is reported without stopping monitoring");
            foreach (string path in Directory.GetFiles(folder)) File.Delete(path); Directory.Delete(folder);
        }
        static void GateTests()
        {
            DateTime t = new DateTime(2026, 10, 24, 0, 0, 0, DateTimeKind.Utc);
            ResumeGate gate = new ResumeGate();
            for (int i = 0; i < 3; i++) Check(!gate.Observe(ButtonState.Resume, t.AddMilliseconds(i * 400)), "no early click " + i);
            Check(gate.Observe(ButtonState.Resume, t.AddSeconds(1.2)), "four stable frames ready");
            gate.MarkAttempt(t.AddSeconds(1.2));
            for (int i = 0; i < 10; i++) Check(!gate.Observe(ButtonState.Resume, t.AddSeconds(1.6 + i * .4)), "no repeat " + i);
            gate.BreakStreak(); Check(!gate.Observe(ButtonState.Unknown, t.AddSeconds(6)), "hidden does not rearm");
            for (int i = 0; i < 3; i++) gate.Observe(ButtonState.Running, t.AddSeconds(6.4 + i * .4));
            Check(!gate.AwaitingRunning && gate.Verified == 1, "stable square verifies and rearms");
            for (int i = 0; i < 4; i++) Check(!gate.Observe(ButtonState.Resume, t.AddSeconds(7.6 + i * .4)), "cooldown " + i);
            Check(gate.Observe(ButtonState.Resume, t.AddSeconds(13)), "new pause ready after cooldown");
            gate.MarkAttempt(t.AddSeconds(13)); gate.Observe(ButtonState.Unknown, t.AddSeconds(21));
            Check(!gate.AwaitingRunning && gate.Attempts == 2 && !gate.Observe(ButtonState.Resume, t.AddSeconds(22)), "missed confirmation keeps monitoring without latching failure");
            for (int i = 0; i < 3; i++) gate.Observe(ButtonState.Resume, t.AddSeconds(22.4 + i * .4));
            Check(gate.Observe(ButtonState.Resume, t.AddSeconds(24)), "stable triangle retries after interval without manual rebind");
            gate.MarkAttempt(t.AddSeconds(24));
            gate.Observe(ButtonState.Running, t.AddSeconds(24.1));
            Check(gate.Verified == 2 && !gate.AwaitingRunning, "brief valid square confirms recovery immediately");
            for (int i = 0; i < 100; i++) gate.Observe(ButtonState.Unknown, t.AddSeconds(40 + i));
            for (int i = 0; i < 4; i++) gate.Observe(ButtonState.Resume, t.AddSeconds(150 + i));
            Check(gate.Observe(ButtonState.Resume, t.AddSeconds(155)), "long unknown period never requires restarting monitor");
            gate.MarkAttempt(t.AddSeconds(155)); gate.Observe(ButtonState.Unknown, t.AddSeconds(170));
            gate.Observe(ButtonState.Running, t.AddSeconds(171));
            Check(gate.Verified == 3 && !gate.AwaitingRunning, "late square still verifies recovery after confirmation wait interval");
            gate.Observe(ButtonState.Running, t.AddSeconds(172));
            Check(gate.Verified == 3, "same late square is counted only once");
            gate = new ResumeGate();
            gate.Observe(ButtonState.Resume, t); gate.Observe(ButtonState.Resume, t); gate.BreakStreak();
            Check(!gate.Observe(ButtonState.Resume, t), "occlusion breaks stability streak");
            gate.Observe(ButtonState.Unknown, t);
            Check(!gate.Observe(ButtonState.Resume, t), "unknown breaks stability streak");
            foreach (ButtonState inactive in new ButtonState[] { ButtonState.Send, ButtonState.Voice })
            {
                gate = new ResumeGate();
                for (int i = 0; i < 40; i++) Check(!gate.Observe(inactive, t.AddMilliseconds(i * 400)), inactive + " never requests click " + i);
                for (int i = 0; i < 3; i++) gate.Observe(ButtonState.Resume, t);
                gate.Observe(inactive, t);
                Check(!gate.Observe(ButtonState.Resume, t), inactive + " resets resume streak");
                gate.MarkAttempt(t);
                for (int i = 0; i < 3; i++) gate.Observe(inactive, t.AddSeconds(1));
                Check(gate.AwaitingRunning && gate.Verified == 0, inactive + " cannot verify running");
            }
        }
    }
}
