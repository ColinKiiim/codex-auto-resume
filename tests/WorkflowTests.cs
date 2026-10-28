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
        [STAThread] public static int Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--composer-worker") return ComposerWorker.Run();
            if (args.Length == 1 && args[0] == "--diagnostic-state-worker") return DiagnosticStateWorker.Run();
            Native.SetDpi(); DesktopIdentity.Initialize(); Application.EnableVisualStyles();
            // Tests must never add fixture recoveries or settings to the user's profile.
            string testData = Path.Combine(Path.GetTempPath(), "CAR-workflow-" + Guid.NewGuid().ToString("N"));
            typeof(Portable).GetField("DataDirectory").SetValue(null, testData);
            string settings = Path.Combine(Portable.DataDirectory, "settings.json"); byte[] prior = File.Exists(settings) ? File.ReadAllBytes(settings) : null;
            int code = 1;
            using (var main = new MainForm())
            {
                Field<UserPreferences>(main, "preferences").CheckUpdates = false;
                if (args.Length == 1 && args[0] == "--composer-boundary")
                {
                    main.Shown += async delegate { try { await ComposerBoundary(main); code = 0; } catch (Exception error) { Console.Error.WriteLine(error); } finally { main.Close(); if (prior == null) File.Delete(settings); else File.WriteAllBytes(settings, prior); } };
                    Application.Run(main); return code;
                }
                main.GetType().GetField("inputReader", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(main, (Func<InputSnapshot>)(() => new InputSnapshot { Valid = true, Tick = 100, IdleMilliseconds = 10000 }));
                main.Shown += async delegate
                {
                    try
                    {
                        Call(main, "OpenDemo"); var demo = Field<DemoForm>(main, "demo");
                        var automatic = Field<CheckBox>(main, "automatic"); var background = Field<CheckBox>(main, "background");
                        Require(!automatic.Checked, "ordinary startup observes without automatic clicking");
                        Field<Button>(main, "toggle").PerformClick(); background.Checked = true; automatic.Checked = true;
                        var evidence = (CaptureEvidence)main.GetType().GetProperty("Evidence", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(main, null);
                        evidence.Enable(true);
                        Require(!Field<bool>(main, "monitoring"), "changing settings cannot restart paused monitoring");
                        Field<Button>(main, "toggle").PerformClick();
                        ArmSchedule(main, DateTime.UtcNow.AddSeconds(5));
                        using (var cover = new Form { Bounds = Rectangle.Inflate(demo.Bounds, 20, 20), BackColor = Color.Magenta, TopMost = true, StartPosition = FormStartPosition.Manual })
                        {
                            cover.Show(); cover.Activate();
                            await Task.Delay(2500);
                            Require(Field<ResumeGate>(main, "gate").Attempts == 0, "scheduled time blocks ordinary triangle recovery before the deadline");
                            await Until(() => Field<ResumeGate>(main, "gate").Verified == 1);
                            await evidence.Pending;
                            Require(evidence.LastError == null && Directory.GetFiles(Path.Combine(testData, "logs", "capture-evidence"), "*-frame.png").Length > 0, "covered recovery records owned original frames without changing confirmation");
                            evidence.Enable(false);
                            Require(!Field<ResumeSchedule>(main, "resumeSchedule").Active, "confirmed ordinary recovery consumes the one-shot schedule");
                            var multiple = demo.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<CheckBox>().Single();
                            multiple.Checked = true; await Task.Delay(3500);
                            Require(Field<ResumeGate>(main, "gate").Attempts == 1, "ambiguous buttons never cause another click");
                            multiple.Checked = false;
                            var icon = Field<IconButton>(demo, "icon");
                            var options = Field<UserPreferences>(main, "preferences");
                            options.QuotaControl = true;
                            var snapshot = new UsageSnapshot { AccountKey = "quota-fixture", FetchedUtc = DateTime.UtcNow, Windows = new[] {
                                new UsageWindow { Minutes = 300, Remaining = 5 }, new UsageWindow { Minutes = 10080, Remaining = 80 } } };
                            main.GetType().GetField("usageSnapshot", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(main, snapshot);
                            main.GetType().GetField("usageError", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(main, UsageError.None);
                            int submissions = 0;
                            Func<Target, string, ComposerContext, string, DateTime, System.Threading.CancellationToken, Task<ComposerReply>> composer = (t, op, c, text, expires, token) => {
                                if (op == "submit") { submissions++; return Task.FromResult(new ComposerReply { Result = "Uncertain" }); }
                                return Task.FromResult(new ComposerReply { Result = "Observed", Safe = true, Empty = true, Running = icon.State == "方块",
                                    Context = new ComposerContext { Backend = "fixture", Conversation = "owned-conversation", Editor = "editor" } });
                            };
                            main.GetType().GetField("composerRunner", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(main, composer);
                            await Until(() => Field<QuotaPause>(main, "quotaPause").Phase == QuotaPhase.Waiting);
                            Require(icon.State == "三角形" && Field<int>(demo, "clicks") == 2, "low selected quota clicks the running square once");
                            long reads = Field<MonitorActivity>(main, "activity").Reads;
                            await Task.Delay(3500);
                            Require(Field<int>(demo, "clicks") == 2 && Field<MonitorActivity>(main, "activity").Reads == reads, "quota wait suppresses ordinary resume and continuous screen sampling");
                            ArmSchedule(main, DateTime.UtcNow.AddSeconds(5));
                            snapshot.Windows[0].Remaining = 80; snapshot.FetchedUtc = DateTime.UtcNow; Call(main, "QuotaUsageChanged");
                            await Task.Delay(2000);
                            Require(Field<int>(demo, "clicks") == 2, "healthy quota cannot bypass a future recovery time");
                            await Until(() => Field<ResumeGate>(main, "gate").Verified == 2);
                            Require(icon.State == "方块" && Field<int>(demo, "clicks") == 3, "actual quota recovery resumes the original triangle exactly once");
                            snapshot.Windows[0].Remaining = 5; snapshot.FetchedUtc = DateTime.UtcNow;
                            await Until(() => Field<QuotaPause>(main, "quotaPause").Phase == QuotaPhase.Waiting);
                            icon.State = "向上箭头"; icon.Invalidate();
                            snapshot.Windows[0].Remaining = 80; snapshot.FetchedUtc = DateTime.UtcNow; Call(main, "QuotaUsageChanged");
                            await Until(() => submissions == 1);
                            await Task.Delay(3500);
                            Require(submissions == 1 && Field<QuotaPause>(main, "quotaPause").Phase == QuotaPhase.Uncertain, "unknown input or send outcome never repeats a continuation");
                            Field<Button>(main, "toggle").PerformClick(); automatic.Checked = false; automatic.Checked = true;
                            Require(!Field<bool>(main, "monitoring"), "explicit pause remains paused after option changes");
                        }
                        await TimedQueue(main);
                        await ComposerBoundary(main);
                        code = 0;
                    }
                    catch (Exception error) { Console.Error.WriteLine(error); }
                    finally { main.Close(); if (prior == null) File.Delete(settings); else File.WriteAllBytes(settings, prior); }
                };
                Application.Run(main);
            }
            return code;
        }
        static void ArmSchedule(MainForm main, DateTime due)
        {
            var target = Field<Target>(main, "target");
            Field<ResumeSchedule>(main, "resumeSchedule").Arm(due, target.Window + ":" + target.ProcessId + ":" + target.ProcessStart,
                DateTime.UtcNow, (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency, TimeZoneInfo.Local.Id);
        }
        static async Task TimedQueue(MainForm main)
        {
            var options = Field<UserPreferences>(main, "preferences"); options.LimitScope = false; options.QuotaControl = false;
            Field<CheckBox>(main, "automatic").Checked = true;
            if (!Field<bool>(main, "monitoring")) Field<Button>(main, "toggle").PerformClick();
            Call(main, "CancelQuota");
            var target = Field<Target>(main, "target");
            main.GetType().GetField("usageTargetKey", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(main, UsageSource.TargetKey(target));
            main.GetType().GetField("usageBusy", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(main, true);
            main.GetType().GetField("usageSnapshot", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(main, new UsageSnapshot { AccountKey = "fixture-account", FetchedUtc = DateTime.UtcNow, Windows = new[] { new UsageWindow { Minutes = 300, Remaining = 90 }, new UsageWindow { Minutes = 10080, Remaining = 90 } } });
            main.GetType().GetField("usageError", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(main, UsageError.None);
            main.GetType().GetField("timedLoaded", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(main, true);
            var items = Field<System.Collections.Generic.List<TimedInstruction>>(main, "timedInstructions"); items.Clear();
            var sent = new System.Collections.Generic.List<string>();
            Func<Target, string, ComposerContext, string, DateTime, System.Threading.CancellationToken, DateTime?, Task<ComposerReply>> runner = (t, op, context, prompt, expires, cancel, due) => {
                if (op == "inspect") return Task.FromResult(new ComposerReply { Result = "Observed", Safe = true, Empty = true, Running = false, Context = new ComposerContext { Backend = "fixture", Conversation = "one", Editor = "new-editor", Title = "fixture" } });
                sent.Add(prompt); return Task.FromResult(new ComposerReply { Result = prompt == "不明结果" ? "Uncertain" : "Submitted" });
            };
            main.GetType().GetField("timedRunner", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(main, runner);
            var contextValue = new ComposerContext { Backend = "fixture", Conversation = "one", Editor = "one", Title = "fixture" };
            foreach (string prompt in new[] { "任务一", "任务二", "不明结果" })
            {
                var item = new TimedInstruction { Id = Guid.NewGuid().ToString("N"), Prompt = prompt, DueUtc = DateTime.UtcNow.AddSeconds(-3 + items.Count), Zone = TimeZoneInfo.Local.Id, State = "等待执行", Context = contextValue, AccountKey = TimedInstructionPolicy.AccountFingerprint("fixture-account"), Window = target.Window.ToInt64(), ProcessId = target.ProcessId, ProcessStart = target.ProcessStart, ProcessPath = target.ProcessPath };
                item.BoundUtc = DateTime.UtcNow; item.BoundClock = (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency; items.Add(item);
            }
            DateTime end = DateTime.UtcNow.AddSeconds(15);
            while (sent.Count < 3 && DateTime.UtcNow < end) { Call(main, "TickTimedInstructions"); await Task.Delay(200); }
            Require(sent.SequenceEqual(new[] { "任务一", "任务二", "不明结果" }), "multiple timed instructions use their own prompts in deadline order");
            Require(items[0].State == "已完成" && items[1].State == "已完成" && items[2].State == "结果不明，请核对", "submitted tasks finish and uncertain submission is reserved");
            for (int i = 0; i < 6; i++) { Call(main, "TickTimedInstructions"); await Task.Delay(200); }
            Require(sent.Count == 3, "uncertain timed submission is not repeated");
            main.GetType().GetField("timedRunner", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(main, null);
            main.GetType().GetField("usageBusy", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(main, false);
            items.Clear(); main.GetType().GetMethod("Stop", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(main, new object[] { "timed fixture complete" });
        }
        static async Task ComposerBoundary(MainForm main)
        {
            main.GetType().GetMethod("Stop", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(main, new object[] { "composer fixture" });
            // A real cross-process UIA boundary, using only our own fixture.
            // No text or message is sent to a user's Codex conversation.
            using (var fixture = new Form { Text = "CAR conversation fixture", ClientSize = new Size(640, 400), StartPosition = FormStartPosition.CenterScreen })
            using (var cover = new Form { Text = "CAR composer cover", TopMost = false, StartPosition = FormStartPosition.CenterScreen, ClientSize = new Size(700, 450) })
            {
                var threads = new ListBox { Dock = DockStyle.Left, Width = 180 }; threads.Items.Add("CAR conversation fixture"); threads.SelectedIndex = 0;
                var composer = new Panel { Dock = DockStyle.Bottom, Height = 100 };
                var editor = new TextBox { Multiline = true, Width = 300, Height = 70, Location = new Point(10, 5) };
                var send = new Button { Text = "Send", Location = new Point(320, 5) };
                var stop = new Button { Text = "Stop", Location = new Point(320, 40), Visible = false };
                int count = 0; send.Click += delegate { string text = editor.Text; count++; fixture.Controls.Add(new Label { Text = text, AutoSize = true, Location = new Point(200, 120) }); editor.Clear(); stop.Visible = true; };
                composer.Controls.Add(editor); composer.Controls.Add(send); composer.Controls.Add(stop); fixture.Controls.Add(composer); fixture.Controls.Add(threads);
                fixture.Show(); cover.Show(); cover.Activate(); await Task.Delay(1800);
                using (var process = System.Diagnostics.Process.GetCurrentProcess())
                {
                    var target = new Target { Window = fixture.Handle, ProcessId = process.Id, ProcessStart = process.StartTime.ToUniversalTime().Ticks, ProcessPath = process.MainModule.FileName };
                    editor.Text = "private diagnostic sentinel";
                    string diagnostic = await DiagnosticStateWorker.ReadAsync(target, System.Threading.CancellationToken.None);
                    Require(diagnostic.Contains("\"result\":\"observed\"") && diagnostic.Contains("\"diagnosticOnly\":true") && !diagnostic.Contains("private diagnostic sentinel") && editor.Text == "private diagnostic sentinel" && count == 0, "read-only diagnostic helper observes owned controls without returning drafts or performing actions");
                    editor.Clear();
                    var observed = await ComposerBridge.RunAsync(target, "inspect", null, "继续工作", DateTime.UtcNow.AddSeconds(25), System.Threading.CancellationToken.None);
                    Require(observed.Result == "Observed" && observed.Context != null && observed.Safe && !observed.Running && observed.Empty, "owned UIA helper binds the selected conversation and safe idle editor; stage=" + observed.Stage);
                    var early = await ComposerBridge.RunAsync(target, "submit", observed.Context, "继续工作", DateTime.UtcNow.AddSeconds(25), System.Threading.CancellationToken.None, DateTime.UtcNow.AddHours(1));
                    Require(early.Result == "Blocked" && editor.Text == "" && count == 0, "worker independently rejects submission before the scheduled time");
                    editor.Text = "existing draft";
                    var blocked = await ComposerBridge.RunAsync(target, "submit", observed.Context, "继续工作", DateTime.UtcNow.AddSeconds(25), System.Threading.CancellationToken.None);
                    Require(blocked.Result == "Blocked" && editor.Text == "existing draft" && count == 0, "existing draft is neither overwritten nor sent");
                    editor.Clear(); await Task.Delay(1800);
                    var submitted = await ComposerBridge.RunAsync(target, "submit", observed.Context, "继续工作", DateTime.UtcNow.AddSeconds(25), System.Threading.CancellationToken.None);
                    if (submitted.Result == "Submitted")
                        Require(count == 1 && editor.Text == "" && Native.GetForegroundWindow() == fixture.Handle, "UIA foreground fallback sends once and confirms a new fixture message plus running");
                    else
                    {
                        // Real input protection remains enabled in the helper.
                        // Running this check while the user works must abort,
                        // rather than forcing the desktop into an idle state.
                        bool guarded = submitted.Stage == "focus:ComposerUnavailable:activation-denied" || submitted.Stage == "inspect:ComposerUnavailable:input-or-target-changed" || submitted.Stage == "focus:ComposerUnavailable:focus-postflight" || submitted.Stage == "write-preflight:ComposerUnavailable:value-pattern-unavailable";
                        Require(submitted.Result == "Blocked" && guarded && count == 0 && editor.Text == "", "input activity, activation denial or unavailable ValuePattern aborts before input or send; result=" + submitted.Result + "; stage=" + submitted.Stage + "; fixture-messages=" + count + "; draft-length=" + editor.Text.Length);
                        Console.WriteLine("LIMITATION: successful foreground submission was not exercised; blocked by " + submitted.Stage);
                    }
                    int previous = count;
                    stop.Visible = false; fixture.Text = "different conversation";
                    var changed = await ComposerBridge.RunAsync(target, "submit", observed.Context, "继续工作", DateTime.UtcNow.AddSeconds(25), System.Threading.CancellationToken.None);
                    Require(changed.Result == "Blocked" && count == previous, "conversation change invalidates the saved input transaction");
                }
            }
        }
    }
}
