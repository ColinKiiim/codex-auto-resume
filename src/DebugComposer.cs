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
                string list;
                using (var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(3) })
                    list = http.GetStringAsync("http://127.0.0.1:" + port + "/json/list").GetAwaiter().GetResult();
                var pages = json.Deserialize<List<Dictionary<string, object>>>(list);
                Dictionary<string, object> page = null;
                foreach (var p in pages)
                    if (Get(p, "type") as string == "page" && (Get(p, "url") as string ?? "").StartsWith("app://-/index.html", StringComparison.Ordinal))
                    { if (page != null) throw new InvalidOperationException(); page = p; }
                if (page == null) throw new InvalidOperationException();
                Uri endpoint = new Uri((string)Get(page, "webSocketDebuggerUrl"));
                if (endpoint.Scheme != "ws" || !endpoint.IsLoopback || endpoint.Port != port) throw new InvalidOperationException();
                socket.ConnectAsync(endpoint, lifetime.Token).GetAwaiter().GetResult();
                var response = Call("Runtime.evaluate", new Dictionary<string, object> {
                    { "expression", "(()=>{const es=document.querySelectorAll('.ProseMirror[contenteditable=true]');if(es.length!==1)throw Error();return es[0]})()" }, { "returnByValue", false } });
                CheckException(response);
                editorId = (string)Get((Dictionary<string, object>)Get(response, "result"), "objectId");
                context = request.Context;
                var state = Operation("bind", "");
                var found = new ComposerContext { Backend = "CDP", Port = port, Conversation = (string)Get(state, "conversation"), Editor = (string)Get(state, "editor"), Title = (string)Get(state, "title") };
                if (context != null && (context.Conversation != found.Conversation || context.Editor != found.Editor || context.Title != found.Title)) throw new InvalidOperationException();
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
            const string function = @"function(mode,sample,expected){
              if(!this.isConnected||location.protocol!=='app:'||location.hostname!=='-'
                ||document.querySelectorAll('.ProseMirror[contenteditable=true]').length!==1
                ||document.querySelector('.ProseMirror[contenteditable=true]')!==this)throw Error('Editor changed');
              const visible=e=>!!(e&&e.getClientRects().length&&!e.closest('[aria-hidden=true],[inert]'));
              const active=[...document.querySelectorAll('a[aria-current],a[data-active=true],a[data-state=active],[data-thread-id][aria-selected=true]')]
                .filter(e=>visible(e)&&!['false',''].includes(e.getAttribute('aria-current')));
              const keys=active.map(e=>e.getAttribute('data-thread-id')||e.getAttribute('href')||'')
                .filter(v=>/thread|conversation|[0-9a-f]{8}-[0-9a-f-]{27}/i.test(v));
              const unique=[...new Set(keys)];
              if(unique.length!==1)throw Error('No unique selected conversation');
              const conversation=unique[0],title=document.title;
              if(expected&&(expected.Conversation!==conversation||expected.Title!==title||this.getAttribute('data-car-editor')!==expected.Editor))throw Error('Conversation changed');
              if(!expected&&mode==='bind')this.setAttribute('data-car-editor',crypto.randomUUID());
              const name=b=>(b.getAttribute('aria-label')||b.getAttribute('title')||b.textContent||'').trim();
              const sendName=n=>/^(send|send message|发送|发送消息)$/i.test(n);
              const stopName=n=>/^(stop|stop generating|停止|停止生成)$/i.test(n);
              let container=this.closest('form');
              if(!container){let p=this.parentElement;for(let i=0;p&&i<4;i++,p=p.parentElement){
                if([...p.querySelectorAll('button')].some(b=>sendName(name(b))||stopName(name(b)))){container=p;break;}}}
              if(!container)throw Error('No composer container');
              const buttons=[...container.querySelectorAll('button')].filter(visible);
              const running=buttons.some(b=>stopName(name(b))&&!b.disabled);
              const unsafe=[...document.querySelectorAll('[role=dialog],[role=alertdialog]')].some(visible)
                ||buttons.some(b=>/remove.*(file|attachment)|移除|删除附件|close voice|退出语音|submit|提交/i.test(name(b)))
                ||[...container.querySelectorAll('input[type=file]')].some(e=>e.files&&e.files.length>0)
                ||[...container.querySelectorAll('[data-testid*=attachment],[data-testid*=question],[data-testid*=voice],input[type=file]')].some(e=>visible(e)&&e.tagName!=='INPUT');
              const text=this.textContent;
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
              const messages=[...document.querySelectorAll('[data-message-author-role=user],[data-testid=user-message],[data-message-role=user],[data-role=user]')];
              const matches=messages.filter(e=>!container.contains(e)&&e.textContent.trim()===sample).length;
              return {conversation,title,editor:this.getAttribute('data-car-editor'),safe:!unsafe,running,empty:text==='',matches:text===sample,editorActive:document.activeElement===this,messageCount:messages.length?matches:-1};
            }";
            var reply = Call("Runtime.callFunctionOn", new Dictionary<string, object> {
                { "objectId", editorId }, { "functionDeclaration", function }, { "returnByValue", true },
                { "arguments", new object[] { new { value = mode }, new { value = sample }, new { value = context } } } });
            CheckException(reply);
            return (Dictionary<string, object>)Get((Dictionary<string, object>)Get(reply, "result"), "value");
        }
        public ComposerContext Context { get { return context; } }
        public ComposerReply Inspect()
        {
            var value = Operation("inspect", "");
            return new ComposerReply { Context = context, Safe = (bool)Get(value, "safe"), Running = (bool)Get(value, "running"), Empty = (bool)Get(value, "empty") };
        }
        public void Focus(bool background)
        {
            IntPtr before = Native.GetForegroundWindow();
            var state = Operation("focus", "");
            if (!(bool)Get(state, "editorActive") || background && Native.GetForegroundWindow() != before) throw new InvalidOperationException();
        }
        public void Write(string text)
        {
            Operation("writeCheck", ""); written = text;
            Call("Input.insertText", new Dictionary<string, object> { { "text", text } });
            if (!(bool)Get(Operation("inspect", text), "matches")) throw new InvalidOperationException();
        }
        public int MessageCount(string text) { return Convert.ToInt32(Get(Operation("inspect", text), "messageCount")); }
        public void Send() { Operation("send", written); }
        public void Dispose() { socket.Dispose(); lifetime.Dispose(); }
    }
}
