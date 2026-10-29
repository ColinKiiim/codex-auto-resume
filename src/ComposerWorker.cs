using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Automation;

namespace CodexAutoResume
{
    internal sealed class ComposerUnavailable : Exception { internal ComposerUnavailable(string reason) : base(reason) { } }
    internal interface IComposer : IDisposable
    {
        ComposerContext Context { get; }
        ComposerReply Inspect();
        void Focus(bool background);
        void VerifyWritable();
        void Write(string text);
        int MessageCount(string text);
        void Send();
        void ChangeState(ButtonState expected);
    }

    internal static class ComposerWorker
    {
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("iphlpapi.dll")] static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int family, int tableClass, uint reserved);
        internal static bool PortOwned(int port, int pid)
        {
            int size = 0; GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 3, 0);
            if (size < 4 || size > 1024 * 1024) return false;
            IntPtr table = Marshal.AllocHGlobal(size);
            try
            {
                if (GetExtendedTcpTable(table, ref size, false, 2, 3, 0) != 0) return false;
                int count = Marshal.ReadInt32(table);
                for (int n = 0; n < count && 4 + (n + 1) * 24 <= size; n++)
                {
                    IntPtr row = IntPtr.Add(table, 4 + n * 24);
                    if (Marshal.ReadInt32(row) == 2 && Marshal.ReadInt32(row, 4) == 0x0100007f &&
                        Marshal.ReadByte(row, 8) * 256 + Marshal.ReadByte(row, 9) == port && Marshal.ReadInt32(row, 20) == pid) return true;
                }
                return false;
            }
            finally { Marshal.FreeHGlobal(table); }
        }
        internal static int DiscoverPort(int pid)
        {
            using (var query = new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE ProcessId=" + pid))
            {
                query.Options.Timeout = TimeSpan.FromSeconds(3);
                using (var rows = query.Get()) foreach (ManagementObject row in rows) using (row)
                {
                    var match = Regex.Match(Convert.ToString(row["CommandLine"]), @"--remote-debugging-port(?:=|\s+)(\d+)(?:\s|$)");
                    int port;
                    if (match.Success && Int32.TryParse(match.Groups[1].Value, out port) && port > 0 && port < 65536 && PortOwned(port, pid)) return port;
                }
            }
            return 0;
        }
        internal static bool Valid(ComposerRequest request, uint? inputTick)
        {
            string why;
            var target = new Target { Window = new IntPtr(request.Window), ProcessId = request.ProcessId, ProcessStart = request.ProcessStart, ProcessPath = request.ProcessPath };
            DateTime expires = request.ExpiresUtc.ToUniversalTime();
            if (DateTime.UtcNow >= expires || (expires - DateTime.UtcNow).TotalSeconds > 31 || !target.ValidateProcessIdentity(out why)) return false;
            if (request.NotBeforeUtc.HasValue && DateTime.UtcNow < request.NotBeforeUtc.Value.ToUniversalTime()) return false;
            if (!inputTick.HasValue) return true;
            var input = Native.ReadInput();
            return input.Valid && !input.Held && input.IdleMilliseconds >= RecoveryWindow.QuietMilliseconds && input.Tick == inputTick.Value;
        }
        internal static int Run()
        {
            var json = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };
            var reply = new ComposerReply { Result = "Blocked" };
            bool mayHaveWritten = false;
            bool actionAttempted = false;
            string stage = "open";
            string bindingNote = null;
            try
            {
                var request = json.Deserialize<ComposerRequest>(Console.ReadLine());
                if (!Valid(request, null)) throw new InvalidOperationException();
                IComposer composer;
                if (request.Context == null)
                {
                    int port;
                    try { port = DiscoverPort(request.ProcessId); } catch (Exception error) { port = 0; bindingNote = "port-discovery:" + error.GetType().Name; }
                    try { composer = port == 0 ? null : new DebugComposer(request, port); }
                    catch (Exception error) { composer = null; bindingNote = "CDP-unavailable:" + error.GetType().Name + ":" + error.Message; }
                    if (composer == null) { if (bindingNote == null) bindingNote = "no-owned-CDP-endpoint"; composer = new AccessibleComposer(request); }
                }
                else composer = request.Context.Backend == "CDP" ? (IComposer)new DebugComposer(request, request.Context.Port) : new AccessibleComposer(request);
                using (composer)
                {
                    stage = "inspect";
                    reply = composer.Inspect(); reply.Context = composer.Context; reply.Result = "Observed";
                    if (request.Operation == "resume" || request.Operation == "pause")
                    {
                        ButtonState expected = request.Operation == "resume" ? ButtonState.Resume : ButtonState.Running;
                        var input = Native.ReadInput();
                        if (!Valid(request, input.Tick) || !reply.Safe || reply.State != expected ||
                            request.Context == null || !ComposerContext.SameConversation(request.Context, composer.Context)) throw new ComposerUnavailable("state-preflight");
                        IntPtr foreground = Native.GetForegroundWindow(); bool minimized = Native.IsIconic(new IntPtr(request.Window));
                        // Reserve before invoking; an unknown result must not be
                        // mistaken for a safe text fallback or an image click.
                        stage = "state-action"; actionAttempted = true; mayHaveWritten = true; composer.ChangeState(expected);
                        reply.Result = "Uncertain";
                        for (int n = 0; n < 15; n++)
                        {
                            if (!Valid(request, input.Tick) || Native.GetForegroundWindow() != foreground || Native.IsIconic(new IntPtr(request.Window)) != minimized) break;
                            var after = composer.Inspect();
                            if (!ComposerContext.SameConversation(after.Context, request.Context)) break;
                            if (after.Safe && (expected == ButtonState.Resume ? after.State == ButtonState.Running : after.State != ButtonState.Running && after.State != ButtonState.Unknown))
                            { reply = after; reply.Result = "Changed"; break; }
                            Thread.Sleep(100);
                        }
                    }
                    else if (request.Operation == "submit")
                    {
                        var input = Native.ReadInput();
                        if (!ComposerRequest.ValidPrompt(request.Prompt)) throw new ComposerUnavailable("invalid-prompt");
                        if (!Valid(request, input.Tick)) throw new ComposerUnavailable("input-or-target-changed");
                        if (!reply.Safe) throw new ComposerUnavailable("unsafe-composer");
                        if (reply.Running) throw new ComposerUnavailable("already-running");
                        if (!reply.Empty) throw new ComposerUnavailable("draft-not-empty");
                        int before = composer.MessageCount(request.Prompt);
                        if (before < 0) throw new InvalidOperationException();
                        IntPtr foreground = Native.GetForegroundWindow();
                        stage = "focus";
                        try { composer.Focus(true); }
                        catch
                        {
                            // No text has been written at this point. Foreground
                            // fallback is explicitly authorized by the user.
                            if (!Valid(request, input.Tick)) throw new ComposerUnavailable("input-before-focus");
                            if (Native.IsIconic(new IntPtr(request.Window))) throw new ComposerUnavailable("minimized-background-input-unavailable");
                            if (composer.Context.Backend != "UIA") SetForegroundWindow(new IntPtr(request.Window));
                            composer.Focus(false);
                            if (Native.GetForegroundWindow() != new IntPtr(request.Window)) throw new ComposerUnavailable("activation-denied");
                            foreground = new IntPtr(request.Window);
                        }
                        reply = composer.Inspect();
                        if (!reply.Safe || !reply.Empty || reply.Running || !Valid(request, input.Tick) || Native.GetForegroundWindow() != foreground) throw new ComposerUnavailable("focus-postflight");
                        stage = "write-preflight"; composer.VerifyWritable();
                        stage = "write"; mayHaveWritten = true; composer.Write(request.Prompt);
                        reply = composer.Inspect();
                        if (!reply.Safe || reply.Running || reply.Empty || !Valid(request, input.Tick) || Native.GetForegroundWindow() != foreground) throw new InvalidOperationException();
                        // Each transport rechecks the exact draft and original
                        // conversation again inside Send. Never use Enter.
                        stage = "send"; composer.Send();
                        stage = "verify";
                        reply.Result = "Uncertain";
                        for (int n = 0; n < 20; n++)
                        {
                            Thread.Sleep(150);
                            if (!Valid(request, input.Tick)) break;
                            try
                            {
                                var after = composer.Inspect();
                                // A newly confirmed user bubble plus an empty
                                // editor proves delivery even if a short reply
                                // already completed before the running sample.
                                if (after.Empty && composer.MessageCount(request.Prompt) > before)
                                { reply = after; reply.Result = "Submitted"; break; }
                            }
                            catch (ElementNotAvailableException) { /* Provider rebuilding after our own send; read only. */ }
                            catch (ComposerUnavailable error)
                            {
                                if (!error.Message.StartsWith("editor-count-", StringComparison.Ordinal)) throw;
                                // No actions follow this transient state. Wait
                                // for the same conversation's new editor only.
                            }
                        }
                    }
                    else if (request.Operation != "inspect" && request.Operation != "observe") throw new InvalidOperationException();
                }
            }
            catch (Exception error) { stage += ":" + error.GetType().Name + (error is ComposerUnavailable ? ":" + error.Message : ""); reply = new ComposerReply { Result = mayHaveWritten ? "Uncertain" : "Blocked" }; }
            reply.Stage = stage;
            reply.BindingNote = bindingNote;
            reply.ActionAttempted = actionAttempted;
            if (reply.ObservedUtc == DateTime.MinValue) reply.ObservedUtc = DateTime.UtcNow;
            // The portable application is a GUI executable, without a console.
            // OutputEncoding tries SetConsoleOutputCP and can throw even with
            // inherited redirected pipes. Write UTF-8 to the pipe directly.
            using (var output = new System.IO.StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false)))
            { output.WriteLine(json.Serialize(reply)); output.Flush(); }
            return 0;
        }
    }

    internal sealed class AccessibleComposer : IComposer
    {
        readonly ComposerRequest request;
        readonly AutomationElement root;
        readonly bool pageOnly;
        AutomationElement editor;
        readonly ComposerContext context;
        string written;
        bool sendAttempted;
        bool backgroundWrite;
        internal AccessibleComposer(ComposerRequest request, bool enforceContext = true, bool pageOnly = false)
        {
            this.request = request; this.pageOnly = pageOnly; root = AutomationElement.FromHandle(new IntPtr(request.Window));
            for (int attempt = 0; ; attempt++)
            {
                try { editor = SingleEditor(); break; }
                catch (ComposerUnavailable error) { if (attempt >= 2 || error.Message != "editor-count-0-all-0") throw; Thread.Sleep(150); }
            }
            context = BuildContext();
            if (enforceContext && request.Context != null && !Same(request.Context, context) &&
                !((request.Operation == "inspect" || request.Operation == "observe") && ComposerContext.SameConversation(request.Context, context))) throw new InvalidOperationException();
        }
        static bool Same(ComposerContext a, ComposerContext b)
        { return a.Backend == b.Backend && a.Title == b.Title && a.Conversation == b.Conversation && a.Editor == b.Editor; }
        static string Id(AutomationElement e) { return String.Join(".", e.GetRuntimeId().Select(n => n.ToString())); }
        AutomationElement[] Elements(ControlType type)
        { return root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, type)).Cast<AutomationElement>().ToArray(); }
        AutomationElement SingleEditor()
        {
            var all = Elements(ControlType.Edit).Concat(Elements(ControlType.Document)).ToArray();
            bool minimized = Native.IsIconic(new IntPtr(request.Window));
            var editors = all.Where(e => (minimized || !e.Current.IsOffscreen)).ToArray();
            var richEditors = editors.Where(e => e.Current.ControlType == ControlType.Edit && e.Current.ClassName.Split(' ').Contains("ProseMirror")).ToArray();
            if (richEditors.Length > 0) editors = richEditors;
            var named = editors.Where(e => e.Current.ControlType == ControlType.Edit && Named(e, "和 Codex 一起工作", "Work with Codex")).ToArray();
            if (named.Length > 0) editors = named;
            if (editors.Length != 1) throw new ComposerUnavailable("editor-count-" + editors.Length + "-all-" + all.Length);
            return editors[0];
        }
        AutomationElement CodexDocument()
        {
            // Recognized Store Codex structure, observed on 26.1021.3705.0.
            // Do not treat an arbitrary document title as a conversation ID.
            if ((request.ProcessPath ?? "").IndexOf("\\OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) < 0) return null;
            for (var node = editor; node != null; node = TreeWalker.ControlViewWalker.GetParent(node))
            {
                if (node.Current.ControlType == ControlType.Document && node.Current.AutomationId == "RootWebArea" && !String.IsNullOrWhiteSpace(node.Current.Name)) return node;
                if (node.Current.ControlType == ControlType.Window) break;
            }
            return null;
        }
        static bool InListItem(AutomationElement element)
        {
            for (int n = 0; element != null && n < 5; n++, element = TreeWalker.ControlViewWalker.GetParent(element))
                if (element.Current.ControlType == ControlType.ListItem) return true;
            return false;
        }
        ComposerContext BuildContext()
        {
            var document = CodexDocument();
            if (document != null)
            {
                string name = document.Current.Name;
                if (pageOnly) return new ComposerContext { Backend = "UIA", Title = name, Conversation = "document:" + Id(document), Editor = Id(editor) };
                var headers = Elements(ControlType.ToolBar).Where(e => Named(e, "聊天工具栏", "Chat toolbar") &&
                    e.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text)).Cast<AutomationElement>().Any(t => t.Current.Name == name)).ToArray();
                var conversations = Elements(ControlType.Button).Where(e => e.Current.Name == name && InListItem(e)).ToArray();
                // Match the main editor's document to its chat header.
                if (headers.Length != 1) throw new ComposerUnavailable("chat-header-ambiguous");
                // Sidebar titles are corroboration, not an availability condition.
                // The exact editor/document remain bound and are rechecked before actions.
                string chat = conversations.Length == 1 ? Id(conversations[0]) : "editor:" + Id(editor);
                return new ComposerContext { Backend = "UIA", Title = name, Conversation = Id(document) + ":" + chat, Editor = Id(editor) };
            }
            // A generic app title alone cannot bind a conversation. Require a
            // selected conversation item with a stable provider runtime ID.
            string title = Native.Title(new IntPtr(request.Window));
            var selected = root.FindAll(TreeScope.Descendants, new PropertyCondition(SelectionItemPattern.IsSelectedProperty, true)).Cast<AutomationElement>()
                .Where(e => e.Current.Name.Length >= 4 && title.IndexOf(e.Current.Name, StringComparison.Ordinal) >= 0 &&
                    (e.Current.ControlType == ControlType.TreeItem || e.Current.ControlType == ControlType.ListItem || e.Current.ControlType == ControlType.TabItem)).ToArray();
            if (selected.Length != 1 || title == "Codex" || title == "ChatGPT") throw new ComposerUnavailable("conversation-count-" + selected.Length);
            return new ComposerContext { Backend = "UIA", Title = title, Conversation = Id(selected[0]), Editor = Id(editor) };
        }
        void Check()
        {
            if (!ComposerWorker.Valid(request, null)) throw new ComposerUnavailable("composer-request-invalid");
            var fresh = BuildContext();
            if (fresh.Title != context.Title) throw new ComposerUnavailable("composer-title-changed");
            if (fresh.Conversation != context.Conversation) throw new ComposerUnavailable("composer-conversation-changed");
            if (!Same(context, fresh)) throw new ComposerUnavailable("composer-bound-editor-changed");
            if (Id(SingleEditor()) != context.Editor) throw new ComposerUnavailable("composer-resolved-editor-changed");
        }
        static bool Named(AutomationElement e, params string[] names)
        { return names.Contains(e.Current.Name, StringComparer.OrdinalIgnoreCase); }
        internal static ButtonState NamedState(string name)
        {
            name = (name ?? "").Trim();
            if (new[] { "Stop", "Stop generating", "停止", "停止生成" }.Contains(name, StringComparer.OrdinalIgnoreCase)) return ButtonState.Running;
            if (new[] { "Resume", "Resume generation", "Continue", "Continue generating", "恢复", "继续", "恢复运行", "继续运行", "继续生成" }.Contains(name, StringComparer.OrdinalIgnoreCase)) return ButtonState.Resume;
            if (new[] { "Send", "Send message", "发送", "发送消息" }.Contains(name, StringComparer.OrdinalIgnoreCase)) return ButtonState.Send;
            if (new[] { "Start voice mode", "启动语音模式", "语音模式" }.Contains(name, StringComparer.OrdinalIgnoreCase)) return ButtonState.Voice;
            return ButtonState.Unknown;
        }
        AutomationElement ComposerContainer()
        {
            var parent = TreeWalker.ControlViewWalker.GetParent(editor);
            for (; parent != null; parent = TreeWalker.ControlViewWalker.GetParent(parent))
            {
                // Never climb into a window/document-wide pool of buttons.
                if (parent.Current.ControlType == ControlType.Window || parent.Current.ControlType == ControlType.TitleBar || parent.Current.AutomationId == "RootWebArea") break;
                var buttons = parent.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)).Cast<AutomationElement>().ToArray();
                if (buttons.Any(b => NamedState(b.Current.Name) != ButtonState.Unknown)) return parent;
            }
            throw new ComposerUnavailable("composer-container-unavailable");
        }
        AutomationElement[] ActionButtons()
        {
            bool minimized = Native.IsIconic(new IntPtr(request.Window));
            return ComposerContainer().FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)).Cast<AutomationElement>()
                .Where(e => e.Current.IsEnabled && (minimized || !e.Current.IsOffscreen) && NamedState(e.Current.Name) != ButtonState.Unknown).ToArray();
        }
        internal Dictionary<string, object> Describe()
        {
            Check(); var ancestry = new List<object>();
            for (var node = editor; node != null && ancestry.Count < 8; node = TreeWalker.ControlViewWalker.GetParent(node))
            {
                ancestry.Add(new { type = node.Current.ControlType.ProgrammaticName, css = node.Current.ClassName,
                    runtimeHash = CaptureEvidence.Hash(Id(node)), patterns = node.GetSupportedPatterns().Select(p => p.ProgrammaticName).ToArray() });
                if (node.Current.AutomationId == "RootWebArea") break;
            }
            var buttons = ActionButtons().Select(b => new { state = NamedState(b.Current.Name).ToString(), enabled = b.Current.IsEnabled,
                offscreen = b.Current.IsOffscreen, runtimeHash = CaptureEvidence.Hash(Id(b)),
                patterns = b.GetSupportedPatterns().Select(p => p.ProgrammaticName).ToArray() }).ToArray();
            return new Dictionary<string, object> { { "result", "bound" }, { "conversationHash", CaptureEvidence.Hash(context.Conversation) },
                { "editorCount", 1 }, { "editorHash", CaptureEvidence.Hash(context.Editor) }, { "ancestry", ancestry }, { "composerButtons", buttons },
                { "minimizedStateTrusted", false } };
        }
        public void ChangeState(ButtonState expected)
        {
            Check(); var input = Native.ReadInput();
            var before = Inspect();
            if (!before.Safe || before.State != expected || !ComposerWorker.Valid(request, input.Tick)) throw new ComposerUnavailable("state-changed");
            var buttons = ActionButtons().Where(b => NamedState(b.Current.Name) == expected).ToArray();
            if (buttons.Length != 1) throw new ComposerUnavailable("composer-action-unavailable");
            if (!ComposerWorker.Valid(request, input.Tick)) throw new ComposerUnavailable("input-before-invoke");
            BackgroundAction(buttons[0], input.Tick);
        }
        void BackgroundAction(AutomationElement button, uint inputTick)
        {
            IntPtr child = IntPtr.Zero;
            for (var node = button; node != null; node = TreeWalker.RawViewWalker.GetParent(node))
            {
                int handle = node.Current.NativeWindowHandle;
                if (handle != 0) { child = new IntPtr(handle); break; }
            }
            var bounds = button.Current.BoundingRectangle;
            var rectangle = new System.Drawing.Rectangle((int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height);
            if (child == IntPtr.Zero || !BackgroundClick.SendControl(new IntPtr(request.Window), child, rectangle,
                delegate { Check(); return ComposerWorker.Valid(request, inputTick) && button.Current.BoundingRectangle == bounds; }))
                throw new ComposerUnavailable("background-control-geometry-unavailable");
        }
        AutomationElement SendButton()
        {
            // The editor and send must share the immediate composer container.
            var parent = ComposerContainer();
            var buttons = parent.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)).Cast<AutomationElement>()
                .Where(e => e.Current.IsEnabled && (Native.IsIconic(new IntPtr(request.Window)) || !e.Current.IsOffscreen) && Named(e, "Send", "Send message", "发送", "发送消息")).ToArray();
            if (buttons.Length != 1) throw new InvalidOperationException();
            return buttons[0];
        }
        static AutomationElement[] RawChildren(AutomationElement parent)
        {
            var result = new List<AutomationElement>(); var raw = TreeWalker.RawViewWalker;
            for (var child = raw.GetFirstChild(parent); child != null; child = raw.GetNextSibling(child))
            { if (result.Count >= 8) throw new ComposerUnavailable("editor-structure-limit"); result.Add(child); }
            return result.ToArray();
        }
        bool CodexPlaceholder(string text)
        {
            if (CodexDocument() == null || text != editor.Current.Name + "\n") return false;
            // The observed Codex provider exposes generated placeholder text as
            // Value/Text. Prove its raw empty-only wrapper and every descendant;
            // matching the label alone must never make a draft look empty.
            var root = RawChildren(editor);
            if (root.Length != 1 || root[0].Current.ControlType != ControlType.Group || root[0].Current.ClassName != "placeholder" ||
                root[0].Current.IsContentElement || root[0].Current.IsControlElement || root[0].Current.IsKeyboardFocusable) return false;
            var content = RawChildren(root[0]);
            if (content.Length != 2 || content[0].Current.ControlType != ControlType.Group || content[0].Current.ClassName != "" ||
                content[0].Current.IsContentElement || content[0].Current.IsControlElement || content[1].Current.ControlType != ControlType.Text ||
                content[1].Current.ClassName != "ProseMirror-trailingBreak" || content[1].Current.Name != "\n" || RawChildren(content[1]).Length != 0) return false;
            var placeholder = RawChildren(content[0]);
            return placeholder.Length == 1 && placeholder[0].Current.ControlType == ControlType.Text && placeholder[0].Current.ClassName == "" &&
                placeholder[0].Current.Name == editor.Current.Name && RawChildren(placeholder[0]).Length == 0;
        }
        string Text()
        {
            object pattern; string text;
            if (editor.TryGetCurrentPattern(ValuePattern.Pattern, out pattern)) text = ((ValuePattern)pattern).Current.Value;
            else if (editor.TryGetCurrentPattern(TextPattern.Pattern, out pattern)) text = ((TextPattern)pattern).DocumentRange.GetText(-1).TrimEnd('\r', '\n');
            else throw new ComposerUnavailable("editor-text-pattern-unavailable");
            return CodexPlaceholder(text) ? "" : text;
        }
        public ComposerReply Inspect()
        {
            // Chromium may freeze its accessibility state while minimized too.
            // Native identity remains useful to bind CDP, but these cached
            // labels cannot authorize a minimized state action or text input.
            if (Native.IsIconic(new IntPtr(request.Window))) throw new ComposerUnavailable("minimized-state-requires-cdp");
            if (sendAttempted)
            {
                editor = SingleEditor(); ComposerContext fresh = BuildContext();
                if (fresh.Backend != context.Backend || fresh.Title != context.Title || fresh.Conversation != context.Conversation)
                    throw new ComposerUnavailable("conversation-after-send");
                context.Editor = fresh.Editor;
            }
            Check();
            // Question controls belong to this chat document. Content checks
            // apply only to explicit input/submission operations.
            bool contentOperation = request.Operation != "observe" && request.Operation != "resume" && request.Operation != "pause";
            var area = ComposerContainer();
            var localButtons = area.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)).Cast<AutomationElement>().ToArray();
            bool question = (CodexDocument() ?? root).FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Group)).Cast<AutomationElement>()
                .Any(e => ((e.Current.ClassName ?? "").IndexOf("question", StringComparison.OrdinalIgnoreCase) >= 0 || Named(e, "Questions", "问题", "Waiting for your answer", "等待你的回答"))
                    && e.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)).Cast<AutomationElement>().Any(b => b.Current.IsEnabled && Named(b,"Submit","提交","Skip","跳过")));
            bool blocked = question || contentOperation && (Elements(ControlType.Window).Length > 0 || localButtons.Any(e => Named(e,
                "Submit", "提交", "Skip", "跳过", "Close voice mode", "退出语音模式", "Remove attachment", "移除附件", "Remove file", "移除文件")));
            var actions = ActionButtons();
            var states = actions.Select(b => NamedState(b.Current.Name)).Distinct().ToArray();
            ButtonState state = states.Length == 1 && actions.Length == 1 ? states[0] : ButtonState.Unknown;
            object writable; bool canWrite = editor.TryGetCurrentPattern(ValuePattern.Pattern, out writable) && !((ValuePattern)writable).Current.IsReadOnly;
            bool readInput = request.Operation != "observe" && request.Operation != "resume" && request.Operation != "pause";
            return new ComposerReply { Context = context, Safe = !blocked, Running = state == ButtonState.Running, Empty = readInput && Text().Length == 0,
                State = state, BlockReason = question ? "current-task-question" : blocked ? "content-operation-blocked" : null, BackgroundWritable = canWrite, ObservedUtc = DateTime.UtcNow };
        }
        public ComposerContext Context { get { return context; } }
        public void Focus(bool background)
        {
            Check(); backgroundWrite = false;
            if (background)
            {
                object writable;
                if (!editor.TryGetCurrentPattern(ValuePattern.Pattern, out writable) || ((ValuePattern)writable).Current.IsReadOnly) throw new NotSupportedException();
                backgroundWrite = true; return;
            }
            // UIA SetFocus is itself the foreground fallback; some providers
            // activate their window through this accessibility operation.
            InputSnapshot input = Native.ReadInput();
            if (!ComposerWorker.Valid(request, input.Tick)) throw new ComposerUnavailable("input-before-focus");
            editor.SetFocus();
            DateTime until = DateTime.UtcNow.AddMilliseconds(1500);
            do
            {
                if (!ComposerWorker.Valid(request, input.Tick)) throw new ComposerUnavailable("input-during-focus");
                try
                {
                    editor = SingleEditor(); ComposerContext fresh = BuildContext();
                    if (fresh.Backend != context.Backend || fresh.Title != context.Title || fresh.Conversation != context.Conversation)
                        throw new ComposerUnavailable("conversation-during-focus");
                    if (Native.GetForegroundWindow() == new IntPtr(request.Window) && editor.Current.HasKeyboardFocus)
                    { context.Editor = fresh.Editor; return; }
                }
                catch (ElementNotAvailableException) { }
                Thread.Sleep(50);
            } while (DateTime.UtcNow < until);
            if (Native.GetForegroundWindow() != new IntPtr(request.Window)) throw new ComposerUnavailable("activation-denied");
            throw new ComposerUnavailable("editor-focus-denied");
        }
        public void VerifyWritable()
        {
            Check(); object pattern;
            if (!editor.TryGetCurrentPattern(ValuePattern.Pattern, out pattern) || ((ValuePattern)pattern).Current.IsReadOnly)
                throw new ComposerUnavailable("value-pattern-unavailable");
        }
        public void Write(string text)
        {
            Check(); if (!Inspect().Empty || !backgroundWrite && (!editor.Current.HasKeyboardFocus || Native.GetForegroundWindow() != new IntPtr(request.Window))) throw new InvalidOperationException();
            object pattern;
            if (!editor.TryGetCurrentPattern(ValuePattern.Pattern, out pattern) || ((ValuePattern)pattern).Current.IsReadOnly) throw new NotSupportedException();
            written = text; ((ValuePattern)pattern).SetValue(text);
            // Codex replaces the accessibility editor after SetValue. Rebind
            // only after our single write, to the same proven conversation,
            // and require exact readback before allowing Send.
            for (int n = 0; n < 5; n++)
            {
                try
                {
                    if (!ComposerWorker.Valid(request, null)) throw new ComposerUnavailable("target-after-write");
                    editor = SingleEditor(); ComposerContext fresh = BuildContext();
                    if (fresh.Backend != context.Backend || fresh.Title != context.Title || fresh.Conversation != context.Conversation)
                        throw new ComposerUnavailable("conversation-after-write");
                    if (Text() == text) { context.Editor = fresh.Editor; return; }
                }
                catch (ElementNotAvailableException) { }
                Thread.Sleep(100);
            }
            throw new ComposerUnavailable("write-readback-mismatch");
        }
        static bool BubbleText(AutomationElement element, System.Text.StringBuilder value, int depth, ref int count)
        {
            if (depth > 12 || ++count > 512 || value.Length > 4096) return false;
            var info = element.Current;
            if (info.ControlType == ControlType.Text) { value.Append(info.Name); return true; }
            if (info.ControlType != ControlType.Group) return false;
            var raw = TreeWalker.RawViewWalker;
            for (var child = raw.GetFirstChild(element); child != null; child = raw.GetNextSibling(child))
                if (!BubbleText(child, value, depth + 1, ref count)) return false;
            return true;
        }
        public int MessageCount(string text)
        {
            Check(); var texts = Elements(ControlType.Text);
            if (CodexDocument() == null) return texts.Count(e => e.Current.Name == text);
            var bubbles = new HashSet<string>(); int matches = 0;
            foreach (var part in texts.Where(e => e.Current.Name.Length > 0 && text.Contains(e.Current.Name)))
            {
                var parent = TreeWalker.RawViewWalker.GetParent(part);
                for (int n = 0; parent != null && n < 8; n++, parent = TreeWalker.RawViewWalker.GetParent(parent))
                {
                    string css = parent.Current.ClassName;
                    if (!css.Split(' ').Contains("bg-user-message") || !css.Split(' ').Contains("text-user-message")) continue;
                    if (bubbles.Add(Id(parent)))
                    {
                        int nodes = 0; var value = new System.Text.StringBuilder();
                        if (BubbleText(parent, value, 0, ref nodes) && value.ToString() == text) matches++;
                    }
                    break;
                }
            }
            return matches;
        }
        public void Send()
        {
            Check(); if (sendAttempted || String.IsNullOrEmpty(written) || Text() != written || !Inspect().Safe || Inspect().Running || !backgroundWrite && Native.GetForegroundWindow() != new IntPtr(request.Window)) throw new InvalidOperationException();
            var button = SendButton();
            if (backgroundWrite) { var input = Native.ReadInput(); sendAttempted = true; BackgroundAction(button, input.Tick); }
            else { object pattern; if (!button.TryGetCurrentPattern(InvokePattern.Pattern, out pattern)) throw new NotSupportedException(); sendAttempted = true; ((InvokePattern)pattern).Invoke(); }
        }
        public void Dispose() { }
    }
}
