using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Automation;
using System.Windows.Forms;

namespace CodexAutoResume
{
    [ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ScopeUia
    {
        void Unused0(); void Unused1(); void Unused2();
        ScopeElement ElementFromHandle(IntPtr hwnd);
        void Unused4(); void Unused5(); void Unused6(); void Unused7(); void Unused8(); void Unused9(); void Unused10();
        ScopeWalker ControlViewWalker();
    }
    [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ScopeElement
    {
        void Unused0(); void Unused1(); void Unused2(); void Unused3(); void Unused4(); void Unused5(); void Unused6();
        [return: MarshalAs(UnmanagedType.Struct)] object Property(int id);
        [return: MarshalAs(UnmanagedType.Struct)] object PropertyEx(int id, [MarshalAs(UnmanagedType.Bool)] bool ignoreDefault);
    }
    [ComImport, Guid("4042c624-389c-4afc-a630-9df854a541fc"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ScopeWalker
    { ScopeElement Parent(ScopeElement e); ScopeElement First(ScopeElement e); ScopeElement Last(ScopeElement e); ScopeElement Next(ScopeElement e); }
    public sealed class ScopeEntry
    {
        public string Kind { get; set; }
        public string Id { get; set; }
        public string ProjectId { get; set; }
        public string Project { get; set; }
        public string Title { get; set; }
        public bool Current { get; set; }
        public bool Offscreen { get; set; }
        public string Expanded { get; set; }
        public bool Ambiguous { get; set; }
        public override string ToString() { return (Kind == "project" ? (UiText.English ? "Project: " : "项目：") : (UiText.English ? "Chat: " : "聊天：")) + (Project == null || Kind == "project" ? "" : Project + " / ") + Title + (Ambiguous ? (UiText.English ? " (duplicate name)" : "（同名）") : ""); }
    }
    public sealed class ScopeRequest
    {
        public ComposerRequest Target { get; set; }
        public string Operation { get; set; }
        public string FromId { get; set; }
        public string ToId { get; set; }
        public string ProjectId { get; set; }
        public uint InputTick { get; set; }
    }
    public sealed class ScopeReply
    {
        public string Result { get; set; }
        public string Stage { get; set; }
        public string CurrentId { get; set; }
        public string ProjectId { get; set; }
        public string CurrentTitle { get; set; }
        public List<ScopeEntry> Entries { get; set; }
    }
    internal static class ScopeWorker
    {
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
        static ScopeWalker walker;
        static string Prop(ScopeElement element, int id) { return element.PropertyEx(id, true) as string ?? ""; }
        static string Id(ScopeElement element)
        { var ids = element.Property(30000) as int[]; return ids == null ? "" : String.Join(".", ids); }
        sealed class Scan
        {
            internal readonly List<ScopeEntry> Entries = new List<ScopeEntry>();
            internal readonly List<string> Headers = new List<string>();
            internal string Document;
            internal int CurrentCount, Count;
            internal ScopeEntry Current;
        }
        static void Visit(ScopeElement e, ScopeEntry project, bool toolbar, Scan scan, int depth)
        {
            if (e == null) return;
            if (depth > 36 || ++scan.Count > 4000) throw new InvalidOperationException("sidebar tree limit");
            int type = Convert.ToInt32(e.Property(30003)); string name = Prop(e, 30005), css = Prop(e, 30012);
            // Message bodies may contain thousands of nodes and are irrelevant
            // to the sidebar/toolbar identity proof. If the provider moves the
            // toolbar into this subtree, the proof fails closed.
            if (css.Contains("thread-scroll-container") || css.StartsWith("group/sidebar-rail ", StringComparison.Ordinal)) return;
            if (type == ControlType.Group.Id && css.StartsWith("group/cwd ", StringComparison.Ordinal))
            {
                string id = Id(e); if (id.Length == 0 || String.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("project identity");
                project = new ScopeEntry { Kind = "project", Id = id, ProjectId = id, Title = name, Project = name };
                scan.Entries.Add(project);
            }
            if (project != null && type == ControlType.Button.Id && name == project.Title && !css.Contains("data-[app-action-sidebar-thread-selected=true]"))
            {
                string aria = Prop(e, 30102);
                if (aria.Split(';').Contains("expanded=true")) project.Expanded = "true";
                else if (aria.Split(';').Contains("expanded=false")) project.Expanded = "false";
            }
            if (type == ControlType.Document.Id && Prop(e, 30011) == "RootWebArea")
            { if (scan.Document != null) throw new InvalidOperationException("multiple documents"); scan.Document = name; }
            toolbar = toolbar || type == ControlType.ToolBar.Id && (name == "聊天工具栏" || name == "Chat toolbar");
            if (toolbar && (type == ControlType.Text.Id || type == ControlType.Image.Id)) scan.Headers.Add(name);
            if (type == ControlType.Button.Id && css.Contains("data-[app-action-sidebar-thread-selected=true]"))
            {
                string id = Id(e), aria = Prop(e, 30102);
                bool current = aria.Split(';').Contains("current=page");
                bool offscreen = Convert.ToBoolean(e.Property(30022));
                if (id.Length == 0 || String.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("chat identity");
                var chat = new ScopeEntry { Kind = "chat", Id = id, Title = name, ProjectId = project == null ? null : project.Id, Project = project == null ? null : project.Title, Current = current, Offscreen = offscreen };
                scan.Entries.Add(chat); if (current) { scan.CurrentCount++; scan.Current = chat; }
                return; // Nested pin/archive controls are not conversation rows.
            }
            for (var child = walker.First(e); child != null; child = walker.Next(child)) Visit(child, project, toolbar, scan, depth + 1);
        }
        static ScopeReply Read(IntPtr window)
        {
            var automation = (ScopeUia)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("ff48dba4-60ef-4201-aa87-54103eef594e")));
            walker = automation.ControlViewWalker(); var scan = new Scan(); Visit(automation.ElementFromHandle(window), null, false, scan, 0);
            if (scan.CurrentCount != 1 || scan.Current == null || scan.Document != scan.Current.Title || !scan.Headers.Contains(scan.Current.Title))
                throw new InvalidOperationException("current chat evidence disagrees");
            if (scan.Current.Project != null && !scan.Headers.Contains("项目：" + scan.Current.Project) && !scan.Headers.Contains("Project: " + scan.Current.Project))
                throw new InvalidOperationException("project evidence disagrees");
            if (scan.Entries.GroupBy(x => x.Id).Any(x => x.Count() != 1)) throw new InvalidOperationException("duplicate runtime identity");
            foreach (var entry in scan.Entries) entry.Ambiguous = scan.Entries.Count(x => x.Kind == entry.Kind && x.Title == entry.Title) != 1;
            return new ScopeReply { Result = "Observed", CurrentId = scan.Current.Id, ProjectId = scan.Current.ProjectId, CurrentTitle = scan.Current.Title, Entries = scan.Entries };
        }
        static AutomationElement Chat(IntPtr window, string id)
        {
            return AutomationElement.FromHandle(window).FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)).Cast<AutomationElement>()
                .Single(e => String.Join(".", e.GetRuntimeId().Select(n => n.ToString())) == id && e.Current.ClassName.Contains("data-[app-action-sidebar-thread-selected=true]"));
        }
        static bool PrepareNavigation(ComposerRequest request, uint inputTick, IntPtr window)
        {
            if (!ComposerWorker.Valid(request, inputTick)) throw new InvalidOperationException("input before navigation");
            if (Native.IsIconic(window)) throw new InvalidOperationException("minimized navigation requires a bound debug endpoint");
            using (var composer = new AccessibleComposer(request, false))
                if (!composer.Inspect().Safe) throw new ComposerUnavailable("navigation-needs-user");
            if (!ComposerWorker.Valid(request, inputTick)) throw new InvalidOperationException("input after navigation safety check");
            if (Native.GetForegroundWindow() == window) return false;
            // This Codex provider can block Invoke itself while backgrounded,
            // so resolve activation before committing the single navigation.
            SetForegroundWindow(window);
            DateTime until = DateTime.UtcNow.AddMilliseconds(1500);
            while (DateTime.UtcNow < until && Native.GetForegroundWindow() != window)
            { if (!ComposerWorker.Valid(request, inputTick)) throw new InvalidOperationException("input during activation"); Thread.Sleep(50); }
            if (Native.GetForegroundWindow() != window) throw new InvalidOperationException("navigation activation denied");
            return true;
        }
        static DebugComposer DebugNavigation(ComposerRequest request)
        {
            try { int port = ComposerWorker.DiscoverPort(request.ProcessId); return port == 0 ? null : new DebugComposer(request, port); }
            catch { return null; }
        }
        static ScopeReply NavigateDebug(DebugComposer composer, ScopeRequest request, ScopeReply before, ScopeEntry destination, bool expand, Action reserve)
        {
            IntPtr window = new IntPtr(request.Target.Window), foreground = Native.GetForegroundWindow(); bool minimized = Native.IsIconic(window);
            var current = Read(window);
            if (current.CurrentId != before.CurrentId || !current.Entries.Any(x => x.Id == destination.Id && x.Title == destination.Title && x.ProjectId == destination.ProjectId)) throw new InvalidOperationException("Navigation proof changed");
            composer.Navigate(current, destination, expand, request.InputTick, reserve);
            composer.RefreshAccessibility();
            DateTime deadline = DateTime.UtcNow.AddSeconds(8);
            string reason = "native-state-unconfirmed";
            while (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(150);
                if (!ComposerWorker.Valid(request.Target, request.InputTick)) { reason = "input-or-target-changed"; break; }
                if (Native.GetForegroundWindow() != foreground) { reason = "foreground-changed"; break; }
                if (Native.IsIconic(window) != minimized) { reason = "minimized-state-changed"; break; }
                var fresh = Read(window);
                if (expand ? fresh.CurrentId == before.CurrentId && fresh.Entries.Any(x => x.Id == destination.Id && x.Expanded == "true") :
                    fresh.CurrentId == destination.Id && fresh.ProjectId == destination.ProjectId)
                { fresh.Result = expand ? "Expanded" : "Switched"; fresh.Stage = "background-CDP-native-confirmed"; return fresh; }
            }
            return new ScopeReply { Result = "Uncertain", Stage = "background-CDP-" + reason };
        }
        internal static int Run()
        {
            var json = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };
            ScopeReply reply = new ScopeReply { Result = "Blocked" }; bool invoked = false;
            try
            {
                var request = json.Deserialize<ScopeRequest>(Console.ReadLine());
                if (request == null || !ComposerWorker.Valid(request.Target, null)) throw new InvalidOperationException("target");
                IntPtr window = new IntPtr(request.Target.Window); reply = Read(window);
                if (request.Operation == "switch")
                {
                    if (!ComposerWorker.Valid(request.Target, request.InputTick) || reply.CurrentId != request.FromId) throw new InvalidOperationException("source changed");
                    var destination = reply.Entries.Single(x => x.Kind == "chat" && x.Id == request.ToId && x.ProjectId == request.ProjectId);
                    // Ambiguous titles are not usable as corroborating evidence.
                    if (reply.Entries.Count(x => x.Kind == "chat" && x.Title == destination.Title) != 1) throw new InvalidOperationException("duplicate chat title");
                    using (var debug = DebugNavigation(request.Target))
                        if (debug != null) { reply = NavigateDebug(debug, request, reply, destination, false, delegate { invoked = true; }); goto finished; }
                    bool activationRequested = PrepareNavigation(request.Target, request.InputTick, window);
                    var chat = Chat(window, destination.Id); object pattern;
                    if (chat.Current.IsOffscreen && !Native.IsIconic(window))
                    {
                        object scroll;
                        if (!chat.TryGetCurrentPattern(ScrollItemPattern.Pattern, out scroll)) throw new InvalidOperationException("chat offscreen");
                        if (!ComposerWorker.Valid(request.Target, request.InputTick)) throw new InvalidOperationException("input before scroll");
                        ((ScrollItemPattern)scroll).ScrollIntoView();
                        Thread.Sleep(150);
                        var scrolled = Read(window);
                        if (scrolled.CurrentId != request.FromId || !ComposerWorker.Valid(request.Target, request.InputTick)) throw new InvalidOperationException("changed while scrolling");
                        chat = Chat(window, destination.Id);
                        if (chat.Current.IsOffscreen) throw new InvalidOperationException("chat remains offscreen");
                    }
                    if (!chat.Current.IsEnabled || !chat.TryGetCurrentPattern(InvokePattern.Pattern, out pattern)) throw new InvalidOperationException("invoke unavailable");
                    var preflight = Read(window);
                    if (preflight.CurrentId != request.FromId || !ComposerWorker.Valid(request.Target, request.InputTick)) throw new InvalidOperationException("preflight changed");
                    invoked = true; ((InvokePattern)pattern).Invoke();
                    DateTime started = DateTime.UtcNow;
                    DateTime until = started.AddSeconds(8);
                    while (DateTime.UtcNow < until)
                    {
                        Thread.Sleep(150); if (!ComposerWorker.Valid(request.Target, request.InputTick)) throw new InvalidOperationException("input changed after invoke");
                        var fresh = Read(window);
                        if (fresh.CurrentId == destination.Id && fresh.ProjectId == request.ProjectId) { reply = fresh; reply.Result = "Switched"; reply.Stage = activationRequested ? "activated-before-single-invoke" : "foreground"; break; }
                    }
                    if (reply.Result != "Switched") reply = new ScopeReply { Result = "Uncertain" };
                }
                else if (request.Operation == "expand")
                {
                    if (!ComposerWorker.Valid(request.Target, request.InputTick) || reply.CurrentId != request.FromId) throw new InvalidOperationException("source changed");
                    var selected = reply.Entries.Single(x => x.Kind == "project" && x.Id == request.ProjectId && x.Expanded == "false");
                    using (var debug = DebugNavigation(request.Target))
                        if (debug != null) { reply = NavigateDebug(debug, request, reply, selected, true, delegate { invoked = true; }); goto finished; }
                    PrepareNavigation(request.Target, request.InputTick, window);
                    var root = AutomationElement.FromHandle(window);
                    var group = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Group)).Cast<AutomationElement>()
                        .Single(e => String.Join(".", e.GetRuntimeId().Select(n => n.ToString())) == selected.Id && e.Current.Name == selected.Title);
                    var buttons = group.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)).Cast<AutomationElement>()
                        .Where(e => e.Current.Name == selected.Title).ToArray();
                    var expandable = buttons.Where(e => { object p; return e.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out p); }).ToArray();
                    if (expandable.Length != 1) throw new InvalidOperationException("project expand unavailable");
                    var button = expandable[0]; object pattern;
                    if (button.Current.IsOffscreen)
                    {
                        object scroll; if (!button.TryGetCurrentPattern(ScrollItemPattern.Pattern, out scroll)) throw new InvalidOperationException("project offscreen");
                        ((ScrollItemPattern)scroll).ScrollIntoView(); Thread.Sleep(150);
                    }
                    if (!ComposerWorker.Valid(request.Target, request.InputTick) || Read(window).CurrentId != request.FromId || !button.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out pattern)) throw new InvalidOperationException("expand preflight changed");
                    invoked = true; ((ExpandCollapsePattern)pattern).Expand();
                    DateTime until = DateTime.UtcNow.AddSeconds(8);
                    while (DateTime.UtcNow < until)
                    {
                        Thread.Sleep(150); if (!ComposerWorker.Valid(request.Target, request.InputTick)) throw new InvalidOperationException("input changed after expand");
                        var fresh = Read(window);
                        if (fresh.CurrentId == request.FromId && fresh.Entries.Any(x => x.Kind == "project" && x.Id == selected.Id && x.Title == selected.Title && x.Expanded == "true"))
                        { reply = fresh; reply.Result = "Expanded"; break; }
                    }
                    if (reply.Result != "Expanded") reply = new ScopeReply { Result = "Uncertain" };
                }
                else if (request.Operation != "read") throw new InvalidOperationException("operation");
            }
            catch (Exception error) { reply = new ScopeReply { Result = invoked ? "Uncertain" : "Blocked", Stage = error.GetType().Name + ":" + error.Message }; }
            finished:
            using (var output = new System.IO.StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false))) { output.WriteLine(json.Serialize(reply)); output.Flush(); }
            return 0;
        }
    }
    internal static class ScopeBridge
    {
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);
        internal static ScopeReply Run(Target target, string operation, string from, string to, string project, uint tick, int timeout)
        {
            var req = new ScopeRequest { Target = new ComposerRequest { Window = target.Window.ToInt64(), ProcessId = target.ProcessId, ProcessStart = target.ProcessStart, ProcessPath = target.ProcessPath, ExpiresUtc = DateTime.UtcNow.AddSeconds(25) }, Operation = operation, FromId = from, ToId = to, ProjectId = project, InputTick = tick };
            var json = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };
            string input = String.Concat(json.Serialize(req).Select(c => c > 127 ? "\\u" + ((int)c).ToString("x4") : c.ToString()));
            using (var process = new Process { StartInfo = new ProcessStartInfo(Updates.Executable, "--scope-worker") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 } })
            using (var job = new UsageJob())
            {
                try
                {
                    process.Start(); job.Add(process); AllowSetForegroundWindow(process.Id); process.StandardInput.WriteLine(input); process.StandardInput.Close();
                    var line = process.StandardOutput.ReadLineAsync();
                    if (!line.Wait(timeout) || !process.WaitForExit(500)) return new ScopeReply { Result = operation == "switch" || operation == "expand" ? "Uncertain" : "Blocked" };
                    return json.Deserialize<ScopeReply>(line.Result ?? "null") ?? new ScopeReply { Result = "Blocked" };
                }
                catch { return new ScopeReply { Result = operation == "switch" || operation == "expand" ? "Uncertain" : "Blocked" }; }
                finally { try { if (!process.HasExited) process.Kill(); } catch { } }
            }
        }
    }
    internal static class ScopePolicy
    {
        internal static bool AllowsEntry(bool limited, bool uncertain, ScopeEntry selected, ScopeEntry entry)
        {
            if (!limited) return true;
            if (uncertain || selected == null || selected.Ambiguous || entry == null || entry.Ambiguous || entry.Kind != "chat") return false;
            return selected.Kind == "project" ? entry.ProjectId == selected.Id && entry.Project == selected.Title :
                selected.Kind == "chat" && selected.Id == entry.Id && selected.Title == entry.Title && selected.ProjectId == entry.ProjectId && selected.Project == entry.Project;
        }
        internal static bool FrameAfterBinding(bool limited, DateTime contentAt, DateTime boundAt)
        { return !limited || contentAt != DateTime.MinValue && contentAt >= boundAt; }
        internal static bool Allows(bool limited, bool uncertain, ScopeEntry selected, ScopeReply reply)
        {
            if (!limited) return true;
            if (uncertain || selected == null || selected.Ambiguous || reply == null || reply.Result != "Observed" || reply.Entries == null) return false;
            return selected.Kind == "project" ? reply.ProjectId == selected.Id && reply.Entries.Any(x => x.Kind == "project" && x.Id == selected.Id && x.Title == selected.Title && !x.Ambiguous) :
                selected.Kind == "chat" && reply.CurrentId == selected.Id && reply.ProjectId == selected.ProjectId && reply.Entries.Any(x => x.Kind == "chat" && x.Id == selected.Id && x.ProjectId == selected.ProjectId && x.Title == selected.Title && x.Project == selected.Project && !x.Ambiguous);
        }
    }
    internal sealed partial class MainForm
    {
        ScopeEntry scopeSelection;
        bool scopeUncertain, scopeBusy;
        long scopeWindow, scopeProcessStart;
        int scopeProcessId;
        Target scopeListTarget;
        int scopeListRevision;
        readonly Dictionary<string, DateTime> scopeBlockedChats = new Dictionary<string, DateTime>();
        DateTime nextScopeSwitch;
        ComboBox scopeChoices;
        Label scopeInfo;
        bool ScopeMatches(ScopeReply reply)
        { return ScopePolicy.Allows(preferences.LimitScope, scopeUncertain, scopeSelection, reply); }
        bool ScopeAllowsEntry(Target connected, ScopeEntry entry)
        {
            if (!preferences.LimitScope) return true;
            return connected != null && connected.Window.ToInt64() == scopeWindow && connected.ProcessId == scopeProcessId && connected.ProcessStart == scopeProcessStart &&
                ScopePolicy.AllowsEntry(true, scopeUncertain, scopeSelection, entry);
        }
        bool ScopeCheck(Target connected)
        {
            if (!preferences.LimitScope) return true;
            if (scopeUncertain || scopeSelection == null || connected == null || connected.Window.ToInt64() != scopeWindow ||
                connected.ProcessId != scopeProcessId || connected.ProcessStart != scopeProcessStart) return false;
            var reply = ScopeBridge.Run(connected, "read", null, null, null, 0, 2500);
            return ScopeMatches(reply);
        }
        void AddScopeSettings(FlowLayoutPanel panel)
        {
            panel.Controls.Add(UiLabel("恢复范围"));
            scopeChoices = new ComboBox { Name = "ScopeChoices", Width = 480, DropDownStyle = ComboBoxStyle.DropDownList };
            panel.Controls.Add(scopeChoices);
            scopeInfo = new Label { AutoSize = true, MaximumSize = new System.Drawing.Size(490, 0), ForeColor = System.Drawing.Color.DimGray,
                Text = preferences.LimitScope ? "范围需重新确认；确认前不会自动操作" : "当前监测显示中的聊天" };
            panel.Controls.Add(scopeInfo);
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            row.Controls.Add(UiButton("读取项目和聊天", async delegate {
                if (scopeBusy || target == null || !monitoring) return;
                scopeBusy = true; Target connected = target; int revision = generation; ComboBox choices = scopeChoices; Label feedback = scopeInfo;
                try
                {
                    var read = await Task.Run(() => ScopeBridge.Run(connected, "read", null, null, null, 0, 4000));
                    if (choices.IsDisposed || feedback.IsDisposed || scopeChoices != choices) return;
                    if (target != connected || generation != revision || read.Result != "Observed" || read.Entries == null) { scopeInfo.Text = "无法确认侧栏，请展开侧栏后重试"; return; }
                    scopeListTarget = connected; scopeListRevision = revision;
                    scopeChoices.Items.Clear(); scopeChoices.Items.Add("不限制范围");
                    foreach (var entry in read.Entries) scopeChoices.Items.Add(entry);
                    scopeChoices.SelectedIndex = 0; scopeInfo.Text = "选择项目或聊天后点击应用";
                }
                finally { scopeBusy = false; }
            }));
            row.Controls.Add(UiButton("应用范围", async delegate {
                if (scopeBusy || scopeChoices.SelectedIndex < 0) return;
                var chosen = scopeChoices.SelectedItem as ScopeEntry; ComboBox choices = scopeChoices; Label feedback = scopeInfo;
                Target connected = target; int revision = generation;
                if (chosen != null)
                {
                    if (connected == null || connected != scopeListTarget || revision != scopeListRevision) { scopeInfo.Text = "目标已变化，请重新读取侧栏"; return; }
                    scopeBusy = true;
                    try
                    {
                        var read = await Task.Run(() => ScopeBridge.Run(connected, "read", null, null, null, 0, 4000));
                        if (choices.IsDisposed || feedback.IsDisposed || scopeChoices != choices) return;
                        if (chosen.Ambiguous || read.Entries != null && read.Entries.Any(x => x.Kind == chosen.Kind && x.Id == chosen.Id && x.Ambiguous))
                        { scopeInfo.Text = "存在同名对象，请选择可明确确认的聊天"; return; }
                        if (target != connected || generation != revision || scopeChoices.SelectedItem != chosen || read.Result != "Observed" || read.Entries == null ||
                            !read.Entries.Any(x => x.Kind == chosen.Kind && x.Id == chosen.Id && x.ProjectId == chosen.ProjectId && x.Title == chosen.Title))
                        { scopeInfo.Text = "范围已变化，请重新读取侧栏"; return; }
                    }
                    finally { scopeBusy = false; }
                }
                if (automatic.Checked) automatic.Checked = false;
                scopeSelection = chosen;
                scopeWindow = connected == null ? 0 : connected.Window.ToInt64(); scopeProcessId = connected == null ? 0 : connected.ProcessId; scopeProcessStart = connected == null ? 0 : connected.ProcessStart;
                scopeBlockedChats.Clear(); preferences.LimitScope = scopeSelection != null; scopeUncertain = false; nextScopeSwitch = DateTime.UtcNow.AddSeconds(15);
                CancelQuota(); gate.BreakStreak(); recoveryWindow.Reset(); SavePreferences();
                scopeInfo.Text = scopeSelection == null ? "当前监测显示中的聊天" : "已绑定 " + scopeSelection.Title + "；请开启自动恢复";
            }));
            panel.Controls.Add(row);
        }
        async void TickScope()
        {
            if (!preferences.LimitScope || scopeSelection == null || scopeSelection.Kind != "project" || scopeUncertain || scopeBusy || timedBusy || updating || quotaCancellation != null ||
                !monitoring || !automatic.Checked || target == null || target.Window.ToInt64() != scopeWindow || target.ProcessId != scopeProcessId || target.ProcessStart != scopeProcessStart || diagnosing || questions.Waiting || BackgroundNeedsUser || quotaPause.Owned || gate.AwaitingRunning || DateTime.UtcNow < nextScopeSwitch) return;
            InputSnapshot input = inputReader();
            if (!input.Valid || input.Held || input.IdleMilliseconds < RecoveryWindow.QuietMilliseconds) return;
            Target connected = target; int revision = generation; ScopeEntry selected = scopeSelection;
            scopeBusy = true; updating = true; nextScopeSwitch = DateTime.UtcNow.AddSeconds(15);
            try
            {
                var view = await Task.Run(() => ScopeBridge.Run(connected, "read", null, null, null, 0, 4000));
                var after = inputReader();
                if (target != connected || generation != revision || !automatic.Checked || !monitoring || scopeSelection != selected || !after.Valid || after.Held || after.Tick != input.Tick || view.Result != "Observed" || view.Entries == null) return;
                if (!view.Entries.Any(x => x.Kind == "project" && x.Id == selected.Id && x.Title == selected.Title))
                { scopeUncertain = true; SetStatus("项目已变化，请重新确认范围"); return; }
                var project = view.Entries.Single(x => x.Kind == "project" && x.Id == selected.Id && x.Title == selected.Title);
                if (project.Expanded == "false")
                {
                    var expanded = await Task.Run(() => ScopeBridge.Run(connected, "expand", view.CurrentId, null, selected.Id, input.Tick, 14000));
                    if (expanded.Result == "Uncertain") { scopeUncertain = true; SetStatus("项目展开结果不明，请重新确认范围"); }
                    else if (expanded.Result == "Expanded") { nextScopeSwitch = DateTime.UtcNow; SetStatus("已展开指定项目，正在读取聊天"); }
                    else SetStatus("无法展开指定项目，请手动展开侧栏");
                    return;
                }
                var chats = view.Entries.Where(x => x.Kind == "chat" && x.ProjectId == selected.Id && (!scopeBlockedChats.ContainsKey(x.Id) || scopeBlockedChats[x.Id] <= DateTime.UtcNow) && view.Entries.Count(y => y.Kind == "chat" && y.Title == x.Title) == 1).ToList();
                if (chats.Count == 0) { SetStatus("指定项目没有可确认的可见聊天，等待侧栏展开"); return; }
                int index = chats.FindIndex(x => x.Id == view.CurrentId); var next = chats[(index + 1) % chats.Count];
                if (next.Id == view.CurrentId) return;
                var result = await Task.Run(() => ScopeBridge.Run(connected, "switch", view.CurrentId, next.Id, selected.Id, input.Tick, 14000));
                if (result.Result != "Switched")
                {
                    // Once a switch may have been dispatched, never retry or act
                    // on a stale view until the user explicitly rebinds scope.
                    if (result.Result == "Uncertain") { scopeUncertain = true; SetStatus("聊天切换结果不明，请重新确认范围"); }
                    else { scopeBlockedChats[next.Id] = DateTime.UtcNow.AddMinutes(1); SetStatus("此聊天暂不可切换，继续检查其他聊天"); }
                    return;
                }
                gate.BreakStreak(); recoveryWindow.Reset(); quotaQuiet.Reset(); ResetPreview(); nextMonitor = DateTime.MinValue;
                SetStatus("已切换指定项目中的聊天，正在重新核验");
            }
            finally { scopeBusy = false; updating = false; }
        }
    }
}
