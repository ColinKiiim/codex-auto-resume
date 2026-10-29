using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace CodexAutoResume
{
    internal sealed class DebugComposer : IComposer
    {
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };
        ClientWebSocket socket = new ClientWebSocket();
        readonly CancellationTokenSource lifetime = new CancellationTokenSource(17000);
        readonly ComposerRequest request;
        ComposerContext context;
        string editorId, written;
        bool stateDispatched;
        int serial;
        string windowProofNote;
        static readonly string RuntimeFunction = LoadRuntime();
        static string LoadRuntime()
        { using (var stream = typeof(DebugComposer).Assembly.GetManifestResourceStream("composer-runtime.js")) using (var reader = new StreamReader(stream)) return reader.ReadToEnd(); }
        internal DebugComposer(ComposerRequest request, int port)
        {
            this.request = request;
            try
            {
                if (!ComposerWorker.PortOwned(port, request.ProcessId)) throw new InvalidOperationException();
                ComposerContext nativeProof = null;
                try { using (var native = new AccessibleComposer(request, false, true)) nativeProof = native.Context; } catch (ComposerUnavailable) { }
                string windowBinding = "window:" + request.Window + ":" + request.ProcessId + ":" + request.ProcessStart;
                if (request.Context != null && (request.Context.NativeBinding != windowBinding || String.IsNullOrEmpty(request.Context.PageId))) throw new InvalidOperationException("Native window binding changed");
                string list;
                using (var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(3) })
                    list = http.GetStringAsync("http://127.0.0.1:" + port + "/json/list").GetAwaiter().GetResult();
                var pages = json.Deserialize<List<Dictionary<string, object>>>(list);
                var candidates = new List<Dictionary<string, object>>();
                foreach (var p in pages)
                    if (Get(p, "type") as string == "page" && (Get(p, "url") as string ?? "").StartsWith("app://-/index.html", StringComparison.Ordinal) &&
                        (nativeProof == null || String.Equals(Get(p, "title") as string, nativeProof.Title, StringComparison.Ordinal)) && (request.Context == null || (Get(p, "id") as string) == request.Context.PageId)) candidates.Add(p);
                Dictionary<string, object> page = null;
                if (nativeProof == null && request.Context == null)
                {
                    string caption = Native.Title(new IntPtr(request.Window));
                    var captionPages = candidates.FindAll(p => String.Equals(Get(p,"title") as string,caption,StringComparison.Ordinal));
                    if (caption != "Codex" && caption != "ChatGPT" && captionPages.Count == 1) page = captionPages[0];
                }
                if (page == null && candidates.Count == 1 && (nativeProof != null || request.Context != null)) page = candidates[0];
                else if (page == null) foreach (var p in candidates)
                {
                    var endpointProbe = new Uri((string)Get(p, "webSocketDebuggerUrl"));
                    if (endpointProbe.Scheme != "ws" || !endpointProbe.IsLoopback || endpointProbe.Port != port) throw new InvalidOperationException();
                    socket.ConnectAsync(endpointProbe, lifetime.Token).GetAwaiter().GetResult();
                    var geometry = Call("Runtime.evaluate", new Dictionary<string, object> { { "expression", "({left:screenX,top:screenY,width:outerWidth,height:outerHeight})" }, { "returnByValue", true } });
                    var bounds = Get((Dictionary<string,object>)Get(geometry,"result"), "value") as Dictionary<string, object>;
                    if (MatchesWindow(bounds)) { if (page != null) throw new InvalidOperationException("ambiguous-app-windows"); page = p; }
                    socket.Dispose(); socket = new ClientWebSocket();
                }
                if (page == null) throw new ComposerUnavailable("app-page-window-unmatched:" + windowProofNote);
                Uri endpoint = new Uri((string)Get(page, "webSocketDebuggerUrl"));
                if (endpoint.Scheme != "ws" || !endpoint.IsLoopback || endpoint.Port != port) throw new InvalidOperationException();
                socket.ConnectAsync(endpoint, lifetime.Token).GetAwaiter().GetResult();
                var response = Call("Runtime.evaluate", new Dictionary<string, object> {
                    { "expression", "(" + RuntimeFunction + ").call(document,'locate','',null,false)" }, { "returnByValue", false } });
                CheckException(response);
                editorId = (string)Get((Dictionary<string, object>)Get(response, "result"), "objectId");
                // A read-only poll may rebind a replaced editor in the same
                // independently proven page/chat. Mutations retain the exact
                // editor token saved by their immediately preceding poll.
                context = request.Operation == "inspect" || request.Operation == "observe" ? null : request.Context;
                var state = Operation("bind", "");
                var found = new ComposerContext { Backend = "CDP", Port = port, PageId = (string)Get(page, "id"), NativeBinding = windowBinding,
                    ActionToken = request.Context == null ? (string)Get(state, "actionToken") : request.Context.ActionToken, ActionSource = (string)Get(state, "actionSource"),
                    Conversation = (string)Get(state, "conversation"), Editor = (string)Get(state, "editor"), Title = (string)Get(state, "title") };
                if (nativeProof != null && found.Title != nativeProof.Title) throw new InvalidOperationException();
                if (context != null && (context.Conversation != found.Conversation || context.Editor != found.Editor)) throw new InvalidOperationException();
                if (request.Context != null && request.Operation != "observe" && request.Operation != "inspect" && !ComposerContext.SameConversation(request.Context, found)) throw new InvalidOperationException();
                context = found;
            }
            catch { Dispose(); throw; }
        }
        internal static object Get(Dictionary<string, object> data, string key)
        { object value; return data.TryGetValue(key, out value) ? value : null; }
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct Placement { public int Length, Flags, Show; public Native.POINT Min, Max; public Native.RECT Normal; }
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr window, ref Placement value);
        bool MatchesWindow(Dictionary<string, object> bounds)
        {
            if (bounds == null || !bounds.ContainsKey("left") || !bounds.ContainsKey("width")) return false;
            Native.RECT rect; if (!Native.GetWindowRect(new IntPtr(request.Window), out rect)) return false;
            double x = Convert.ToDouble(Get(bounds,"left")), y = Convert.ToDouble(Get(bounds,"top")), w = Convert.ToDouble(Get(bounds,"width")), h = Convert.ToDouble(Get(bounds,"height"));
            double scale = Native.Dpi(new IntPtr(request.Window)) / 96.0;
            windowProofNote += "native=" + rect.Rectangle + "; renderer=" + x + "," + y + "," + w + "," + h + "; scale=" + scale + " | ";
            if (Matches(rect,x,y,w,h,scale)) return true;
            if (Native.IsIconic(new IntPtr(request.Window))) { var place = new Placement { Length = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Placement)) }; if (GetWindowPlacement(new IntPtr(request.Window), ref place)) return Matches(place.Normal,x,y,w,h,scale); }
            return false;
        }
        static bool Matches(Native.RECT rect,double x,double y,double w,double h,double scale)
        { return (Near(rect.Left,x)&&Near(rect.Top,y) || Near(rect.Left,x*scale)&&Near(rect.Top,y*scale)) && (Near(rect.Right-rect.Left,w)&&Near(rect.Bottom-rect.Top,h) || Near(rect.Right-rect.Left,w*scale)&&Near(rect.Bottom-rect.Top,h*scale)); }
        static bool Near(double a, double b) { return Math.Abs(a-b) <= 12; }
        Dictionary<string, object> Call(string method, Dictionary<string, object> parameters)
        {
            int id = ++serial;
            byte[] bytes = Encoding.UTF8.GetBytes(json.Serialize(new { id = id, method = method, @params = parameters }));
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                deadline.CancelAfter(3000);
                socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, deadline.Token).GetAwaiter().GetResult();
                byte[] buffer = new byte[4096];
                for (int n = 0; n < 64; n++) using (var message = new MemoryStream())
                {
                    WebSocketReceiveResult part;
                    do
                    {
                        part = socket.ReceiveAsync(new ArraySegment<byte>(buffer), deadline.Token).GetAwaiter().GetResult();
                        if (part.MessageType != WebSocketMessageType.Text || message.Length + part.Count > 1024 * 1024) throw new InvalidOperationException();
                        message.Write(buffer, 0, part.Count);
                    } while (!part.EndOfMessage);
                    var reply = json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(message.ToArray()));
                    if (Get(reply, "id") == null || Convert.ToInt32(Get(reply, "id")) != id) continue;
                    if (Get(reply, "error") != null) throw new InvalidOperationException();
                    return (Dictionary<string, object>)Get(reply, "result");
                }
                throw new InvalidOperationException();
            }
        }
        static void CheckException(Dictionary<string, object> reply)
        {
            var details = Get(reply, "exceptionDetails") as Dictionary<string, object>;
            if (details == null) return;
            var exception = Get(details, "exception") as Dictionary<string, object>;
            string reason = exception == null ? "page-evaluation" : Convert.ToString(Get(exception,"description")).Split('\n')[0];
            if (reason.Length > 180) reason = reason.Substring(0,180);
            throw new ComposerUnavailable(reason);
        }
        Dictionary<string, object> Operation(string mode, string sample)
        {
            if (!ComposerWorker.Valid(request, null)) throw new InvalidOperationException();
            bool mutating = mode != "bind" && mode != "inspect";
            uint inputTick = mutating ? Native.ReadInput().Tick : 0;
            // Window/process identity is rechecked here; the exact page, chat and
            // editor/action tokens are verified inside the page before dispatch.
            string function = RuntimeFunction;
            if (mutating && !ComposerWorker.Valid(request, inputTick)) throw new ComposerUnavailable("input-before-cdp-action");
            var reply = Call("Runtime.callFunctionOn", new Dictionary<string, object> {
                { "objectId", editorId }, { "functionDeclaration", function }, { "returnByValue", true },
                { "arguments", new object[] { new { value = mode }, new { value = sample }, new { value = context }, new { value = request.Operation != "observe" && request.Operation != "resume" && request.Operation != "pause" } } } });
            CheckException(reply);
            return (Dictionary<string, object>)Get((Dictionary<string, object>)Get(reply, "result"), "value");
        }
        public ComposerContext Context { get { return context; } }
        public ComposerReply Inspect()
        {
            var value = Operation("inspect", "");
            if (!stateDispatched && (request.Operation == "resume" || request.Operation == "pause") && request.Context != null && !String.IsNullOrEmpty(request.Context.ActionToken) && request.Context.ActionToken != (string)Get(value,"actionToken"))
                throw new ComposerUnavailable("action-replaced-before-dispatch");
            if (stateDispatched || request.Operation == "inspect" || request.Operation == "observe") { context.ActionToken = (string)Get(value, "actionToken"); context.ActionSource = (string)Get(value, "actionSource"); }
            return new ComposerReply { Context = context, Safe = (bool)Get(value, "safe"), Running = (bool)Get(value, "running"), Empty = (bool)Get(value, "empty"),
                State = (ButtonState)Enum.Parse(typeof(ButtonState), (string)Get(value, "state")), BlockReason = (string)Get(value, "blockReason"), BackgroundWritable = true, ObservedUtc = DateTime.UtcNow };
        }
        public void Focus(bool background)
        {
            IntPtr before = Native.GetForegroundWindow();
            var state = Operation("focus", "");
            if (!(bool)Get(state, "editorActive") || background && Native.GetForegroundWindow() != before) throw new InvalidOperationException();
        }
        public void VerifyWritable() { Operation("writeCheck", ""); }
        public void Write(string text)
        {
            uint tick = Native.ReadInput().Tick; Operation("writeCheck", ""); written = text;
            if (!ComposerWorker.Valid(request, tick)) throw new ComposerUnavailable("input-before-cdp-write");
            Call("Input.insertText", new Dictionary<string, object> { { "text", text } });
            if (!(bool)Get(Operation("inspect", text), "matches")) throw new InvalidOperationException();
        }
        public int MessageCount(string text) { return Convert.ToInt32(Get(Operation("inspect", text), "messageCount")); }
        public void Send() { Operation("send", written); }
        public void ChangeState(ButtonState expected) { Operation(expected == ButtonState.Resume ? "resume" : "pause", ""); stateDispatched = true; }
        internal ScopeReply ReadScope()
        {
            Operation("inspect", "");
            const string read = @"function(expected){
                if(!this.isConnected||this.__carEditorToken!==expected.Editor||document.title!==expected.Title)throw Error('Page changed');
                const visible=e=>!!(e&&e.getClientRects().length&&!e.closest('[aria-hidden=true],[inert]'));
                const groups=[...document.querySelectorAll('[data-sidebar-project-container-id]')].filter(visible);
                const entries=groups.map(g=>({Kind:'project',Id:'cdp-project:'+g.getAttribute('data-sidebar-project-container-id'),ProjectId:'cdp-project:'+g.getAttribute('data-sidebar-project-container-id'),Title:g.getAttribute('aria-label'),Project:g.getAttribute('aria-label'),Expanded:g.querySelector('button[aria-expanded]')?.getAttribute('aria-expanded')}));
                const rows=[...document.querySelectorAll('[data-app-action-sidebar-thread-id]')].filter(visible);
                const rowId=e=>'cdp-chat:'+(e.getAttribute('data-app-action-sidebar-thread-host-id')||'')+':'+e.getAttribute('data-app-action-sidebar-thread-id');
                for(const r of rows){const g=r.closest('[data-sidebar-project-container-id]');entries.push({Kind:'chat',Id:rowId(r),Title:r.getAttribute('data-app-action-sidebar-thread-title')||r.textContent.trim(),ProjectId:g?'cdp-project:'+g.getAttribute('data-sidebar-project-container-id'):null,Project:g?.getAttribute('aria-label')||null,Current:r.getAttribute('data-app-action-sidebar-thread-id').toLowerCase()===expected.Conversation,Offscreen:false})}
                const current=entries.filter(e=>e.Kind==='chat'&&e.Current);
                if(current.length!==1||entries.some(e=>!e.Id||!e.Title)||new Set(entries.map(e=>e.Id)).size!==entries.length)throw Error('Sidebar identity unavailable');
                return {Result:'Observed',Stage:'bound-CDP-sidebar',CurrentId:current[0].Id,CurrentTitle:current[0].Title,ProjectId:current[0].ProjectId,Entries:entries};
            }";
            var reply = Call("Runtime.callFunctionOn", new Dictionary<string, object> { { "objectId", editorId }, { "functionDeclaration", read }, { "returnByValue", true }, { "arguments", new object[] { new { value = context } } } });
            CheckException(reply);
            return json.Deserialize<ScopeReply>(json.Serialize(Get((Dictionary<string,object>)Get(reply,"result"),"value")));
        }
        internal void Navigate(ScopeReply proof, ScopeEntry destination, bool expand, uint tick, Action reserve)
        {
            var state = Operation("inspect", "");
            if (!(bool)Get(state, "safe")) throw new ComposerUnavailable("navigation-needs-user");
            if (context.Title != proof.CurrentTitle) throw new InvalidOperationException("Navigation native binding changed");
            if (!ComposerWorker.Valid(request, tick)) throw new InvalidOperationException("Input before navigation");
            const string navigate = @"function(expected,currentTitle,entry,expand){
                const visible=e=>!!(e&&e.getClientRects().length&&getComputedStyle(e).visibility!=='hidden'&&!e.closest('[aria-hidden=true],[inert]'));
                if(!this.isConnected||location.protocol!=='app:'||location.hostname!=='-'||document.title!==expected.Title||document.title!==currentTitle)throw Error('Page changed');
                if([...document.querySelectorAll('[data-testid*=question],[data-testid*=elicitation],[data-question-id]')].some(e=>visible(e)&&[...e.querySelectorAll('button')].some(b=>!b.disabled&&/^(skip|submit|跳过|提交)$/i.test((b.getAttribute('aria-label')||b.textContent||'').trim()))))throw Error('Navigation needs user');
                const rows=[...document.querySelectorAll('[data-app-action-sidebar-thread-id]')].filter(visible);
                const active=rows.filter(e=>e.getAttribute('aria-current')==='page');
                if(active.length!==1||active[0].getAttribute('data-app-action-sidebar-thread-id')!==expected.Conversation)throw Error('Selected conversation changed');
                let action;
                if(expand){
                    const groups=[...document.querySelectorAll('[data-sidebar-project-container-id]')].filter(e=>visible(e)&&(entry.Id.startsWith('cdp-project:')?'cdp-project:'+e.getAttribute('data-sidebar-project-container-id')===entry.Id:e.getAttribute('aria-label')===entry.Title));
                    if(groups.length!==1)throw Error('Project changed');
                    const bs=[...groups[0].querySelectorAll('button[aria-expanded=false]')].filter(e=>visible(e)&&e.closest('[data-sidebar-project-container-id]')===groups[0]);
                    if(bs.length!==1)throw Error('Expand control unavailable');action=bs[0];
                }else{
                    const targets=rows.filter(e=>(entry.Id.startsWith('cdp-chat:')?'cdp-chat:'+(e.getAttribute('data-app-action-sidebar-thread-host-id')||'')+':'+e.getAttribute('data-app-action-sidebar-thread-id')===entry.Id:e.getAttribute('data-app-action-sidebar-thread-title')===entry.Title)
                        &&e.getAttribute('data-app-action-sidebar-thread-host-id')===active[0].getAttribute('data-app-action-sidebar-thread-host-id')
                        &&((e.closest('[data-sidebar-project-container-id]')?.getAttribute('aria-label')||null)===entry.Project));
                    if(targets.length!==1)throw Error('Destination changed');action=targets[0];
                }
                if(action.disabled||action.getAttribute('aria-disabled')==='true')throw Error('Disabled navigation');
                action.click();return true;
            }";
            reserve(); // Once dispatched, errors must never cause an Invoke fallback.
            var reply = Call("Runtime.callFunctionOn", new Dictionary<string, object> { { "objectId", editorId }, { "functionDeclaration", navigate }, { "returnByValue", true },
                { "arguments", new object[] { new { value = context }, new { value = proof.CurrentTitle }, new { value = destination }, new { value = expand } } } });
            CheckException(reply);
        }
        internal void RefreshAccessibility()
        { Call("Accessibility.getFullAXTree", new Dictionary<string, object> { { "depth", 2 } }); }
        public void Dispose() { socket.Dispose(); lifetime.Dispose(); }
    }
}
