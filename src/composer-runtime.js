function(mode, sample, expected, readInput) {
  const visible = e => !!(e && e.isConnected && e.getClientRects().length && getComputedStyle(e).visibility !== 'hidden' && !e.closest('[aria-hidden=true],[inert]'));
  const name = e => {
    const ids = (e.getAttribute('aria-labelledby') || '').split(/\s+/).filter(Boolean);
    return ((ids.length ? ids.map(id => document.getElementById(id)?.textContent || '').join(' ') : '') || e.getAttribute('aria-label') || e.getAttribute('title') || e.textContent || '').trim();
  };
  const sendName = n => /^(send|send message|send prompt|发送|发送消息|发送提示)$/i.test(n);
  const stopName = n => /^(stop|stop generating|stop response|停止|停止生成|停止响应)$/i.test(n);
  const resumeName = n => /^(resume|resume generation|continue|continue generating|恢复|继续|恢复运行|继续运行|继续生成)$/i.test(n);
  const voiceName = n => /^(start voice mode|start voice chat|开启语音聊天|启动语音模式|语音模式)$/i.test(n);
  const buttonState = b => {
    const n = name(b), id = b.getAttribute('data-testid') || '';
    if (stopName(n) || /^(stop-button|stop-generating-button)$/.test(id)) return 'Running';
    if (resumeName(n) || /^(resume-button|continue-button)$/.test(id)) return 'Resume';
    if (sendName(n) || id === 'send-button') return 'Send';
    return 'Unknown';
  };
  const allEditors = [...document.querySelectorAll('.ProseMirror')].filter(visible);
  let editors = allEditors.filter(e => e.closest('[data-thread-find-composer],[data-testid=composer]'));
  if (!editors.length) editors = allEditors.filter(e => /^(Work with Codex|和 Codex 一起工作)$/i.test(name(e)));
  if (!editors.length) editors = allEditors.filter(e => e.closest('form'));
  if (!editors.length) editors = allEditors;
  if (editors.length !== 1) throw Error('main-editor-ambiguous:' + editors.length);
  const editor = editors[0];
  if (mode === 'locate') return editor;
  if (!this.isConnected || this !== editor || location.protocol !== 'app:' || location.hostname !== '-') throw Error('Editor changed');
  let container = editor.closest('[data-thread-find-composer],form,[data-testid=composer]');
  if (!container) for (let p = editor.parentElement; p && p !== document.body; p = p.parentElement) {
    if ([...p.querySelectorAll('button,[role=button]')].some(b => visible(b) && (buttonState(b) !== 'Unknown' || voiceName(name(b))))) { container = p; break; }
  }
  if (!container) throw Error('composer-region-unavailable');
  // These metadata anchors can have zero size. They identify the chat, not a screen coordinate.
  const metadataSelector = '[data-above-composer-conversation-id]';
  // Metadata itself can be zero-sized or hidden. Reject inactive ancestor
  // views, and prefer the nearest chat region belonging to the main editor.
  const activeMetadata = e => {
    if (e.closest('[aria-hidden=true],[inert]')) return false;
    for (let p = e.parentElement; p && p !== document.body; p = p.parentElement)
      if (getComputedStyle(p).display === 'none' || getComputedStyle(p).visibility === 'hidden') return false;
    return true;
  };
  let anchors = [];
  for (let p = container; p && p !== document.body; p = p.parentElement) {
    const local = [...p.querySelectorAll(metadataSelector)].filter(activeMetadata);
    if (local.length) { anchors = local; break; }
  }
  if (!anchors.length) anchors = [...document.querySelectorAll(metadataSelector)].filter(activeMetadata);
  const metadata = anchors
    .map(e => e.getAttribute('data-above-composer-conversation-id')).filter(Boolean);
  const selected = [...document.querySelectorAll('[data-app-action-sidebar-thread-id][aria-current=page],a[aria-current=page][href],[data-thread-id][aria-selected=true]')]
    .filter(e => e.hasAttribute('data-app-action-sidebar-thread-id') || e.hasAttribute('data-thread-id') || /\/(?:threads?|conversations?|chats?)\//.test(e.getAttribute('href') || ''))
    .filter(visible).map(e => e.getAttribute('data-app-action-sidebar-thread-id') || e.getAttribute('data-thread-id') || e.getAttribute('href')).filter(Boolean);
  const canonical = value => (value.match(/[0-9a-f]{8}-[0-9a-f-]{27}/i)?.[0] || value).toLowerCase();
  const metadataIds = [...new Set(metadata.map(canonical))], selectedIds = [...new Set(selected.map(canonical))];
  const ids = metadataIds.length ? metadataIds : selectedIds;
  if (ids.length !== 1) throw Error('conversation-identity-ambiguous:' + ids.length);
  if (metadataIds.length && selectedIds.length && (selectedIds.length !== 1 || selectedIds[0] !== ids[0])) throw Error('conversation-identity-conflict');
  const conversation = ids[0], title = document.title;
  if (expected && (expected.Conversation !== conversation || editor.__carEditorToken !== expected.Editor)) throw Error('Conversation changed');
  if (!expected && mode === 'bind' && !editor.__carEditorToken) editor.__carEditorToken = crypto.randomUUID();
  const buttons = [...container.querySelectorAll('button,[role=button]')].filter(visible);
  const enabled = b => !b.disabled && b.getAttribute('aria-disabled') !== 'true';
  const primary = buttons.filter(b => enabled(b) && buttonState(b) !== 'Unknown');
  let state = primary.length === 1 ? buttonState(primary[0]) : primary.length === 0 && buttons.some(b => enabled(b) && voiceName(name(b))) ? 'Voice' : 'Unknown';
  const questionRegions = [...document.querySelectorAll('[data-testid*=question],[data-testid*=elicitation],[data-question-id]')]
    .filter(e => visible(e) && !e.closest('[data-message-author-role=user],[data-local-conversation-user-anchor]'));
  const question = questionRegions.some(e => [...e.querySelectorAll('button')].some(b => enabled(b) && /^(submit|skip|提交|跳过)$/i.test(name(b))));
  const contentUnsafe = [...document.querySelectorAll('[role=dialog],[role=alertdialog]')].some(visible)
    || buttons.some(b => /remove.*(file|attachment)|移除|删除附件|close voice|退出语音|submit|提交/i.test(name(b)))
    || [...container.querySelectorAll('input[type=file]')].some(e => e.files && e.files.length)
    || [...container.querySelectorAll('[data-testid*=attachment],[data-testid*=voice]')].some(visible);
  const unsafe = question || !!readInput && contentUnsafe;
  // Retry belongs to a transcript error item, never the composer, browser, or an unrelated callout.
  const scrolls = [...document.querySelectorAll('.thread-scroll-container')].filter(e => !e.closest('[aria-hidden=true],[inert]'));
  const transcript = scrolls.length === 1 ? scrolls[0] : editor.closest('main');
  const userContent = '[data-local-conversation-user-anchor],[data-message-author-role=user]';
  const errorItems = transcript ? [...transcript.querySelectorAll('[data-content-search-unit-key]')].filter(e => !container.contains(e)
    && !e.closest(userContent) && !e.querySelector(userContent)
    && [...e.querySelectorAll('aside')].some(a => (a.textContent || '').includes('Selected model is at capacity'))) : [];
  const errorUnits = errorItems.filter(e => !errorItems.some(other => other !== e && e.contains(other)));
  const units = transcript ? [...transcript.querySelectorAll('[data-content-search-unit-key]')] : [];
  const latestErrors = errorUnits.filter(unit => !units.some(u => !unit.contains(u) && !u.contains(unit) && (unit.compareDocumentPosition(u) & Node.DOCUMENT_POSITION_FOLLOWING)));
  const retries = latestErrors.flatMap(unit => [...unit.querySelectorAll('aside button,aside [role=button]')].filter(b => visible(b) && enabled(b)
    && /^(重试|Retry|Retry in \d+s|\d+\s*秒后重试)$/i.test(name(b))).map(button => ({unit,button})));
  const running = state === 'Running';
  let retry = !running && retries.length === 1 ? retries[0] : null;
  let action = primary.length === 1 ? primary[0] : null;
  if (retry) { state = 'Resume'; action = retry.button; }
  const actionSource = retry ? 'capacity-retry' : 'composer';
  let actionToken = null;
  if (action) {
    if (!action.__carActionToken) action.__carActionToken = crypto.randomUUID();
    actionToken = action.__carActionToken + (retry ? ':' + retry.unit.getAttribute('data-content-search-unit-key') : '');
  }
  if (mode === 'resume' || mode === 'pause') {
    if (unsafe || (mode === 'resume' ? state !== 'Resume' : state !== 'Running') || !action) throw Error('State changed');
    if (expected && expected.ActionToken && expected.ActionToken !== actionToken) throw Error('Action replaced');
    action.click();
  }
  const view = readInput && editor.pmViewDesc && editor.pmViewDesc.view;
  const text = !readInput ? null : view && view.dom === editor && view.state?.doc && typeof view.state.doc.textBetween === 'function'
    ? view.state.doc.textBetween(0, view.state.doc.content.size, '\n') : (editor.innerText || '').replace(/\r\n/g, '\n');
  if (mode === 'focus') {
    if (unsafe || running || text !== '') throw Error('Unsafe focus');
    editor.focus({preventScroll:true}); const range=document.createRange();range.selectNodeContents(editor);range.collapse(false);const selection=getSelection();selection.removeAllRanges();selection.addRange(range);
  }
  if (mode === 'writeCheck' && (unsafe || running || text !== '' || document.activeElement !== editor)) throw Error('Unsafe write');
  if (mode === 'send') {
    if (unsafe || running || text !== sample) throw Error('Draft changed');
    const sends=buttons.filter(b=>enabled(b)&&sendName(name(b)));if(sends.length!==1)throw Error('No unique enabled send button');sends[0].click();
  }
  const messages=readInput?[...document.querySelectorAll('[data-message-author-role=user],[data-testid=user-message],[data-message-role=user],[data-role=user],[class~=bg-user-message][class~=text-user-message]')].filter(visible):[];
  const matches=messages.filter(e=>!container.contains(e)&&(e.innerText||e.textContent).trim()===sample).length;
  return {conversation,title,state,editor:editor.__carEditorToken,actionToken,actionSource,safe:!unsafe,blockReason:question?'current-task-question':readInput&&contentUnsafe?'content-operation-blocked':null,
    running:state==='Running',empty:text==='',matches:text===sample,editorActive:document.activeElement===editor,messageCount:messages.length?matches:-1};
}
