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
            if (DateTime.UtcNow >= expires || (expires - DateTime.UtcNow).TotalSeconds > 31 || !target.ValidateIdentity(out why)) return false;
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
            string stage = "open";
            try
            {
                var request = json.Deserialize<ComposerRequest>(Console.ReadLine());
                if (!Valid(request, null)) throw new InvalidOperationException();
                IComposer composer;
                if (request.Context == null)
                {
                    int port;
                    try { port = DiscoverPort(request.ProcessId); } catch { port = 0; }
                    try { composer = port == 0 ? null : new DebugComposer(request, port); }
                    catch { composer = null; }
                    if (composer == null) composer = new AccessibleComposer(request);
                }
                else composer = request.Context.Backend == "CDP" ? (IComposer)new DebugComposer(request, request.Context.Port) : new AccessibleComposer(request);
                using (composer)
                {
                    stage = "inspect";
                    reply = composer.Inspect(); reply.Context = composer.Context; reply.Result = "Observed";
                    if (request.Operation == "submit")
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
                    else if (request.Operation != "inspect") throw new InvalidOperationException();
                }
            }
            catch (Exception error) { stage += ":" + error.GetType().Name + (error is ComposerUnavailable ? ":" + error.Message : ""); reply = new ComposerReply { Result = mayHaveWritten ? "Uncertain" : "Blocked" }; }
            reply.Stage = stage;
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
        AutomationElement editor;
        readonly ComposerContext context;
        string written;
        bool sendAttempted;
        internal AccessibleComposer(ComposerRequest request)
        {
            this.request = request; root = AutomationElement.FromHandle(new IntPtr(request.Window));
            editor = SingleEditor(); context = BuildContext();
            if (request.Context != null && !Same(request.Context, context)) throw new InvalidOperationException();
        }
        static bool Same(ComposerContext a, ComposerContext b)
        { return a.Backend == b.Backend && a.Title == b.Title && a.Conversation == b.Conversation && a.Editor == b.Editor; }
        static string Id(AutomationElement e) { return String.Join(".", e.GetRuntimeId().Select(n => n.ToString())); }
        AutomationElement[] Elements(ControlType type)
        { return root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, type)).Cast<AutomationElement>().ToArray(); }
        AutomationElement SingleEditor()
        {
            var all = Elements(ControlType.Edit).Concat(Elements(ControlType.Document)).ToArray();
            var editors = all.Where(e => e.Current.IsEnabled && !e.Current.IsOffscreen && e.Current.IsKeyboardFocusable).ToArray();
            if (CodexDocument() != null)
                editors = editors.Where(e => e.Current.ControlType == ControlType.Edit && Named(e, "和 Codex 一起工作", "Work with Codex")).ToArray();
            if (editors.Length != 1) throw new ComposerUnavailable("editor-count-" + editors.Length + "-all-" + all.Length);
            return editors[0];
        }
        AutomationElement CodexDocument()
        {
            // Recognized Store Codex structure, observed on 26.1021.3705.0.
            // Do not treat an arbitrary document title as a conversation ID.
            if ((request.ProcessPath ?? "").IndexOf("\\OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) < 0) return null;
            var documents = Elements(ControlType.Document).Where(e => e.Current.AutomationId == "RootWebArea" && !String.IsNullOrWhiteSpace(e.Current.Name)).ToArray();
            return documents.Length == 1 ? documents[0] : null;
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
                var headers = Elements(ControlType.ToolBar).Where(e => Named(e, "聊天工具栏", "Chat toolbar") &&
                    e.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text)).Cast<AutomationElement>().Any(t => t.Current.Name == name)).ToArray();
                var conversations = Elements(ControlType.Button).Where(e => e.Current.Name == name && InListItem(e)).ToArray();
                // Require agreement of the document, unique sidebar item and
                // current chat header. Duplicate names or hidden sidebars wait.
                if (headers.Length != 1 || conversations.Length != 1) throw new ComposerUnavailable("codex-conversation-structure");
                return new ComposerContext { Backend = "UIA", Title = name, Conversation = Id(document) + ":" + Id(conversations[0]), Editor = Id(editor) };
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
        { if (!ComposerWorker.Valid(request, null) || !Same(context, BuildContext()) || Id(SingleEditor()) != context.Editor) throw new InvalidOperationException(); }
        static bool Named(AutomationElement e, params string[] names)
        { return names.Contains(e.Current.Name, StringComparer.OrdinalIgnoreCase); }
        AutomationElement SendButton()
        {
            // The editor and send must share the immediate composer container.
            var parent = TreeWalker.ControlViewWalker.GetParent(editor);
            var buttons = parent.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)).Cast<AutomationElement>()
                .Where(e => e.Current.IsEnabled && !e.Current.IsOffscreen && Named(e, "Send", "Send message", "发送", "发送消息")).ToArray();
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
            else throw new InvalidOperationException();
            return CodexPlaceholder(text) ? "" : text;
        }
        public ComposerReply Inspect()
        {
            if (sendAttempted)
            {
                editor = SingleEditor(); ComposerContext fresh = BuildContext();
                if (fresh.Backend != context.Backend || fresh.Title != context.Title || fresh.Conversation != context.Conversation)
                    throw new ComposerUnavailable("conversation-after-send");
                context.Editor = fresh.Editor;
            }
            Check();
            // Attachments, dialogs, questions or voice controls block the whole
            // transaction. Unknown provider structure remains unsupported.
            bool blocked = Elements(ControlType.Window).Length > 0 || Elements(ControlType.Button).Any(e => Named(e,
                "Submit", "提交", "Skip", "跳过", "Close voice mode", "退出语音模式", "Remove attachment", "移除附件", "Remove file", "移除文件"));
            bool running = Elements(ControlType.Button).Any(e => Named(e, "Stop", "Stop generating", "停止", "停止生成"));
            return new ComposerReply { Context = context, Safe = !blocked, Running = running, Empty = Text().Length == 0 };
        }
        public ComposerContext Context { get { return context; } }
        public void Focus(bool background)
        {
            Check(); if (background) throw new NotSupportedException();
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
            Check(); if (!Inspect().Empty || !editor.Current.HasKeyboardFocus || Native.GetForegroundWindow() != new IntPtr(request.Window)) throw new InvalidOperationException();
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
            Check(); if (sendAttempted || String.IsNullOrEmpty(written) || Text() != written || !Inspect().Safe || Inspect().Running || Native.GetForegroundWindow() != new IntPtr(request.Window)) throw new InvalidOperationException();
            var button = SendButton(); object pattern;
            if (!button.TryGetCurrentPattern(InvokePattern.Pattern, out pattern)) throw new NotSupportedException();
            sendAttempted = true; ((InvokePattern)pattern).Invoke();
        }
        public void Dispose() { }
    }
}
