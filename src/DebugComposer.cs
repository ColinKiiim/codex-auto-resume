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
        readonly ClientWebSocket socket = new ClientWebSocket();
        readonly CancellationTokenSource lifetime = new CancellationTokenSource(17000);
        readonly ComposerRequest request;
        ComposerContext context;
        string editorId, written;
        int serial;
        internal DebugComposer(ComposerRequest request, int port)
        {
            this.request = request;
            try
            {
                if (!ComposerWorker.PortOwned(port, request.ProcessId)) throw new InvalidOperationException();
                ComposerContext nativeProof;
                using (var native = new AccessibleComposer(request, false)) nativeProof = native.Context;
                if (request.Context != null && (request.Context.NativeBinding != nativeProof.Conversation || String.IsNullOrEmpty(request.Context.PageId))) throw new InvalidOperationException("Native page binding changed");
                string list;
                using (var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(3) })
                    list = http.GetStringAsync("http://127.0.0.1:" + port + "/json/list").GetAwaiter().GetResult();
                var pages = json.Deserialize<List<Dictionary<string, object>>>(list);
                Dictionary<string, object> page = null;
                foreach (var p in pages)
                    if (Get(p, "type") as string == "page" && (Get(p, "url") as string ?? "").StartsWith("app://-/index.html", StringComparison.Ordinal) &&
                        String.Equals(Get(p, "title") as string, nativeProof.Title, StringComparison.Ordinal) && (request.Context == null || (Get(p, "id") as string) == request.Context.PageId))
                    { if (page != null) throw new InvalidOperationException(); page = p; }
                if (page == null) throw new InvalidOperationException();
                Uri endpoint = new Uri((string)Get(page, "webSocketDebuggerUrl"));
                if (endpoint.Scheme != "ws" || !endpoint.IsLoopback || endpoint.Port != port) throw new InvalidOperationException();
                socket.ConnectAsync(endpoint, lifetime.Token).GetAwaiter().GetResult();
                var response = Call("Runtime.evaluate", new Dictionary<string, object> {
                    { "expression", "(()=>{const es=[...document.querySelectorAll('.ProseMirror[contenteditable=true]')].filter(e=>e.getClientRects().length&&getComputedStyle(e).visibility!=='hidden'&&!e.closest('[aria-hidden=true],[inert]'));if(es.length!==1)throw Error();return es[0]})()" }, { "returnByValue", false } });
                CheckException(response);
                editorId = (string)Get((Dictionary<string, object>)Get(response, "result"), "objectId");
                // A read-only poll may rebind a replaced editor in the same
                // independently proven page/chat. Mutations retain the exact
                // editor token saved by their immediately preceding poll.
                context = request.Operation == "inspect" || request.Operation == "observe" ? null : request.Context;
                var state = Operation("bind", "");
                var found = new ComposerContext { Backend = "CDP", Port = port, PageId = (string)Get(page, "id"), NativeBinding = nativeProof.Conversation,
                    Conversation = (string)Get(state, "conversation"), Editor = (string)Get(state, "editor"), Title = (string)Get(state, "title") };
                if (found.Title != nativeProof.Title) throw new InvalidOperationException();
                if (context != null && (context.Conversation != found.Conversation || context.Editor != found.Editor || context.Title != found.Title)) throw new InvalidOperationException();
                if (request.Context != null && !ComposerContext.SameConversation(request.Context, found)) throw new InvalidOperationException();
                context = found;
            }
            catch { Dispose(); throw; }
        }
        internal static object Get(Dictionary<string, object> data, string key)
        { object value; return data.TryGetValue(key, out value) ? value : null; }
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
        { if (Get(reply, "exceptionDetails") != null) throw new InvalidOperationException("Composer context or state unavailable"); }
        Dictionary<string, object> Operation(string mode, string sample)
        {
            if (!ComposerWorker.Valid(request, null)) throw new InvalidOperationException();
            bool mutating = mode != "bind" && mode != "inspect";
            uint inputTick = mutating ? Native.ReadInput().Tick : 0;
            if (context != null && mode != "bind" && mode != "inspect")
                using (var native = new AccessibleComposer(request, false))
                    if (native.Context.Conversation != context.NativeBinding || native.Context.Title != context.Title) throw new InvalidOperationException("Native conversation changed");
            const string function = @"function(mode,sample,expected,readInput){
              const visible=e=>!!(e&&e.getClientRects().length&&getComputedStyle(e).visibility!=='hidden'&&!e.closest('[aria-hidden=true],[inert]'));
              const editors=[...document.querySelectorAll('.ProseMirror[contenteditable=true]')].filter(visible);
              if(!this.isConnected||location.protocol!=='app:'||location.hostname!=='-'||editors.length!==1||editors[0]!==this)throw Error('Editor changed');
              const active=[...document.querySelectorAll('[data-app-action-sidebar-thread-id][aria-current=page],a[aria-current=page][href],[data-thread-id][aria-selected=true]')]
                .filter(e=>visible(e)&&!['false',''].includes(e.getAttribute('aria-current')));
              const keys=active.map(e=>e.getAttribute('data-app-action-sidebar-thread-id')||e.getAttribute('data-thread-id')||e.getAttribute('href')||'')
                .filter(v=>/thread|conversation|[0-9a-f]{8}-[0-9a-f-]{27}/i.test(v));
              const unique=[...new Set(keys)];
              if(unique.length!==1)throw Error('No unique selected conversation');
              const conversation=unique[0],title=document.title;
              if(expected&&(expected.Conversation!==conversation||expected.Title!==title||this.getAttribute('data-car-editor')!==expected.Editor))throw Error('Conversation changed');
              if(!expected&&mode==='bind'&&!this.getAttribute('data-car-editor'))this.setAttribute('data-car-editor',crypto.randomUUID());
              const name=b=>(b.getAttribute('aria-label')||b.getAttribute('title')||b.textContent||'').trim();
              const sendName=n=>/^(send|send message|发送|发送消息)$/i.test(n);
              const stopName=n=>/^(stop|stop generating|停止|停止生成)$/i.test(n);
              const resumeName=n=>/^(resume|resume generation|continue|continue generating|恢复|继续|恢复运行|继续运行|继续生成)$/i.test(n);
              const voiceName=n=>/^(start voice mode|启动语音模式|语音模式)$/i.test(n);
              let container=this.closest('form');
              if(!container){let p=this.parentElement;for(let i=0;p&&i<10&&p!==document.body;i++,p=p.parentElement){
                const bs=[...p.querySelectorAll('button')].filter(visible);
                if(bs.length<=14&&bs.some(b=>sendName(name(b))||stopName(name(b))||resumeName(name(b))||voiceName(name(b)))){container=p;break;}}}
              if(!container)throw Error('No composer container');
              const buttons=[...container.querySelectorAll('button')].filter(visible);
              const enabled=buttons.filter(b=>!b.disabled&&b.getAttribute('aria-disabled')!=='true');
              const primary=enabled.filter(b=>stopName(name(b))||resumeName(name(b))||sendName(name(b)));
              const state=primary.length===1?(stopName(name(primary[0]))?'Running':resumeName(name(primary[0]))?'Resume':'Send'):
                primary.length===0&&enabled.filter(b=>voiceName(name(b))).length===1?'Voice':'Unknown';
              const running=state==='Running';
              const unsafe=[...document.querySelectorAll('[role=dialog],[role=alertdialog]')].some(visible)
                ||[...document.querySelectorAll('button')].some(b=>visible(b)&&!b.disabled&&b.getAttribute('aria-disabled')!=='true'&&/^(skip|submit|跳过|提交|close voice mode|退出语音模式|remove attachment|remove file|移除附件|移除文件)$/i.test(name(b)))
                ||buttons.some(b=>/remove.*(file|attachment)|移除|删除附件|close voice|退出语音|submit|提交/i.test(name(b)))
                ||[...container.querySelectorAll('input[type=file]')].some(e=>e.files&&e.files.length>0)
                ||[...container.querySelectorAll('[data-testid*=attachment],[data-testid*=question],[data-testid*=voice],input[type=file]')].some(e=>visible(e)&&e.tagName!=='INPUT');
              const view=readInput&&this.pmViewDesc&&this.pmViewDesc.view;
              const text=!readInput?null:view&&view.dom===this&&view.state&&view.state.doc&&typeof view.state.doc.textBetween==='function'
                ?view.state.doc.textBetween(0,view.state.doc.content.size,'\n'):(this.innerText||'').replace(/\r\n/g,'\n');
              if(mode==='focus'){
                if(unsafe||running||text!=='')throw Error('Unsafe focus');
                this.focus({preventScroll:true});const r=document.createRange();r.selectNodeContents(this);r.collapse(false);const s=getSelection();s.removeAllRanges();s.addRange(r);
              }
              if(mode==='writeCheck'&&(unsafe||running||text!==''||document.activeElement!==this))throw Error('Unsafe write');
              if(mode==='send'){
                if(unsafe||running||text!==sample)throw Error('Draft changed');
                const send=buttons.filter(b=>sendName(name(b))&&!b.disabled&&b.getAttribute('aria-disabled')!=='true');
                if(send.length!==1)throw Error('No unique enabled send button');send[0].click();
              }
              if(mode==='resume'||mode==='pause'){
                if(unsafe||(mode==='resume'&&state!=='Resume')||(mode==='pause'&&state!=='Running'))throw Error('State changed');
                const action=buttons.filter(b=>(mode==='resume'?resumeName(name(b)):stopName(name(b)))&&!b.disabled&&b.getAttribute('aria-disabled')!=='true');
                if(action.length!==1)throw Error('No unique composer action');action[0].click();
              }
              const messages=readInput?[...document.querySelectorAll('[data-message-author-role=user],[data-testid=user-message],[data-message-role=user],[data-role=user],[class~=bg-user-message][class~=text-user-message]')].filter(visible):[];
              const matches=messages.filter(e=>!container.contains(e)&&(e.innerText||e.textContent).trim()===sample).length;
              return {conversation,title,state,editor:this.getAttribute('data-car-editor'),safe:!unsafe,running,empty:text==='',matches:text===sample,editorActive:document.activeElement===this,messageCount:messages.length?matches:-1};
            }";
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
            return new ComposerReply { Context = context, Safe = (bool)Get(value, "safe"), Running = (bool)Get(value, "running"), Empty = (bool)Get(value, "empty"),
                State = (ButtonState)Enum.Parse(typeof(ButtonState), (string)Get(value, "state")), BackgroundWritable = true, ObservedUtc = DateTime.UtcNow };
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
        public void ChangeState(ButtonState expected) { Operation(expected == ButtonState.Resume ? "resume" : "pause", ""); }
        internal void Navigate(ScopeReply proof, ScopeEntry destination, bool expand, uint tick, Action reserve)
        {
            var state = Operation("inspect", "");
            if (!(bool)Get(state, "safe")) throw new ComposerUnavailable("navigation-needs-user");
            using (var native = new AccessibleComposer(request, false))
                if (native.Context.Conversation != context.NativeBinding || native.Context.Title != proof.CurrentTitle) throw new InvalidOperationException("Navigation native binding changed");
            if (!ComposerWorker.Valid(request, tick)) throw new InvalidOperationException("Input before navigation");
            const string navigate = @"function(expected,currentTitle,entry,expand){
                const visible=e=>!!(e&&e.getClientRects().length&&getComputedStyle(e).visibility!=='hidden'&&!e.closest('[aria-hidden=true],[inert]'));
                if(!this.isConnected||location.protocol!=='app:'||location.hostname!=='-'||document.title!==expected.Title||document.title!==currentTitle)throw Error('Page changed');
                if([...document.querySelectorAll('[role=dialog],[role=alertdialog],[data-testid*=question]')].some(visible)
                    ||[...document.querySelectorAll('button')].some(b=>visible(b)&&!b.disabled&&b.getAttribute('aria-disabled')!=='true'
                        &&/^(skip|submit|跳过|提交|close voice mode|退出语音模式|remove attachment|remove file|移除附件|移除文件)$/i.test((b.getAttribute('aria-label')||b.getAttribute('title')||b.textContent||'').trim())))throw Error('Navigation needs user');
                const rows=[...document.querySelectorAll('[data-app-action-sidebar-thread-id]')].filter(visible);
                const active=rows.filter(e=>e.getAttribute('aria-current')==='page');
                if(active.length!==1||active[0].getAttribute('data-app-action-sidebar-thread-id')!==expected.Conversation)throw Error('Selected conversation changed');
                let action;
                if(expand){
                    const groups=[...document.querySelectorAll('[data-sidebar-project-container-id]')].filter(e=>visible(e)&&e.getAttribute('aria-label')===entry.Title);
                    if(groups.length!==1)throw Error('Project changed');
                    const bs=[...groups[0].querySelectorAll('button[aria-expanded=false]')].filter(e=>visible(e)&&e.closest('[data-sidebar-project-container-id]')===groups[0]);
                    if(bs.length!==1)throw Error('Expand control unavailable');action=bs[0];
                }else{
                    const targets=rows.filter(e=>e.getAttribute('data-app-action-sidebar-thread-title')===entry.Title
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
