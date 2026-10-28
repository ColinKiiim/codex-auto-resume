import fs from 'node:fs/promises';
import path from 'node:path';
import {spawn,spawnSync} from 'node:child_process';
import {setTimeout as delay} from 'node:timers/promises';
import net from 'node:net';
const [exe,bridge,chrome,folder,kind='chrome']=process.argv.slice(2);
const checks=[];const require=(yes,label)=>{checks.push({label,pass:!!yes});if(!yes)throw Error(label);console.log('PASS: '+label)};
const native=(...args)=>{const p=spawnSync(bridge,args,{windowsHide:true,timeout:5000,encoding:'utf8'});if(p.status!==0)throw Error('Fixture native helper failed');return JSON.parse(p.stdout)};
async function connect(url){const ws=new WebSocket(url);await new Promise((r,j)=>{ws.addEventListener('open',r,{once:true});ws.addEventListener('error',j,{once:true})});let id=0;const pending=new Map();ws.addEventListener('message',e=>{const v=JSON.parse(e.data);const p=pending.get(v.id);if(p){pending.delete(v.id);clearTimeout(p.t);v.error?p.j(Error(JSON.stringify(v.error))):p.r(v.result)}});return{close:()=>ws.close(),call:(method,params={})=>new Promise((r,j)=>{const n=++id;const t=setTimeout(()=>{pending.delete(n);j(Error('Fixture CDP timeout'))},5000);pending.set(n,{r,j,t});ws.send(JSON.stringify({id:n,method,params}))})}}
const title='CAR owned controls '+Date.now();
const html=`<!doctype html><meta charset=utf-8><title>${title}</title><style>#messages{white-space:pre-wrap}</style><main>
<div role=tablist><button role=tab aria-selected=true>${title}</button></div>
<div role=toolbar aria-label="Chat toolbar"><span id=header>${title}</span><span>Project: Owned project</span></div>
<section role=group class="group/cwd fixture" aria-label="Owned project" data-sidebar-project-container-id="owned-project">
<button aria-label="Owned project" aria-expanded=false onclick="this.setAttribute('aria-expanded','true')">Owned project</button>
${[1,2].map(n=>`<button type=button class="data-[app-action-sidebar-thread-selected=true]" data-app-action-sidebar-thread-id="10000000-0000-0000-0000-00000000000${n}" data-app-action-sidebar-thread-title="${title+(n===1?'':' other')}" ${n===1?'aria-current=page':''} onclick="document.querySelector('[aria-current=page]').removeAttribute('aria-current');this.setAttribute('aria-current','page');document.title=this.textContent;document.querySelector('#header').textContent=this.textContent;document.querySelector('[role=tab]').textContent=this.textContent">${title+(n===1?'':' other')}</button>`).join('')}
</section>
<button aria-label="恢复" onclick="window.wrong=(window.wrong||0)+1">Window-level decoy</button>
<div id=messages><span data-message-author-role=user>seed</span></div>
<form role=group aria-label="Composer" onsubmit="return false">
<div class=ProseMirror role=textbox aria-label="Work with Codex" contenteditable=true></div>
<button id=action type=button aria-label="恢复" onclick="window.count=(window.count||0)+1;this.setAttribute('aria-label',this.getAttribute('aria-label')==='停止'?'恢复':'停止')">action</button>
<button id=send type=button aria-label="Send" hidden onclick="window.sent=(window.sent||0)+1;const s=document.createElement('span');s.dataset.messageAuthorRole='user';s.textContent=document.querySelector('.ProseMirror').innerText;document.querySelector('#messages').append(s);document.querySelector('.ProseMirror').innerHTML='';this.hidden=true;document.querySelector('#action').hidden=false;document.querySelector('#action').setAttribute('aria-label','停止')">send</button>
</form></main>`;
await fs.mkdir(folder,{recursive:true});await fs.writeFile(path.join(folder,'fixture.html'),html);
const profile=path.join(folder,'profile');await fs.mkdir(profile);
let electronPort;
if(kind==='electron'){
 electronPort=await new Promise(r=>{const server=net.createServer();server.listen(0,'127.0.0.1',()=>{const p=server.address().port;server.close(()=>r(p))})});
 await fs.writeFile(path.join(folder,'main.cjs'),`const {app,BrowserWindow,protocol}=require('electron');const fs=require('fs');protocol.registerSchemesAsPrivileged([{scheme:'app',privileges:{standard:true,secure:true,supportFetchAPI:true}}]);app.whenReady().then(()=>{protocol.handle('app',req=>new Response(fs.readFileSync(${JSON.stringify(path.join(folder,'fixture.html'))},'utf8').replaceAll(${JSON.stringify(title)},req.url.includes('other=1')?'Other owned page':${JSON.stringify(title)}),{headers:{'content-type':'text/html; charset=utf-8'}}));const win=new BrowserWindow({width:850,height:600,x:100,y:150});win.loadURL('app://-/index.html');const other=new BrowserWindow({show:false});other.loadURL('app://-/index.html?other=1');setInterval(()=>{if(fs.existsSync(${JSON.stringify(path.join(folder,'stop-cover'))}))app.quit()},100);});app.on('window-all-closed',()=>app.quit());`);
}
const args=kind==='electron'?[path.join(folder,'main.cjs'),`--user-data-dir=${profile}`,`--remote-debugging-port=${electronPort}`]:[`--user-data-dir=${profile}`,'--remote-debugging-port=0','--no-first-run','--no-default-browser-check','--disable-background-networking',`--app=file:///${path.join(folder,'fixture.html').replaceAll('\\','/')}`];
const child=spawn(chrome,args,{windowsHide:true,stdio:['ignore','ignore','pipe']});let runtimeErrors='';child.stderr.on('data',d=>runtimeErrors+=d);
let browser,page,cover,target;const stop=path.join(folder,'stop-cover');
const operation=async(op,context=null,prompt=null)=>{
 const req={...target,Context:context,Operation:op,Prompt:prompt,ExpiresUtc:new Date(Date.now()+22000).toISOString()};delete req.found;
 return await new Promise((resolve,reject)=>{const p=spawn(exe,['--composer-worker'],{windowsHide:true,stdio:['pipe','pipe','pipe']});let out='',err='';const timeout=setTimeout(()=>{p.kill();reject(Error('Production control worker deadline'))},23000);p.stdout.on('data',d=>out+=d);p.stderr.on('data',d=>err+=d);p.on('error',reject);p.on('exit',code=>{clearTimeout(timeout);try{if(code!==0||err)throw Error('Worker failed '+err);const reply=JSON.parse(out);fs.appendFile(path.join(folder,'worker-results.jsonl'),JSON.stringify({op,reply})+'\n').then(()=>resolve(reply),reject)}catch(e){reject(e)}});p.stdin.end(JSON.stringify(req).replace(/[\u0080-\uffff]/g,c=>'\\u'+c.charCodeAt(0).toString(16).padStart(4,'0'))+'\n')});
};
const scope=async request=>await new Promise((resolve,reject)=>{const p=spawn(exe,['--scope-worker'],{windowsHide:true,stdio:['pipe','pipe','pipe']});let out='';const t=setTimeout(()=>{p.kill();reject(Error('Scope worker deadline'))},18000);p.stdout.on('data',d=>out+=d);p.on('exit',code=>{clearTimeout(t);try{if(code!==0)throw Error('Scope worker failed');resolve(JSON.parse(out))}catch(e){reject(e)}});p.stdin.end(JSON.stringify({Target:{...target,Operation:'inspect',ExpiresUtc:new Date(Date.now()+25000).toISOString()},...request})+'\n')});
const evaluate=async expression=>{const r=await page.call('Runtime.evaluate',{expression,returnByValue:true});if(r.exceptionDetails)throw Error('Fixture expression failed: '+r.exceptionDetails.text);return r.result.value};
const idle=async()=>{for(let n=0;n<150;n++){const s=native('state',String(target.Window));if(s.idle>=1800&&!s.held)return s;await delay(100)}throw Error('User input did not become idle; no forced input')};
try{
 let port;for(let n=0;n<80;n++){try{port=electronPort||+(await fs.readFile(path.join(profile,'DevToolsActivePort'),'utf8')).split('\n')[0];if(port){await fetch(`http://127.0.0.1:${port}/json/version`);break}}catch{}await delay(100)}if(!port)throw Error('Fixture runtime unavailable');
 const version=await(await fetch(`http://127.0.0.1:${port}/json/version`)).json();browser=await connect(version.webSocketDebuggerUrl);
 let tab,tabs;for(let n=0;n<50;n++){tabs=await(await fetch(`http://127.0.0.1:${port}/json/list`)).json();tab=tabs.find(t=>t.type==='page'&&t.title===title);if(tab)break;await delay(100)}if(!tab){await fs.writeFile(path.join(folder,'fixture-pages.json'),JSON.stringify(tabs));throw Error('Fixture page missing')}page=await connect(tab.webSocketDebuggerUrl);
 for(let n=0;n<50;n++){target=native('locate',title);if(target.found)break;await delay(100)}if(!target.found)throw Error('Owned fixture window missing');
 cover=spawn(bridge,['cover',String(target.Window),stop],{windowsHide:true,stdio:'ignore'});await delay(700);
 let observed;for(let n=0;n<4;n++){observed=await operation('inspect');if(observed.Result==='Observed'&&(kind!=='electron'||observed.Context?.Backend==='CDP'))break;await delay(300)}require(observed.Result==='Observed'&&observed.State===1&&observed.Context?.Backend===(kind==='electron'?'CDP':'UIA'),'covered composer is bound independently of the window-level Resume decoy; '+observed.Stage);
 if(kind==='electron') require(observed.Context.PageId===tab.id&&(await(await fetch(`http://127.0.0.1:${port}/json/list`)).json()).filter(t=>t.url.startsWith('app:')).length===2,'multiple app pages are resolved by the independent native window and title binding');
 let before=await idle();let result=await operation('resume',observed.Context);let after=native('state',String(target.Window));
 await fs.writeFile(path.join(folder,'covered-action-debug.json'),JSON.stringify({before,after,result,count:await evaluate('window.count||0'),wrong:await evaluate('window.wrong||0'),name:await evaluate(`document.querySelector('#action').getAttribute('aria-label')`)}));
 require(result.Result==='Changed'&&result.Running&&(await evaluate('window.count'))===1&&(await evaluate('window.wrong||0'))===0,'fully covered semantic resume invokes only the composer and confirms running');
 require(before.foreground===after.foreground&&before.x===after.x&&before.y===after.y,'covered state action preserves foreground and pointer');
 native('minimize',String(target.Window));await evaluate(`document.querySelector('#action').setAttribute('aria-label','恢复')`);await delay(400);
 require(native('discover',exe,String(target.Window)).found,'production window enumeration includes an already minimized target');
 observed=await operation('inspect');
 if(kind!=='electron') {require(observed.Result==='Blocked'&&!observed.ActionAttempted&&native('state',String(target.Window)).minimized,'minimized UIA cached state is blocked when no owned CDP endpoint exists');}
 else {
 require(observed.Result==='Observed'&&observed.State===1,'minimized composer can be observed without restoring the window');
 before=await idle();result=await operation('resume',observed.Context);after=native('state',String(target.Window));
 require(result.Result==='Changed'&&result.Running&&after.minimized&&(await evaluate('window.count'))===2,'minimized semantic resume is confirmed while the window remains minimized');
 require(before.foreground===after.foreground&&before.x===after.x&&before.y===after.y,'minimized state action preserves foreground and pointer');
 await idle();let paused=await operation('pause',result.Context);require(paused.Result==='Changed'&&paused.State===1&&native('state',String(target.Window)).minimized,'minimized quota-style pause is confirmed without restoring');
 const forged={...paused.Context,PageId:'wrong-page'};await idle();const wrongPage=await operation('resume',forged);require(wrongPage.Result==='Blocked'&&!wrongPage.ActionAttempted&&(await evaluate('window.count'))===3,'another page cannot authorize a state action');
 await evaluate(`(()=>{const b=document.createElement('button');b.id='duplicate-action';b.setAttribute('aria-label','恢复');document.querySelector('form').append(b)})()`);observed=await operation('inspect');await idle();const ambiguous=await operation('resume',observed.Context);require(observed.State===0&&ambiguous.Result==='Blocked'&&!ambiguous.ActionAttempted&&(await evaluate('window.count'))===3,'ambiguous composer action buttons cannot authorize a click');await evaluate(`document.querySelector('#duplicate-action').remove()`);
 await evaluate(`document.querySelector('#action').setAttribute('aria-label','Start voice mode')`);observed=await operation('inspect');require(observed.State===4&&!observed.Running&&observed.Empty,'idle voice entry remains available to the verified continuation policy');await evaluate(`document.querySelector('#action').setAttribute('aria-label','恢复')`);
 await evaluate(`(()=>{const d=document.createElement('button');d.id='question';d.setAttribute('aria-label','Submit');d.textContent='Owned question outside composer';document.body.append(d)})()`);observed=await operation('inspect');await idle();const question=await operation('resume',observed.Context);require(!observed.Safe&&question.Result==='Blocked'&&!question.ActionAttempted&&(await evaluate('window.count'))===3,'a question action outside the composer blocks semantic recovery');await evaluate(`document.querySelector('#question').remove()`);
 await evaluate(`(()=>{const d=document.querySelector('.ProseMirror').cloneNode(true);d.id='other-editor';d.textContent='second composer';document.body.append(d)})()`);const multiple=await operation('inspect');require(multiple.Result==='Blocked'&&!multiple.ActionAttempted,'multiple visible composers cannot be silently selected');await evaluate(`document.querySelector('#other-editor').remove()`);
 await evaluate(`document.querySelector('#action').hidden=true;document.querySelector('#send').hidden=false;document.querySelector('.ProseMirror').textContent='existing draft'`);
 observed=await operation('inspect');await idle();result=await operation('submit',observed.Context,'do not overwrite');
 require(result.Result==='Blocked'&&(await evaluate(`document.querySelector('.ProseMirror').innerText`))==='existing draft'&&!(await evaluate('window.sent||0')),'minimized submission preserves an existing draft and sends nothing');
 await evaluate(`document.querySelector('.ProseMirror').textContent=''`);observed=await operation('inspect');await idle();
 const text='CAR 后台测试\n第二行';before=native('state',String(target.Window));result=await operation('submit',observed.Context,text);
 if(result.Result==='Blocked'&&result.Stage.includes('input-or-target-changed')&&!(await evaluate('window.sent||0'))&&(await evaluate(`document.querySelector('.ProseMirror').innerText`))==='') {before=await idle();result=await operation('submit',observed.Context,text);}
 after=native('state',String(target.Window));
 require(result.Result==='Submitted'&&(await evaluate('window.sent'))===1&&(await evaluate(`document.querySelector('#messages').lastElementChild.textContent`))===text&&native('state',String(target.Window)).minimized,'minimized background multiline input is read back and sent exactly once');
 require(before.foreground===after.foreground&&before.x===after.x&&before.y===after.y,'minimized text submission preserves foreground and pointer');
 let view=await scope({Operation:'read'});await fs.writeFile(path.join(folder,'scope-read.json'),JSON.stringify(view));require(view.Result==='Observed'&&view.Entries.length===3,'minimized sidebar identity is read from the owned provider');
 before=await idle();const expanded=await scope({Operation:'expand',FromId:view.CurrentId,ProjectId:view.Entries.find(e=>e.Kind==='project').Id,InputTick:native('state',String(target.Window)).tick});
 await fs.writeFile(path.join(folder,'scope-expand.json'),JSON.stringify({before,after:native('state',String(target.Window)),expanded,domExpanded:await evaluate(`document.querySelector('[aria-expanded]').getAttribute('aria-expanded')`)}));require(expanded.Result==='Expanded'&&native('state',String(target.Window)).minimized,'minimized project expansion is confirmed through the native sidebar');
 view=await scope({Operation:'read'});const destination=view.Entries.find(e=>e.Kind==='chat'&&!e.Current);before=await idle();const switched=await scope({Operation:'switch',FromId:view.CurrentId,ToId:destination.Id,ProjectId:destination.ProjectId,InputTick:before.tick});after=native('state',String(target.Window));
 await fs.writeFile(path.join(folder,'scope-switch.json'),JSON.stringify(switched));require(switched.Result==='Switched'&&after.minimized&&before.foreground===after.foreground&&before.x===after.x&&before.y===after.y,'minimized background navigation is confirmed without foreground or pointer changes');
 }
}finally{
 await fs.writeFile(stop,'stop').catch(()=>{});if(page)page.close();if(browser){try{await browser.call('Browser.close')}catch{}browser.close()}
 await Promise.race([new Promise(r=>child.once('exit',r)),delay(1500)]);if(child.exitCode===null)child.kill();
 if(cover){await Promise.race([new Promise(r=>cover.once('exit',r)),delay(2000)]);if(cover.exitCode===null)cover.kill()}
 await fs.writeFile(path.join(folder,'checks.json'),JSON.stringify(checks,null,2));
 await fs.writeFile(path.join(folder,'runtime-errors.txt'),runtimeErrors);
}
