using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexAutoResume
{
    internal static class UiLayoutTests
    {
        static T Field<T>(object instance, string name) { return (T)instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(instance); }
        static object Call(MainForm form, string method, params object[] args) { return form.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, args); }
        static void Require(bool ok, string text) { if (!ok) throw new Exception(text); Console.WriteLine("PASS: " + text); }
        static IEnumerable<Control> All(Control root) { foreach (Control child in root.Controls) { yield return child; foreach (Control next in All(child)) yield return next; } }
        static void Render(Form form, string name)
        { using (Bitmap image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(Path.GetTempPath(), name + ".png")); } }
        [STAThread] public static int Main(string[] args)
        {
            Native.SetDpi(); DesktopIdentity.Initialize(); Application.EnableVisualStyles();
            string settings = Path.Combine(Portable.DataDirectory, "settings.json"); byte[] previous = File.Exists(settings) ? File.ReadAllBytes(settings) : null;
            try
            {
                foreach (float scale in new[] { 1f, 1.5f, 2f })
                using (MainForm main = new MainForm())
                {
                    Field<UserPreferences>(main, "preferences").CheckUpdates = false;
                    Field<Timer>(main, "timer").Stop(); Field<Timer>(main, "healthTimer").Stop();
                    main.Show(); Call(main, "OpenDemo"); Field<CheckBox>(main, "automatic").Checked = true;
                    main.GetType().GetField("monitoring", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(main, false);
                    Application.DoEvents();
                    main.GetType().GetField("monitoring", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(main, true);
                    Field<Timer>(main, "stabilityTimer").Stop();
                    using (Bitmap sample = new Bitmap(Path.Combine(args[0], "assets", "running-reference.png")))
                    {
                        Detection detection = new Recognizer().Classify(sample); Call(main, "UpdatePreview", sample, detection);
                        Require(Field<PictureBox>(main, "preview").Image.Width <= detection.ButtonBounds.Width + 6, "main preview tightly crops the actual blue badge");
                    }
                    Field<MonitorActivity>(main, "activity").Foreground(DateTime.UtcNow);
                    Call(main, "SetStatus", "Codex 正在运行");
                    for (int i = 0; i < 4; i++) Call(main, "RecentProgress", "已检查 · Codex 正在运行");
                    if (scale != 1)
                    {
                        Control[] controls = All(main).Concat(new Control[] { main }).ToArray();
                        Font[] fonts = controls.Select(c => c.Font).ToArray();
                        main.AutoScaleMode = AutoScaleMode.None; main.Scale(new SizeF(scale, scale));
                        for (int i = 0; i < controls.Length; i++) controls[i].Font = new Font(fonts[i].FontFamily, fonts[i].SizeInPoints * scale, fonts[i].Style);
                    }
                    main.PerformLayout(); Application.DoEvents();
                    Require(Field<TextBox>(main, "recentLog").Lines.Length == 4, "main retains exactly four timestamped progress entries");
                    Button[] rightActions = All(main).OfType<Button>().ToArray();
                    int[] rights = rightActions.Select(b => main.PointToClient(b.PointToScreen(new Point(b.Width, 0))).X).ToArray();
                    Require(rights.Max() - rights.Min() <= 1, "main action buttons share a right edge at scale " + scale);
                    LinkLabel activity = All(main).OfType<LinkLabel>().Single(c => c.Name == "ActivityLink");
                    Require(activity.Width >= TextRenderer.MeasureText(activity.Text, activity.Font).Width, "activity link fits on one line at scale " + scale);
                    Require(All(main).Where(c => c.Visible).All(c => main.ClientRectangle.Contains(main.PointToClient(c.PointToScreen(Point.Empty))) && main.ClientRectangle.Contains(main.PointToClient(c.PointToScreen(new Point(Math.Max(0, c.Width - 1), Math.Max(0, c.Height - 1)))))), "visible main controls stay inside client bounds at scale " + scale);
                    Require(All(main).Where(c => c.Visible).All(c => c.Parent.ClientRectangle.Contains(c.Bounds)), "visible controls fit their immediate parent at scale " + scale);
                    Render(main, "CodexAutoResume-ui-" + (int)(scale * 100));
                    using (Bitmap question = new Bitmap(1600, 1184))
                    using (Graphics g = Graphics.FromImage(question))
                    using (Font font = new Font("Microsoft YaHei UI", 18))
                    {
                        g.Clear(Color.FromArgb(24, 24, 24)); g.FillRectangle(Brushes.DimGray, 545, 823, 920, 338);
                        g.DrawString("正在询问问题", font, Brushes.White, 575, 690); g.DrawString("正在等待你的回答", font, Brushes.White, 575, 745);
                        g.DrawString("3 of 3", font, Brushes.White, 1330, 840); g.DrawString("提交", font, Brushes.White, 1390, 1110); g.Flush();
                        // This suite checks geometry, using the known bounds of
                        // its synthetic panel. Live OCR is covered separately by
                        // QuestionTests and the actual MainForm workflow suite.
                        typeof(QuestionDetector).GetProperty("PreviewBounds", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(Field<QuestionDetector>(main, "questions"), new Rectangle(550, 680, 940, 480), null);
                        Call(main, "UpdateQuestionPreview", question); Call(main, "RecordQuestion", true);
                        Field<Timer>(main, "questionTimer").Stop(); main.PerformLayout(); Application.DoEvents();
                        Require(Field<PictureBox>(main, "preview").Width > 150 * scale, "question preview uses a wider presentation than the blue badge");
                        Render(main, "CodexAutoResume-question-ui-" + (int)(scale * 100));
                    }
                    if (scale == 1)
                    {
                        Call(main, "ShowSettings"); Form window = Field<Form>(main, "settingsWindow"); Render(window, "CodexAutoResume-settings-ui"); window.Close();
                    }
                    main.Close();
                }
                return 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
            finally { if (previous == null) File.Delete(settings); else File.WriteAllBytes(settings, previous); }
        }
    }
}
