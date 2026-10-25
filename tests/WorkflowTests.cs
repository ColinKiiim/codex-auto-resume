using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexAutoResume
{
    internal static class WorkflowTests
    {
        static T Field<T>(object instance, string name) { return (T)instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(instance); }
        static Button Button(MainForm form, string text) { return form.Controls.OfType<TableLayoutPanel>().Single().Controls.OfType<FlowLayoutPanel>().SelectMany(p => p.Controls.OfType<Button>()).Single(b => b.Text == text); }
        static void Require(bool ok, string text) { if (!ok) throw new Exception(text); Console.WriteLine("PASS: " + text); }
        static int Heartbeats(MainForm main) { return Field<TextBox>(main, "logBox").Text.Split(new[] { "心跳：" }, StringSplitOptions.None).Length - 1; }
        static async Task Until(Func<bool> condition, int seconds)
        { DateTime deadline = DateTime.UtcNow.AddSeconds(seconds); while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(100); Require(condition(), "GUI reached expected state within " + seconds + " seconds"); }
        [STAThread] public static int Main()
        {
            Native.SetDpi(); DesktopIdentity.Initialize(); Application.EnableVisualStyles();
            int code = 1;
            using (MainForm main = new MainForm())
            {
                main.Shown += async delegate
                {
                    try
                    {
                        Button(main, "打开演示").PerformClick();
                        DemoForm demo = Field<DemoForm>(main, "demo");
                        CheckBox automatic = Field<CheckBox>(main, "automatic"), background = Field<CheckBox>(main, "background");
                        Require(!automatic.Checked && Field<bool>(main, "monitoring"), "startup observes by default without a shortcut");
                        Require(Target.FindAvailable(true).Any(t => t.Window == demo.Handle), "automatic discovery finds owned demo");
                        using (WindowPicker picker = new WindowPicker(Target.FindAvailable(true))) Require(picker.SelectedIndex == -1, "window choice never has an implicit default");
                        Button(main, "停止").PerformClick();
                        Require(!Field<bool>(main, "monitoring"), "manual stop halts monitoring");
                        background.Checked = true; automatic.Checked = true;
                        Require(Field<bool>(main, "monitoring") && Field<Target>(main, "target").Window == demo.Handle, "checkbox starts background mode and preserves connection without hotkeys");
                        using (Form cover = new Form { Text = "CodexAutoResume workflow cover", Bounds = Rectangle.Inflate(demo.Bounds, 20, 20), BackColor = Color.Magenta, TopMost = true, StartPosition = FormStartPosition.Manual })
                        {
                            cover.Show(); cover.Activate();
                            await Until(() => Field<ResumeGate>(main, "gate").Verified == 1, 12);
                            Require(demo.Controls.OfType<Label>().Any(l => l.Text == "按钮收到的点击：1"), "GUI automatically clicks occluded triangle once");
                            IconButton icon = demo.Controls.OfType<IconButton>().Single(c => c.Visible);
                            CheckBox multiple = demo.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<CheckBox>().Single();
                            multiple.Checked = true;
                            await Until(() => Field<Target>(main, "target").CandidateCount == 2 && Field<Label>(main, "status").Text.Contains("多个候选"), 8);
                            long reads = Field<MonitorActivity>(main, "activity").Reads;
                            await Task.Delay(1800);
                            Require(Field<bool>(main, "monitoring") && Field<MonitorActivity>(main, "activity").Reads > reads && !Application.OpenForms.OfType<RegionPicker>().Any(), "transient multiple candidates keep scanning without an automatic blocking dialog");
                            Require(Field<ResumeGate>(main, "gate").Attempts == 1, "ambiguous candidates cause no extra click");
                            multiple.Checked = false; icon.State = "三角形"; icon.Invalidate();
                            await Until(() => Field<ResumeGate>(main, "gate").Verified == 2, 12);
                            Require(Field<CheckBox>(main, "automatic").Checked && Field<CheckBox>(main, "background").Checked, "return to single candidate preserves background automatic-click mode");
                            multiple.Checked = true;
                            await Until(() => Field<Target>(main, "target").CandidateCount == 2, 8);
                            Button(main, "选择区域").PerformClick();
                            await Until(() => Application.OpenForms.OfType<RegionPicker>().Any(), 8);
                            RegionPicker chooser = Application.OpenForms.OfType<RegionPicker>().Single(); chooser.DialogResult = DialogResult.Cancel; chooser.Close();
                            await Task.Delay(600);
                            Require(Field<bool>(main, "monitoring") && Field<Target>(main, "target") != null, "cancelling manual region choice preserves target and monitoring");
                            multiple.Checked = false; icon.State = "三角形"; icon.Invalidate();
                            await Until(() => Field<ResumeGate>(main, "gate").Verified == 3, 12);
                            int beats = Heartbeats(main);
                            Field<MonitorPulse>(main, "uiPulse").Reset(DateTime.UtcNow.AddSeconds(-61));
                            await Until(() => Heartbeats(main) == beats + 1, 4);
                            await Task.Delay(1200); Require(Heartbeats(main) == beats + 1, "unchanged icon generates one scheduled heartbeat instead of per-tick messages");
                            ComboBox interval = Field<ComboBox>(main, "heartbeatInterval"); interval.SelectedIndex = 1;
                            Field<MonitorPulse>(main, "uiPulse").Reset(DateTime.UtcNow.AddSeconds(-61)); await Task.Delay(1300);
                            Require(Heartbeats(main) == beats + 1, "five-minute UI option suppresses one-minute heartbeat");
                            Field<MonitorPulse>(main, "uiPulse").Reset(DateTime.UtcNow.AddSeconds(-301));
                            await Until(() => Heartbeats(main) == beats + 2, 4);
                            Field<MonitorPulse>(main, "filePulse").Reset(DateTime.UtcNow.AddSeconds(-11));
                            await Task.Delay(1200);
                            string detailed = System.IO.File.ReadAllText(System.IO.Path.Combine(Portable.DataDirectory, "logs", "diagnostics.log"));
                            Require(detailed.Contains("last-content-utc=") && detailed.Contains("native-worker-busy="), "background log exposes native frame time and pending capture status");
                            icon.State = "未知符号"; icon.Invalidate();
                            await Task.Delay(9000);
                            Require(Field<bool>(main, "monitoring") && Field<ResumeGate>(main, "gate").Attempts == 3, "nine seconds of unknown state neither stops nor clicks");
                            // Restore a triangle after the old timeout horizon.
                            icon.State = "三角形"; icon.Invalidate();
                            await Until(() => Field<ResumeGate>(main, "gate").Verified == 4, 10);
                            Require(demo.Controls.OfType<Label>().Any(l => l.Text == "按钮收到的点击：4"), "GUI resumes after long unknown period without rebind");
                            using (Bitmap question = new Bitmap(1600, 1184))
                            using (Graphics g = Graphics.FromImage(question))
                            using (Font font = new Font("Microsoft YaHei UI", 18))
                            using (PictureBox panel = new PictureBox { Dock = DockStyle.Fill, Image = question })
                            {
                                g.Clear(Color.FromArgb(24,24,24)); g.FillRectangle(Brushes.DimGray, 545, 823, 920, 338);
                                g.DrawString("正在询问问题", font, Brushes.White, 575, 690);
                                g.DrawString("正在等待你的回答", font, Brushes.White, 575, 745);
                                g.DrawString("测试问题", font, Brushes.White, 565, 840);
                                g.DrawString("3 of 3", font, Brushes.White, 1330, 840);
                                g.DrawString("提交", font, Brushes.White, 1390, 1110); g.Flush();
                                demo.ClientSize = question.Size; demo.Location = new Point(20,20);
                                demo.Controls.Add(panel); panel.BringToFront(); cover.Bounds = Rectangle.Inflate(demo.Bounds, 20, 20);
                                await Until(() => Field<Label>(main, "scores").Text.StartsWith("等待用户回答"), 15);
                                string questionLog = Field<TextBox>(main, "logBox").Text;
                                await Task.Delay(4600);
                            Require(Field<ResumeGate>(main, "gate").Attempts == 4 && Field<bool>(main, "monitoring"), "question panel keeps monitoring without clicking submit");
                                Require(Field<TextBox>(main, "logBox").Text == questionLog, "unchanged waiting state is logged once");
                                demo.Controls.Remove(panel); panel.Image = null;
                            }
                            demo.ClientSize = new Size(760,420); cover.Bounds = Rectangle.Inflate(demo.Bounds, 20,20);
                            icon.State = "三角形"; icon.Invalidate();
                            await Until(() => Field<ResumeGate>(main, "gate").Verified == 5, 15);
                            Require(Field<TextBox>(main, "logBox").Text.Contains("问答面板已消失"), "question disappearance returns automatically to icon detection");
                            Button(main, "停止").PerformClick();
                            icon.State = "三角形"; icon.Invalidate(); await Task.Delay(1800);
                            Require(Field<ResumeGate>(main, "gate").Attempts == 5 && !Field<bool>(main, "monitoring"), "explicit stop remains stopped");
                            automatic.Checked = false; automatic.Checked = true;
                            Require(Field<bool>(main, "monitoring"), "checking automatic click restarts after manual stop");
                            Button(main, "停止").PerformClick();
                            using (Bitmap ui = new Bitmap(main.Width, main.Height)) { main.DrawToBitmap(ui, new Rectangle(Point.Empty, ui.Size)); ui.Save(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CodexAutoResume-workflow-ui.png")); }
                            cover.Close();
                        }
                        Console.WriteLine("PASS: workflow uses no keyboard shortcuts or physical mouse input"); code = 0;
                    }
                    catch (Exception e) { Console.Error.WriteLine(e); }
                    finally { main.Close(); }
                };
                Application.Run(main);
            }
            return code;
        }
    }
}
