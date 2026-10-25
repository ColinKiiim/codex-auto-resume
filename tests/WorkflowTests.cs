using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexAutoResume
{
    internal static class WorkflowTests
    {
        static T Field<T>(object value, string name) { return (T)value.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(value); }
        static void Call(MainForm form, string method) { form.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, null); }
        static void Require(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS: " + name); }
        static async Task Until(Func<bool> condition)
        { DateTime deadline = DateTime.UtcNow.AddSeconds(15); while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(100); Require(condition(), "covered resume is confirmed"); }
        [STAThread] public static int Main()
        {
            Native.SetDpi(); DesktopIdentity.Initialize(); Application.EnableVisualStyles();
            string settings = Path.Combine(Portable.DataDirectory, "settings.json"); byte[] prior = File.Exists(settings) ? File.ReadAllBytes(settings) : null;
            int code = 1;
            using (var main = new MainForm())
            {
                Field<UserPreferences>(main, "preferences").CheckUpdates = false;
                main.GetType().GetField("inputReader", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(main, (Func<InputSnapshot>)(() => new InputSnapshot { Valid = true, Tick = 100, IdleMilliseconds = 10000 }));
                main.Shown += async delegate
                {
                    try
                    {
                        Call(main, "OpenDemo"); var demo = Field<DemoForm>(main, "demo");
                        var automatic = Field<CheckBox>(main, "automatic"); var background = Field<CheckBox>(main, "background");
                        Require(!automatic.Checked, "ordinary startup observes without automatic clicking");
                        Field<Button>(main, "toggle").PerformClick(); background.Checked = true; automatic.Checked = true;
                        Require(!Field<bool>(main, "monitoring"), "changing settings cannot restart paused monitoring");
                        Field<Button>(main, "toggle").PerformClick();
                        using (var cover = new Form { Bounds = Rectangle.Inflate(demo.Bounds, 20, 20), BackColor = Color.Magenta, TopMost = true, StartPosition = FormStartPosition.Manual })
                        {
                            cover.Show(); cover.Activate();
                            await Until(() => Field<ResumeGate>(main, "gate").Verified == 1);
                            var multiple = demo.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<CheckBox>().Single();
                            multiple.Checked = true; await Task.Delay(3500);
                            Require(Field<ResumeGate>(main, "gate").Attempts == 1, "ambiguous buttons never cause another click");
                            Field<Button>(main, "toggle").PerformClick(); automatic.Checked = false; automatic.Checked = true;
                            Require(!Field<bool>(main, "monitoring"), "explicit pause remains paused after option changes");
                        }
                        code = 0;
                    }
                    catch (Exception error) { Console.Error.WriteLine(error); }
                    finally { main.Close(); if (prior == null) File.Delete(settings); else File.WriteAllBytes(settings, prior); }
                };
                Application.Run(main);
            }
            return code;
        }
    }
}
