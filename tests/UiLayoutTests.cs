using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexAutoResume
{
    internal static class UiLayoutTests
    {
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wparam, IntPtr lparam);
        static T Field<T>(object instance, string name) { return (T)instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(instance); }
        static object Call(MainForm form, string method, params object[] args) { return form.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, args); }
        static void Require(bool ok, string text) { if (!ok) throw new Exception(text); Console.WriteLine("PASS: " + text); }
        static IEnumerable<Control> All(Control root) { foreach (Control child in root.Controls) { yield return child; foreach (Control next in All(child)) yield return next; } }
        static void Render(Form form, string name)
        { using (Bitmap image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); Point offset = form.PointToScreen(Point.Empty); offset.Offset(-form.Left, -form.Top); using (Bitmap client = image.Clone(new Rectangle(offset, form.ClientSize), image.PixelFormat)) client.Save(Path.Combine(Path.GetTempPath(), name + ".png")); } }
        [STAThread] public static int Main(string[] args)
        {
            Native.SetDpi(); DesktopIdentity.Initialize(); Application.EnableVisualStyles();
            string settings = Path.Combine(Portable.DataDirectory, "settings.json"); byte[] previous = File.Exists(settings) ? File.ReadAllBytes(settings) : null;
            try
            {
                foreach (string language in new[] { "zh-CN", "en" })
                foreach (float scale in new[] { 1f, 1.5f, 2f })
                using (MainForm main = new MainForm())
                {
                    main.GetType().GetField("inputReader", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(main, (Func<InputSnapshot>)(() => new InputSnapshot { Valid = true, Tick = 100, IdleMilliseconds = 10000 }));
                    Field<UserPreferences>(main, "preferences").CheckUpdates = false; UiText.SetLanguage(language);
                    Field<Timer>(main, "timer").Stop(); Field<Timer>(main, "healthTimer").Stop();
                    main.Show(); Call(main, "OpenDemo"); Field<CheckBox>(main, "automatic").Checked = true;
                    main.GetType().GetField("monitoring", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(main, false);
                    Application.DoEvents();
                    main.GetType().GetField("monitoring", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(main, true);
                    main.GetType().GetField("nextMonitor", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(main, DateTime.MaxValue);
                    Field<Timer>(main, "stabilityTimer").Stop();
                    using (Bitmap sample = new Bitmap(Path.Combine(args[0], "assets", "running-reference.png")))
                    {
                        Detection detection = new Recognizer().Classify(sample); Call(main, "UpdatePreview", sample, detection);
                        Require(Field<PictureBox>(main, "preview").Image.Width <= detection.ButtonBounds.Width + 6, "main preview tightly crops the actual blue badge");
                    }
                    Field<MonitorActivity>(main, "activity").Foreground(DateTime.UtcNow);
                    Call(main, "SetStatus", "Codex 正在运行");
                    for (int i = 0; i < 30; i++) Call(main, "RecentProgress", "已检查 · Codex 正在运行");
                    if (scale != 1)
                    {
                        Control[] controls = All(main).Concat(new Control[] { main }).ToArray();
                        Font[] fonts = controls.Select(c => c.Font).ToArray();
                        main.AutoScaleMode = AutoScaleMode.None; main.Scale(new SizeF(scale, scale));
                        for (int i = 0; i < controls.Length; i++) controls[i].Font = new Font(fonts[i].FontFamily, fonts[i].SizeInPoints * scale, fonts[i].Style);
                    }
                    main.PerformLayout(); Application.DoEvents();
                    ProgressHistory history = Field<ProgressHistory>(main, "recentLog");
                    Require(history.Lines.Length >= 30 && history.ScrollBars == ScrollBars.Vertical, "main retains scrollable session progress beyond four entries");
                    Require(history.AtBottom, "new progress follows the bottom by default (line " + history.FirstVisibleLine + " / " + history.Lines.Length + ", scale " + scale + ")");
                    history.Select(0, 5); history.ScrollToCaret(); SendMessage(history.Handle, 0x115, IntPtr.Zero, IntPtr.Zero);
                    SendMessage(history.Handle, 0x20A, new IntPtr(-120 * 65536), IntPtr.Zero);
                    Require(history.FirstVisibleLine > 0, "mouse wheel scrolls the progress history");
                    SendMessage(history.Handle, 0x115, IntPtr.Zero, IntPtr.Zero);
                    int first = history.FirstVisibleLine;
                    Require(!history.AtBottom, "reader can scroll back through older entries");
                    Call(main, "RecentProgress", "已检查 · Codex 正在运行");
                    Require(history.FirstVisibleLine == first && history.SelectionStart == 0 && history.SelectionLength == 5, "new progress preserves the reader viewport and text selection");
                    Require(All(main).OfType<Label>().Where(c => c.Text == "Target" || c.Text == "Mode").All(c => c.Width >= TextRenderer.MeasureText(c.Text, c.Font).Width), "English connection labels fit without wrapping");
                    Label caption = Field<Label>(main, "previewCaption");
                    Require(caption.Text == (language == "en" ? "Sample" : "采样图") && !caption.AutoEllipsis && caption.Width >= TextRenderer.MeasureText(caption.Text, caption.Font).Width, "sample caption fits without ellipsis in " + language + " at " + scale);
                    Require(Field<Label>(main, "recoveryCount").Text.StartsWith(language == "en" ? "Resumed this session:" : "本轮已恢复"), "session counter uses the current language");
                    SendMessage(history.Handle, 0x115, new IntPtr(7), IntPtr.Zero);
                    Call(main, "RecentProgress", "已检查 · Codex 正在运行");
                    Require(history.AtBottom, "returning to the bottom resumes following new progress");
                    Button[] rightActions = All(main).OfType<Button>().ToArray();
                    int[] rights = rightActions.Select(b => main.PointToClient(b.PointToScreen(new Point(b.Width, 0))).X).ToArray();
                    Require(rights.Max() - rights.Min() <= 1, "main action buttons share a right edge at scale " + scale);
                    LinkLabel activity = All(main).OfType<LinkLabel>().Single(c => c.Name == "ActivityLink");
                    Require(activity.Width >= TextRenderer.MeasureText(activity.Text, activity.Font).Width, "activity link fits on one line at scale " + scale);
                    Require(All(main).Where(c => c.Visible).All(c => main.ClientRectangle.Contains(main.PointToClient(c.PointToScreen(Point.Empty))) && main.ClientRectangle.Contains(main.PointToClient(c.PointToScreen(new Point(Math.Max(0, c.Width - 1), Math.Max(0, c.Height - 1)))))), "visible main controls stay inside client bounds at scale " + scale);
                    Require(All(main).Where(c => c.Visible).All(c => c.Parent.ClientRectangle.Contains(c.Bounds)), "visible controls fit their immediate parent at scale " + scale);
                    Render(main, "CodexAutoResume-ui-" + language + "-" + (int)(scale * 100));
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
                        Render(main, "CodexAutoResume-question-ui-" + language + "-" + (int)(scale * 100));
                    }
                    if (scale == 1)
                    {
                        Call(main, "ShowSettings"); Form window = Field<Form>(main, "settingsWindow");
                        Require(Object.ReferenceEquals(Call(main, "UpdateDialogOwner"), window), "open settings owns update results");
                        ComboBox selector = All(window).OfType<ComboBox>().Single(c => c.Name == "LanguageSelector");
                        bool monitoring = Field<bool>(main, "monitoring"), automatic = Field<CheckBox>(main, "automatic").Checked;
                        Target target = Field<Target>(main, "target");
                        int line = history.FirstVisibleLine;
                        selector.SelectedIndex = language == "en" ? 0 : 1;
                        Require(Field<Label>(main, "status").Text == (language == "en" ? "等待你的回答" : "Waiting for your answer"), "language switch translates current status immediately");
                        Require(Field<bool>(main, "monitoring") == monitoring && Field<CheckBox>(main, "automatic").Checked == automatic && Object.ReferenceEquals(target, Field<Target>(main, "target")), "language switch preserves monitoring, automatic recovery and target");
                        Require(history.Lines.Length >= 31 && history.FirstVisibleLine == line, "language switch preserves session history and viewport");
                        Require(UserPreferences.Load().Language == (language == "en" ? "zh-CN" : "en"), "language selection persists");
                        selector.SelectedIndex = language == "en" ? 1 : 0;
                        Require(!Field<CheckBox>(main, "background").Text.Contains("实验"), "background mode has no experimental label");
                        Require(All(main).OfType<Label>().All(c => !c.Text.EndsWith("。")), "short main labels omit trailing Chinese full stops");
                        if (language == "zh-CN") Require(Field<Label>(main, "status").Text == "等待你的回答" && All(main).OfType<Button>().Any(c => c.Text == "设置"), "switching back restores Chinese status and controls");
                        Render(window, "CodexAutoResume-settings-ui-" + language); window.Close();
                        Require(window.IsDisposed && Object.ReferenceEquals(Call(main, "UpdateDialogOwner"), main), "closing settings during a check returns update dialogs to the live main window");
                    }
                    using (UpdatePrompt prompt = new UpdatePrompt(new AvailableUpdate { Version = "1.1.11", Tag = "v1.1.11", PublishedAt = DateTime.UtcNow,
                        Notes = "## 中文\n### 改进\n- 最近进度支持滚动查看历史\n- 前后台等待连续 1.5 秒输入空闲再恢复\n- 改善更新提示的信息展示\n## English\n### Improved\n- Scroll through recent progress\n- Wait for 1.5 seconds of input inactivity before resuming\n- Clearer update summaries\n## 已知限制 / Known limitation\n遮挡捕获仍需在实际环境验证\nCovered capture still needs validation in the affected environment" }, "1.1.10"))
                    {
                        prompt.Show(main); Application.DoEvents();
                        if (scale != 1)
                        {
                            Control[] controls = All(prompt).Concat(new Control[] { prompt }).ToArray();
                            Font[] fonts = controls.Select(c => c.Font).ToArray();
                            prompt.AutoScaleMode = AutoScaleMode.None; prompt.Scale(new SizeF(scale, scale));
                            for (int i = 0; i < controls.Length; i++) controls[i].Font = new Font(fonts[i].FontFamily, fonts[i].SizeInPoints * scale, fonts[i].Style);
                        }
                        prompt.PerformLayout(); Application.DoEvents();
                        Require(!All(prompt).OfType<TextBoxBase>().Any(), "update notes use reading controls without text-box selection");
                        Require(Object.ReferenceEquals(prompt.ActiveControl, prompt.Later) && Object.ReferenceEquals(prompt.AcceptButton, prompt.Later), "update prompt focuses Later and does not default Enter to installation");
                        Require(prompt.Summary.Items.Count == 3 && prompt.Summary.Limitation.Length > 0, "update summary selects one language and preserves known issues");
                        Require(All(prompt).OfType<Label>().All(c => !c.Text.StartsWith("#")), "update prompt never exposes Markdown headings");
                        Require(All(prompt).Where(c => c.Visible && c.Parent != prompt.Notes).All(c => c.Parent.ClientRectangle.Contains(c.Bounds)), "fixed update controls stay within their parents at " + scale);
                        Require(prompt.Notes.HorizontalScroll.Maximum <= prompt.Notes.ClientSize.Width || !prompt.Notes.HorizontalScroll.Visible, "update notes wrap without horizontal scrolling at " + scale);
                        Render(prompt, "CodexAutoResume-update-" + language + "-" + (int)(scale * 100));
                        prompt.Later.PerformClick(); Require(prompt.DialogResult == DialogResult.Cancel, "Later closes without starting an update"); prompt.Close();
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
