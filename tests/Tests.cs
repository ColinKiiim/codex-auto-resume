using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;

namespace CodexAutoResume
{
    internal static class Tests
    {
        static int assertions, failures;
        static void Check(bool ok, string name)
        { assertions++; if (!ok) { failures++; Console.WriteLine("FAIL: " + name); } }
        static void Group(string name, Action action)
        { int start = assertions; action(); Console.WriteLine(name + ": " + (assertions - start) + " assertions"); }
        [STAThread] public static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--usage-fake") return UsageTests.Serve(args);
            string root = args.Length > 0 ? args[0] : Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
            try
            {
                Group("Recognition and false-click protection", () => Recognition(root));
                Group("Resume gating and input activity", Recovery);
                Group("Capture evidence and cancellation", Evidence);
                Group("Recent diagnostic export and bounded evidence", () => CaptureDiagnosticTests.Run(Check));
                Group("Update integrity and rollback", () => UpdateTests.Run(Check, root));
                Group("Current-login usage protocol", () => UsageTests.Run(Check));
                Group("Quota pause safety", Quota);
                Group("Scheduled recovery safety", Schedule);
                Group("Scoped actions and timed instructions", ScopedInstructions);
                Group("Semantic context and state boundaries", SemanticControls);
            }
            catch (Exception error) { failures++; Console.Error.WriteLine(error); }
            string result = "Assertions: " + assertions + "; failures: " + failures;
            Console.WriteLine(result); File.WriteAllText(Path.Combine(root, "tests", "last-result.txt"), result + Environment.NewLine);
            return failures == 0 ? 0 : 1;
        }
        static void SemanticControls()
        {
            Check(AccessibleComposer.NamedState("恢复") == ButtonState.Resume && AccessibleComposer.NamedState("停止") == ButtonState.Running, "exact composer state labels");
            Check(AccessibleComposer.NamedState("恢复默认设置") == ButtonState.Unknown && AccessibleComposer.NamedState("Retry settings") == ButtonState.Unknown, "unrelated action labels remain unknown");
            var a = new ComposerContext { Backend = "CDP", Conversation = "chat", Title = "title", Port = 9000, PageId = "page", NativeBinding = "native", Editor = "old" };
            var b = new ComposerContext { Backend = "CDP", Conversation = "chat", Title = "title", Port = 9000, PageId = "page", NativeBinding = "native", Editor = "new" };
            Check(ComposerContext.SameConversation(a, b), "readonly editor replacement preserves conversation identity");
            b.PageId = "other"; Check(!ComposerContext.SameConversation(a, b), "another page is another binding");
            b.PageId = a.PageId; b.NativeBinding = "other"; Check(!ComposerContext.SameConversation(a, b), "another native conversation is another binding");
            b.NativeBinding = a.NativeBinding; b.PageId = "replacement";
            Check(ComposerContext.SameObservedConversation(a, b) && !ComposerContext.SameConversation(a, b), "readonly rediscovery can recognize the same chat on a new page without authorizing an old action context");
            var native = new ComposerContext { Backend = "UIA", Conversation = "native-chat", Title = "title", NativeBinding = "native", NativeConversation = "native-chat" };
            a.NativeConversation = "native-chat";
            Check(ComposerContext.SameObservedConversation(a, native) && !ComposerContext.SameConversation(a, native), "cross-backend observations require a shared independent native chat proof");
            native.NativeConversation = "same-title-other-chat";
            Check(!ComposerContext.SameObservedConversation(a, native), "same title alone cannot bridge another chat");
            native.NativeConversation = null;
            Check(!ComposerContext.SameObservedConversation(a, native), "missing native proof cannot authorize a cross-backend rebind");
            var gate = new ResumeGate(); DateTime now = DateTime.UtcNow; gate.MarkAttempt(now); gate.CancelPending(); gate.ObserveCaptured(ButtonState.Running, now.AddSeconds(1), now.AddSeconds(1));
            Check(gate.Attempts == 1 && gate.Verified == 0 && !gate.AwaitingRunning, "a new conversation cannot confirm the previous conversation's action");
            var activity = new MonitorActivity(); activity.Start(now); activity.Control(now);
            Check(activity.NewContent == 0 && activity.LastContent == DateTime.MinValue && activity.LastSample == now, "control observations do not fabricate system image timestamps");
        }
        static void Recognition(string root)
        {
            var recognizer = new Recognizer();
            foreach (string name in new[] { "resume", "running", "send", "send-current", "voice", "voice-close" })
            using (var sample = new Bitmap(Path.Combine(root, "assets", name + "-reference.png")))
            {
                var expected = name == "resume" ? ButtonState.Resume : name == "running" ? ButtonState.Running : name == "voice" ? ButtonState.Voice : name == "voice-close" ? ButtonState.VoiceClose : ButtonState.Send;
                Check(recognizer.Classify(sample).State == expected, name + " real sample");
                using (var scaled = new Bitmap(126, 126)) using (var graphics = Graphics.FromImage(scaled))
                {
                    graphics.Clear(Color.FromArgb(35, 35, 35)); graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    int width = (int)Math.Round(sample.Width * 1.5), height = (int)Math.Round(sample.Height * 1.5);
                    graphics.DrawImage(sample, new Rectangle((126 - width) / 2, (126 - height) / 2, width, height));
                    Check(recognizer.Classify(scaled).State == expected, name + " representative scaled sample");
                }
            }
            // A few regression examples replace the former font/weight/0–99 matrix.
            foreach (int number in new[] { 1, 11, 88 })
            using (var image = new Bitmap(64, 64)) using (var graphics = Graphics.FromImage(image))
            using (var font = new Font("Segoe UI", number < 10 ? 15 : 12, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var blue = new SolidBrush(Color.FromArgb(55, 125, 245)))
            {
                graphics.Clear(Color.FromArgb(24, 24, 24)); graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.FillEllipse(blue, 14, 14, 36, 36); graphics.DrawString(number.ToString(), font, Brushes.White, new RectangleF(14, 14, 36, 36), format);
                Check(recognizer.Classify(image).State == ButtonState.Unknown && recognizer.FindButtons(image).Count == 0, "citation " + number + " cannot become a click candidate");
            }
            Check(QuestionDetector.Matches("正在等待你的回答", "提交") && QuestionDetector.Matches("Waiting for your answer", "Submit"), "question context is recognized in both languages");
            Check(!QuestionDetector.Matches("普通输入框", "发送") && !QuestionDetector.Matches("", "继续"), "ordinary send/continue text is not a question");
            using (var image = new Bitmap(450, 160)) using (var graphics = Graphics.FromImage(image))
            {
                graphics.Clear(Color.FromArgb(24, 24, 24));
                IconButton.Draw(graphics, new Rectangle(40, 50, 64, 64), "三角形");
                IconButton.Draw(graphics, new Rectangle(300, 50, 64, 64), "三角形");
                var buttons = recognizer.FindButtons(image);
                Check(buttons.Count == 2 && new RegionSelection().Resolve(buttons, image.Size) == -1, "ambiguous buttons require explicit selection");
            }
            Check(!Target.IsCodexIdentity("OtherApp", @"C:\Other.exe", "OpenAI", "Codex") && !Target.IsCodexIdentity("CodexAutoResume", @"C:\CodexAutoResume.exe", "OpenAI", "Codex"), "unrelated process and helper cannot become targets");
            using (var image = new Bitmap(240, 100)) using (var graphics = Graphics.FromImage(image))
            using (var close = new Bitmap(Path.Combine(root, "assets", "voice-close-reference.png")))
            {
                graphics.Clear(Color.FromArgb(24, 24, 24));
                graphics.DrawImageUnscaled(close, 15, 20);
                IconButton.Draw(graphics, new Rectangle(150, 20, 64, 64), "三角形");
                var buttons = recognizer.FindButtons(image);
                Check(buttons.Count == 1 && buttons[0].Detection.State == ButtonState.Resume, "auxiliary voice close cannot block a unique resume button");
            }
        }
        static void Quota()
        {
            DateTime now = DateTime.UtcNow;
            var options = new UserPreferences { QuotaControl = true, QuotaThreshold = 10 };
            var five = new UsageWindow { Minutes = 300, Remaining = 10 };
            var week = new UsageWindow { Minutes = 10080, Remaining = 80 };
            var snapshot = new UsageSnapshot { AccountKey = "fixture", FetchedUtc = now, Windows = new[] { five, week } };
            Check(QuotaPolicy.Evaluate(snapshot, UsageError.None, options, now) == QuotaLevel.Low, "equal threshold pauses with OR logic");
            five.Remaining = 80; week.Remaining = 2;
            Check(QuotaPolicy.Evaluate(snapshot, UsageError.None, options, now) == QuotaLevel.Low, "weekly alone can trigger pause");
            options.QuotaWeekly = false;
            Check(QuotaPolicy.Evaluate(snapshot, UsageError.None, options, now) == QuotaLevel.Healthy, "unselected low window cannot block single-period recovery");
            options.QuotaWeekly = true; week.Remaining = null;
            Check(QuotaPolicy.Evaluate(snapshot, UsageError.None, options, now) == QuotaLevel.Unknown, "missing selected quota cannot resume");
            five.Remaining = 3;
            Check(QuotaPolicy.Evaluate(snapshot, UsageError.None, options, now) == QuotaLevel.Low, "one known low period can pause even when another is unknown");
            five.Remaining = week.Remaining = 80;
            Check(QuotaPolicy.Evaluate(snapshot, UsageError.None, options, now) == QuotaLevel.Healthy, "all selected periods recovered, independent of old reset countdown");
            Check(QuotaPolicy.Evaluate(snapshot, UsageError.Timeout, options, now) == QuotaLevel.Unknown && QuotaPolicy.Evaluate(snapshot, UsageError.None, options, now.AddSeconds(31)) == QuotaLevel.Unknown, "failed or expired quota cannot authorize an action");
            var pause = new QuotaPause(); pause.StopSent(now);
            pause.Observe(ButtonState.Resume, now.AddSeconds(-1), now.AddSeconds(1));
            Check(pause.Phase == QuotaPhase.Stopping, "cached triangle cannot confirm a new stop");
            pause.Observe(ButtonState.Resume, now.AddSeconds(1), now.AddSeconds(1));
            Check(pause.Phase == QuotaPhase.Waiting && !pause.Due(QuotaLevel.Low, now.AddSeconds(59)) && pause.Due(QuotaLevel.Low, now.AddSeconds(61)), "quota wait samples once per minute");
            Check(pause.Due(QuotaLevel.Healthy, now.AddSeconds(2)), "actual quota recovery bypasses minute screen wait");
            pause.Observe(ButtonState.Running, now.AddSeconds(2), now.AddSeconds(2));
            Check(!pause.Owned && pause.ManualOverride, "manual continuation cancels owned pause without fighting the user");
            pause.StopSent(now); pause.Observe(ButtonState.Unknown, now.AddSeconds(-1), now.AddSeconds(16));
            Check(pause.Phase == QuotaPhase.Uncertain, "stop confirmation timeout never allows repeat action, even with stale content");
            Check(ComposerRequest.ValidPrompt("继续\n下一步") && !ComposerRequest.ValidPrompt("\t") && !ComposerRequest.ValidPrompt("继续\r\n下一步"), "custom prompt accepts validated LF text and rejects control sequences");
            var request = new ComposerRequest { Prompt = "继续\n下一步", ProcessPath = @"C:\中文路径\Codex.exe", ExpiresUtc = now };
            var copied = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<ComposerRequest>(ComposerBridge.RequestText(request));
            Check(copied.Prompt == request.Prompt && copied.ProcessPath == request.ProcessPath && Math.Abs((copied.ExpiresUtc.ToUniversalTime() - now).TotalMilliseconds) < 1, "worker wire preserves Chinese, multiline input and the UTC action deadline across code pages");
        }
        static void Schedule()
        {
            DateTime now = DateTime.UtcNow;
            var plan = new ResumeSchedule();
            plan.Arm(now.AddMinutes(1), "original", now, 100, "zone");
            Check(!plan.Allows(now.AddSeconds(30), 130, "original", "zone") && plan.Allows(now.AddMinutes(1), 160, "original", "zone"), "schedule blocks before deadline and allows at deadline without an early action");
            plan.Observe(ButtonState.Resume, now.AddSeconds(-2)); plan.Observe(ButtonState.Running, now.AddSeconds(-1));
            Check(plan.Active, "evidence predating schedule creation cannot consume the new plan");
            plan.Observe(ButtonState.Resume, now); plan.Observe(ButtonState.Running, now.AddSeconds(-1));
            Check(plan.Active, "stale running evidence cannot consume a scheduled recovery");
            plan.Observe(ButtonState.Running, now.AddSeconds(1));
            Check(!plan.Active, "fresh running confirmation consumes the one-shot schedule");
            plan.Arm(now.AddMinutes(1), "original", now, 100, "zone");
            Check(!plan.Allows(now.AddMinutes(1), 160, "other", "zone") && plan.Invalid, "switching target never transfers scheduled permission");
            plan.Arm(now.AddMinutes(1), "original", now, 100, "zone");
            Check(!plan.Allows(now.AddHours(1), 101, "original", "zone") && plan.Invalid, "forward wall-clock jump cannot authorize early recovery");
            plan.Arm(now.AddMinutes(1), "original", now, 100, "zone");
            Check(plan.Allows(now.AddHours(1), 3700, "original", "zone"), "elapsed sleep across deadline allows fresh recovery checks");
            Check(!plan.Allows(now.AddHours(1), 3700, "original", "other-zone"), "timezone changes invalidate armed intent");
            plan.Cancel(); Check(!plan.Active && !new ResumeSchedule().Active, "cancellation and a new process never replay a prior schedule");
            bool rejected = false;
            try { plan.Arm(now, "original", now, 100, "zone"); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "past or current deadlines cannot silently arm");
            var json = new System.Web.Script.Serialization.JavaScriptSerializer();
            var old = json.Deserialize<UserPreferences>("{\"QuotaThreshold\":5,\"QuotaFiveHour\":false,\"QuotaWeekly\":true}");
            Check(old.FiveHourThreshold == 5 && old.WeeklyThreshold == 5 && !old.QuotaFiveHour && old.QuotaWeekly, "legacy shared threshold and selection migrate without changing behavior");
            old.QuotaFiveHourThreshold = 5; old.QuotaWeeklyThreshold = 15;
            var saved = json.Deserialize<UserPreferences>(json.Serialize(old));
            Check(saved.FiveHourThreshold == 5 && saved.WeeklyThreshold == 15, "independent threshold values persist without overwriting each other");
            var five = new UsageWindow { Minutes = 300, Remaining = 6 };
            var week = new UsageWindow { Minutes = 10080, Remaining = 15 };
            var usage = new UsageSnapshot { FetchedUtc = now, Windows = new[] { five, week } };
            saved.QuotaFiveHour = true;
            Check(QuotaPolicy.Evaluate(usage, UsageError.None, saved, now) == QuotaLevel.Low, "weekly equality uses its own threshold when 5h is healthy");
            week.Remaining = 16;
            Check(QuotaPolicy.Evaluate(usage, UsageError.None, saved, now) == QuotaLevel.Healthy, "both selected periods must exceed their individual thresholds");
            five.Remaining = 5;
            Check(QuotaPolicy.Evaluate(usage, UsageError.None, saved, now) == QuotaLevel.Low, "5h equality uses its own threshold");
            saved.QuotaFiveHour = false;
            Check(QuotaPolicy.Evaluate(usage, UsageError.None, saved, now) == QuotaLevel.Healthy, "disabled low 5h cannot block weekly-only recovery");
        }
        static void ScopedInstructions()
        {
            var project = new ScopeEntry { Kind = "project", Id = "p1", Title = "Project" };
            var chat = new ScopeEntry { Kind = "chat", Id = "c1", ProjectId = "p1", Title = "Chat", Project = "Project" };
            var view = new ScopeReply { Result = "Observed", ProjectId = "p1", CurrentId = "c1", Entries = new System.Collections.Generic.List<ScopeEntry> { project, chat } };
            Check(ScopePolicy.Allows(true, false, project, view) && ScopePolicy.Allows(true, false, chat, view), "same project and selected chat are allowed only with matching evidence");
            Check(ScopePolicy.AllowsEntry(true, false, project, chat) && ScopePolicy.AllowsEntry(true, false, chat, chat), "explicit tasks can navigate only to an allowed bound entry");
            Check(!ScopePolicy.AllowsEntry(true, false, project, new ScopeEntry { Kind = "chat", Id = "other", ProjectId = "p2", Project = "Other" }), "task navigation cannot escape its selected project");
            project.Ambiguous = true;
            Check(!ScopePolicy.Allows(true, false, project, view) && !ScopePolicy.AllowsEntry(true, false, project, chat), "duplicate-name project binding cannot authorize navigation or actions");
            project.Ambiguous = false;
            view.ProjectId = "p2";
            Check(!ScopePolicy.Allows(true, false, project, view) && !ScopePolicy.Allows(true, false, chat, view), "wrong project cannot authorize a scoped action");
            view.ProjectId = "p1"; view.CurrentId = "c2";
            Check(!ScopePolicy.Allows(true, false, chat, view) && !ScopePolicy.Allows(true, true, project, view), "other chat and uncertain navigation cannot authorize action");
            view.CurrentId = "c1"; var renamed = new ScopeEntry { Kind = "project", Id = "p1", Title = "Different" }; view.Entries[0] = renamed;
            Check(!ScopePolicy.Allows(true, false, project, view), "renamed project cannot reuse a prior binding");
            Check(!ScopePolicy.Allows(true, false, null, view) && !ScopePolicy.Allows(true, false, project, null), "missing binding or evidence fails closed");
            var originalContext = new ComposerContext { Backend = "UIA", Conversation = "chat", Title = "title", Editor = "old" };
            var reboundContext = new ComposerContext { Backend = "UIA", Conversation = "chat", Title = "title", Editor = "new" };
            Check(ComposerContext.SameConversation(originalContext, reboundContext), "read-only preflight may refresh an editor within the same conversation");
            reboundContext.Conversation = "other";
            Check(!ComposerContext.SameConversation(originalContext, reboundContext), "editor refresh never transfers permission to another conversation");
            DateTime bound = DateTime.UtcNow;
            Check(!ScopePolicy.FrameAfterBinding(true, bound.AddTicks(-1), bound) && ScopePolicy.FrameAfterBinding(true, bound, bound), "a captured frame from before scope binding cannot authorize a click");
            Check(TimedInstructionPolicy.OnLoad("执行中") == "结果不明，请核对" && TimedInstructionPolicy.OnLoad("等待执行") == "需重新绑定", "restart never automatically replays an in-flight or pending task");
            Check(TimedInstructionPolicy.AfterReply("Submitted") == "已完成" && TimedInstructionPolicy.AfterReply("Uncertain") == "结果不明，请核对" && !TimedInstructionPolicy.CanRebind("结果不明，请核对"), "unknown submission outcome cannot be silently rebound or repeated");
            DateTime now = DateTime.UtcNow;
            var first = new TimedInstruction { Id = "1", State = "等待条件", DueUtc = now.AddSeconds(-2), NextAttempt = now.AddSeconds(5), Context = new ComposerContext { Conversation = "one" } };
            var second = new TimedInstruction { Id = "2", State = "等待执行", DueUtc = now.AddSeconds(-1), Context = new ComposerContext { Conversation = "one" } };
            Check(TimedInstructionPolicy.NextDue(new[] { first, second }, now) == null, "a later task for the same chat cannot overtake a waiting earlier task");
            second.Context.Conversation = "two";
            Check(TimedInstructionPolicy.NextDue(new[] { first, second }, now) == second, "a blocked chat does not block an independent target queue");
            second.Context.Conversation = "one"; first.State = "结果不明，请核对";
            Check(TimedInstructionPolicy.NextDue(new[] { first, second }, now) == null, "uncertain submission holds later instructions for that target until review");
            var snapshot = new UsageSnapshot { AccountKey = "one", FetchedUtc = now };
            Check(TimedInstructionPolicy.SameAccount(snapshot, UsageError.None, TimedInstructionPolicy.AccountFingerprint("one"), now) && !TimedInstructionPolicy.SameAccount(snapshot, UsageError.None, TimedInstructionPolicy.AccountFingerprint("two"), now) && !TimedInstructionPolicy.SameAccount(snapshot, UsageError.None, TimedInstructionPolicy.AccountFingerprint("one"), now.AddSeconds(31)), "task requires the same fresh account evidence");
        }
        static void Recovery()
        {
            DateTime now = DateTime.UtcNow; var gate = new ResumeGate();
            foreach (var inactive in new[] { ButtonState.Running, ButtonState.Send, ButtonState.Voice, ButtonState.VoiceClose, ButtonState.Unknown })
                Check(!gate.Observe(inactive, now), inactive + " cannot authorize a resume");
            for (int i = 0; i < 3; i++) gate.Observe(ButtonState.Resume, now.AddMilliseconds(i * 300));
            Check(gate.Observe(ButtonState.Resume, now.AddSeconds(1.2)), "stable triangle can reach action preflight");
            gate.MarkAttempt(now.AddSeconds(1.2));
            Check(!gate.Observe(ButtonState.Resume, now.AddSeconds(1.3)), "no repeat click while awaiting confirmation");
            gate.ObserveCaptured(ButtonState.Running, now.AddSeconds(2), now);
            Check(gate.Verified == 0, "pre-click cached square cannot confirm recovery");
            gate.ObserveCaptured(ButtonState.Running, now.AddSeconds(3), now.AddSeconds(2));
            gate.ObserveCaptured(ButtonState.Running, now.AddSeconds(4), now.AddSeconds(2));
            Check(gate.Verified == 1, "post-click square confirms exactly once");
            var quiet = new RecoveryWindow(); quiet.Observe(ButtonState.Resume, now);
            var input = new InputSnapshot { Valid = true, Tick = 1, IdleMilliseconds = 10000 };
            Check(!quiet.Ready(input, now.AddMilliseconds(1499)) && quiet.Ready(input, now.AddMilliseconds(1500)), "triangle needs its own complete 1.5 second window");
            input.Held = true; Check(!quiet.Ready(input, now.AddSeconds(3)), "held input blocks recovery");
            input.Held = false; input.IdleMilliseconds = 1499; Check(!quiet.Ready(input, now.AddSeconds(3)), "new input restarts the quiet period");
            input.IdleMilliseconds = 1500; var changed = input; changed.Tick++;
            Check(!quiet.Unchanged(input, changed, now.AddSeconds(3)), "input during action preflight cancels the click");
            input.Valid = false; Check(!quiet.Ready(input, now.AddSeconds(3)), "unavailable input evidence blocks recovery");
            quiet.Observe(ButtonState.Send, now.AddSeconds(4)); input.Valid = true;
            Check(!quiet.Ready(input, now.AddSeconds(5)), "icon changes clear the quiet window");
            Check(new ResumeMemory { Window = 0, Automatic = true }.Reconnect() == null, "update cannot resume automatic clicking against a missing target");
        }
        static void Evidence()
        {
            DateTime now = DateTime.UtcNow;
            using (var previous = new WindowFrame { ContentAt = now, ContentSequence = 10 })
            using (var older = new WindowFrame { ContentAt = now.AddSeconds(-1), ContentSequence = 11 })
            {
                Check(!older.NewerEvidenceThan(previous), "new capture sequence cannot promote older pixels");
                older.ContentAt = now.AddSeconds(1); Check(older.NewerEvidenceThan(previous), "genuinely newer evidence can replace the observation");
                older.ContentAt = now.AddHours(-10); older.CapturedAt = now;
                Check(!older.ContentFresh(now), "fresh read timestamp cannot refresh stale content");
            }
            var watchdog = new CaptureWatchdog(); watchdog.Observe("unchanged-square", now);
            Check(watchdog.Due(now.AddSeconds(12), now.AddSeconds(12)), "unchanged running pixels trigger independent verification");
            using (var entered = new ManualResetEvent(false)) using (var release = new ManualResetEvent(false))
            {
                WindowFrame late = null; int calls = 0;
                var sampler = new CaptureSampler(delegate(IntPtr window) { calls++; entered.Set(); release.WaitOne(); return late = new WindowFrame { Image = new Bitmap(8, 8) }; });
                var pending = sampler.ReadAsync(IntPtr.Zero, 40); Check(entered.WaitOne(1000), "capture worker started");
                bool timedOut = false, busy = false;
                try { pending.GetAwaiter().GetResult(); } catch (CaptureDeadlineException) { timedOut = true; }
                try { sampler.ReadAsync(IntPtr.Zero, 40).GetAwaiter().GetResult(); } catch (CaptureBusyException) { busy = true; }
                release.Set();
                Check(timedOut && busy && calls == 1, "timeout quarantines a stalled worker instead of spawning another");
                Check(SpinWait.SpinUntil(() => !sampler.IsBusy, 1000) && late.Image == null, "late capture is discarded and released");
            }
        }
    }
}
