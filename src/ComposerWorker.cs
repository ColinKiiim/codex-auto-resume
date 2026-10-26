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
                            var after = composer.Inspect();
                            if (after.Running && after.Empty && composer.MessageCount(request.Prompt) > before)
                            { reply = after; reply.Result = "Submitted"; break; }
                        }
                    }
                    else if (request.Operation != "inspect") throw new InvalidOperationException();
                }
            }
            catch (Exception error) { stage += ":" + error.GetType().Name + (error is ComposerUnavailable ? ":" + error.Message : ""); reply = new ComposerReply { Result = mayHaveWritten ? "Uncertain" : "Blocked" }; }
            reply.Stage = stage;
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine(json.Serialize(reply)); return 0;
        }
    }

    internal sealed class AccessibleComposer : IComposer
    {
        readonly ComposerRequest request;
        readonly AutomationElement root;
        readonly AutomationElement editor;
        readonly ComposerContext context;
        string written;
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
            if (editors.Length != 1) throw new ComposerUnavailable("editor-count-" + editors.Length + "-all-" + all.Length);
            return editors[0];
        }
        ComposerContext BuildContext()
        {
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
        string Text()
        {
            object pattern;
            if (editor.TryGetCurrentPattern(ValuePattern.Pattern, out pattern)) return ((ValuePattern)pattern).Current.Value;
            if (editor.TryGetCurrentPattern(TextPattern.Pattern, out pattern)) return ((TextPattern)pattern).DocumentRange.GetText(-1).TrimEnd('\r', '\n');
            throw new InvalidOperationException();
        }
        public ComposerReply Inspect()
        {
            Check();
            // Attachments, dialogs, questions or voice controls block the whole
            // transaction. Unknown provider structure remains unsupported.
            bool blocked = Elements(ControlType.Window).Length > 0 || Elements(ControlType.Button).Any(e => Named(e,
                "Submit", "提交", "Close voice mode", "退出语音模式", "Remove attachment", "移除附件", "Remove file", "移除文件"));
            bool running = Elements(ControlType.Button).Any(e => Named(e, "Stop", "Stop generating", "停止", "停止生成"));
            return new ComposerReply { Context = context, Safe = !blocked, Running = running, Empty = Text().Length == 0 };
        }
        public ComposerContext Context { get { return context; } }
        public void Focus(bool background)
        {
            Check(); if (background) throw new NotSupportedException();
            // UIA SetFocus is itself the foreground fallback; some providers
            // activate their window through this accessibility operation.
            editor.SetFocus();
            if (Native.GetForegroundWindow() != new IntPtr(request.Window)) throw new ComposerUnavailable("activation-denied");
            if (!editor.Current.HasKeyboardFocus) throw new ComposerUnavailable("editor-focus-denied");
        }
        public void Write(string text)
        {
            Check(); if (!Inspect().Empty || !editor.Current.HasKeyboardFocus || Native.GetForegroundWindow() != new IntPtr(request.Window)) throw new InvalidOperationException();
            object pattern;
            if (!editor.TryGetCurrentPattern(ValuePattern.Pattern, out pattern) || ((ValuePattern)pattern).Current.IsReadOnly) throw new NotSupportedException();
            written = text; ((ValuePattern)pattern).SetValue(text);
            if (Text() != text) throw new InvalidOperationException();
        }
        public int MessageCount(string text)
        { Check(); return Elements(ControlType.Text).Count(e => e.Current.Name == text); }
        public void Send()
        {
            Check(); if (String.IsNullOrEmpty(written) || Text() != written || !Inspect().Safe || Inspect().Running || Native.GetForegroundWindow() != new IntPtr(request.Window)) throw new InvalidOperationException();
            var button = SendButton(); object pattern;
            if (!button.TryGetCurrentPattern(InvokePattern.Pattern, out pattern)) throw new NotSupportedException();
            ((InvokePattern)pattern).Invoke();
        }
        public void Dispose() { }
    }
}
