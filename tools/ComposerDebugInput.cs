using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

// Prototype for an already enabled, loopback-only Codex debugging endpoint.
// Does not enable debugging, activate the page, focus the editor or submit text.
internal sealed class ComposerDebugInput : IDisposable
{
    readonly JavaScriptSerializer json = new JavaScriptSerializer();
    readonly ClientWebSocket socket = new ClientWebSocket();
    readonly CancellationTokenSource lifetime = new CancellationTokenSource(20000);
    int serial;
    string editorId, title;

    internal ComposerDebugInput(int port)
    {
        try
        {
            string list;
            using (HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) })
                list = http.GetStringAsync("http://127.0.0.1:" + port + "/json/list").GetAwaiter().GetResult();
            var targets = json.Deserialize<List<Dictionary<string, object>>>(list);
            Dictionary<string, object> page = null;
            foreach (var target in targets)
                if (Get(target, "type") as string == "page" && Get(target, "url") as string == "app://-/index.html")
                { if (page != null) throw new InvalidOperationException("Multiple main app pages"); page = target; }
            if (page == null) throw new InvalidOperationException("No main Codex app page at the existing debugging endpoint");
            title = Get(page, "title") as string;
            Uri address = new Uri((string)Get(page, "webSocketDebuggerUrl"));
            if (address.Scheme != "ws" || !address.IsLoopback || address.Port != port) throw new InvalidOperationException("Debugger is not the expected loopback endpoint");
            socket.ConnectAsync(address, lifetime.Token).GetAwaiter().GetResult();
            var response = Call("Runtime.evaluate", new Dictionary<string, object> {
                { "expression", "(()=>{if(location.href!=='app://-/index.html')throw Error('Wrong page');const es=document.querySelectorAll('.ProseMirror[contenteditable=true]');if(es.length!==1)throw Error('Ambiguous composer');return es[0]})()" },
                { "returnByValue", false }
            });
            CheckException(response);
            editorId = (string)Get((Dictionary<string, object>)Get(response, "result"), "objectId");
            if (String.IsNullOrEmpty(editorId)) throw new InvalidOperationException("Composer object missing");
        }
        catch { Dispose(); throw; }
    }

    static object Get(Dictionary<string, object> data, string key)
    { object value; return data.TryGetValue(key, out value) ? value : null; }

    Dictionary<string, object> Call(string method, Dictionary<string, object> parameters)
    {
        int id = ++serial;
        byte[] request = Encoding.UTF8.GetBytes(json.Serialize(new { id = id, method = method, @params = parameters }));
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
        {
            deadline.CancelAfter(4000);
            socket.SendAsync(new ArraySegment<byte>(request), WebSocketMessageType.Text, true, deadline.Token).GetAwaiter().GetResult();
            byte[] buffer = new byte[4096];
            for (int skipped = 0; skipped < 64; skipped++)
            using (MemoryStream message = new MemoryStream())
            {
                WebSocketReceiveResult part;
                do
                {
                    part = socket.ReceiveAsync(new ArraySegment<byte>(buffer), deadline.Token).GetAwaiter().GetResult();
                    if (part.MessageType != WebSocketMessageType.Text || message.Length + part.Count > 1024 * 1024)
                        throw new InvalidOperationException("Unexpected debugger message");
                    message.Write(buffer, 0, part.Count);
                } while (!part.EndOfMessage);
                var reply = json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(message.ToArray()));
                if (Get(reply, "id") == null || Convert.ToInt32(Get(reply, "id")) != id) continue;
                if (Get(reply, "error") != null) throw new InvalidOperationException("Debugger request rejected");
                return (Dictionary<string, object>)Get(reply, "result");
            }
            throw new InvalidOperationException("Too many unrelated debugger messages");
        }
    }

    static void CheckException(Dictionary<string, object> response)
    { if (Get(response, "exceptionDetails") != null) throw new InvalidOperationException("Composer context validation or editor operation failed"); }

    internal Dictionary<string, object> State()
    { return Operation("inspect", ""); }

    internal void Write(string sample)
    {
        if (String.IsNullOrWhiteSpace(sample) || sample.Length > 2048) throw new ArgumentException("A short nonempty single-line draft is required");
        foreach (char c in sample) if (Char.IsControl(c) || c == '\u2028' || c == '\u2029') throw new ArgumentException("Multiline/control-character input has not been validated by this prototype");
        Operation("prepareWrite", sample);
        Call("Input.insertText", new Dictionary<string, object> { { "text", sample } });
    }

    internal void Clear(string sample)
    {
        Operation("prepareClear", sample);
        Call("Input.dispatchKeyEvent", new Dictionary<string, object> { { "type", "keyDown" }, { "key", "Backspace" }, { "code", "Backspace" }, { "windowsVirtualKeyCode", 8 } });
        Call("Input.dispatchKeyEvent", new Dictionary<string, object> { { "type", "keyUp" }, { "key", "Backspace" }, { "code", "Backspace" }, { "windowsVirtualKeyCode", 8 } });
    }

    internal bool Matches(string sample)
    {
        var state = Operation("match", sample);
        return Get(state, "matches") is bool && (bool)Get(state, "matches");
    }

    Dictionary<string, object> Operation(string mode, string sample)
    {
        // Bind to the same DOM node and page title throughout. Input.insertText
        // uses Chromium's normal editing path; never mutate innerText or depend
        // on private ProseMirror state. Selection is limited to our own draft.
        string function = @"function(mode,sample,title){
          if(!this.isConnected||location.href!=='app://-/index.html'||document.title!==title||document.querySelectorAll('.ProseMirror[contenteditable=true]').length!==1||document.querySelector('.ProseMirror[contenteditable=true]')!==this)throw Error('Context changed');
          const text=this.textContent;
          if(mode==='prepareWrite'){
            if(document.hasFocus()||text!==''||document.activeElement!==this)throw Error('Not an empty background editor');
            const s=getSelection();if(!s||!s.isCollapsed||!this.contains(s.anchorNode))throw Error('Unexpected editor selection');
          }else if(mode==='prepareClear'){
            if(text!==sample)throw Error('Draft changed');
            if(document.activeElement!==this)throw Error('Editor selection changed');
            const r=document.createRange();r.selectNodeContents(this);const s=getSelection();s.removeAllRanges();s.addRange(r);
          }
          return {textLength:this.textContent.length,editorActive:document.activeElement===this,hasFocus:document.hasFocus(),matches:this.textContent===sample};
        }";
        var response = Call("Runtime.callFunctionOn", new Dictionary<string, object> {
            { "objectId", editorId }, { "functionDeclaration", function }, { "returnByValue", true },
            { "arguments", new object[] { new { value = mode }, new { value = sample }, new { value = title } } }
        });
        CheckException(response);
        return (Dictionary<string, object>)Get((Dictionary<string, object>)Get(response, "result"), "value");
    }

    public void Dispose()
    { socket.Dispose(); lifetime.Dispose(); }
}
