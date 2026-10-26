# 2026-10-26 不抢焦点后台输入原型

## 已完成的范围

用户在上一轮工具测试之后明确要求尝试原生实现，并在本轮同意短暂停止鼠标键盘操作进行实机验证。本轮新增独立的 .NET Framework / C# 5 原型及显式调用的诊断入口；没有接入 CAR 监测循环，没有改变发布版本，也没有发送聊天消息。

- 核心：[ComposerDebugInput.cs](../../tools/ComposerDebugInput.cs)
- 受控遮挡与焦点检测：[ComposerInputProbe.cs](../../tools/ComposerInputProbe.cs)
- 编译和调用：[probe-composer-input.ps1](../../tools/probe-composer-input.ps1)

## 实机结果

测试目标为本机已安装的 OpenAI.Codex 26.1016.8622.0 Windows x64 应用。先前日志中 EXE 的 `155.0.8059.80` 是 Chromium 文件版本，不是 Codex 应用包版本。

| 路径 | 实际观察 | 结论 |
|---|---|---|
| 原生 .NET UIA `ValuePattern.SetValue` | 半透明遮挡对照中可写入并读回，但前台窗口和键盘焦点改变；鼠标位置和真实输入 tick 不变 | 不满足无焦点要求；已排除仅由上一轮工具自动激活造成的解释 |
| UIA 完全不透明遮挡 | 观察期内读回旧内容，关闭遮挡后出现更新；写入和清空结果存在延迟 | 不按 API 返回成功计为完成；不能据此保证后台路径 |
| 向渲染子窗口定向投递 `WM_CHAR` | 前台、焦点、鼠标、真实输入 tick 均保持，但输入框未读回测试文字 | 该次探测不通过 |
| 已有本机 CDP 接口的 `Input.insertText` | Codex 被独立白色窗口完全遮挡；写入中文、DOM 完整读回、精确清空全部通过；四项输入证据保持不变 | 本机无焦点后台写入原型通过 |

CDP 路径整理前后各完成一次完全遮挡实测。成功输出包括：

```text
AfterCdpInput: ForegroundSame=True; FocusSame=True; PointerSame=True; LastInputSame=True
EditorDomReadback=True
TestDraftCleared=True
AfterCleanup: ForegroundSame=True; FocusSame=True; PointerSame=True; LastInputSame=True
PASS: background CDP input; OpaqueCover=True; no focus change or chat submission
```

结束后重新只读查询，编辑器文本长度为 0，没有遗留探针进程。原生 UIA 首轮曾因立即读回误判清空完成，随后发现临时草稿仍在；已单独清理并确认空白。该失败没有计为通过，最终保留的 CDP 原型使用实际读回确认。

## 实现要点

当前运行的 Codex 命令行已经包含本机调试端口，本次实际为 `12166`。程序没有开启调试、更改配置或重启 Codex。脚本先检查端口监听进程属于已安装的 Codex，再连接 `127.0.0.1`；不把本机端口硬编码为通用规则。

原型通过 `/json/list` 找到唯一的 `app://-/index.html` 主页面，经该页面的 WebSocket 会话定位唯一的 `.ProseMirror[contenteditable=true]`。绑定原 DOM 节点并复核页面标题、连接状态、输入框唯一性、空白内容、页内活动编辑器和选区，避免把输入路由到其他编辑区域。

实际写入使用 Chromium 的 [Input.insertText](https://chromedevtools.github.io/devtools-protocol/tot/Input/#method-insertText)，不直接修改 `innerText`，也不依赖私有 ProseMirror `EditorView` 对象；这样经过编辑器正常的输入路径。清理只选择严格匹配本次随机测试文案的节点内容，并向该页面发送 Backspace 按键事件。没有 Enter、点击发送、全局 `SendInput`、剪贴板、目标窗口激活或目标 `.focus()` 调用。

独立遮挡窗口是测试夹具，测试时主动将它置于前台；“不抢焦点”指随后对 Codex 的写入、观察和清空均不改变这个前台窗口及其键盘焦点。清空后遮挡窗口自动关闭，不以窗口关闭后的焦点恢复掩盖测试中的焦点变化。

## 使用方式与限制

```powershell
# 端口必须来自当前实际运行的 Codex，以下是本机本次值
.\tools\probe-composer-input.ps1 -DebugPort 12166
# 仅在用户明确同意测试且输入框为空时执行
.\tools\probe-composer-input.ps1 -DebugPort 12166 -WriteAndClear
```

- 默认只读；写入模式是手动诊断入口，不加入默认构建回归或自动额度流程
- 依赖目标已有可用的本机 CDP 接口；没有接口时不自动开启、不默默重启，不能宣称默认跨设备可用
- 本次只验证单行中文与 Unicode 文案；多行、控制字符和过长文案在原型中拒绝，不能把接口理论支持当作实测完成
- 当前要求输入框仍是页面内部的活动编辑器且选区位于其中；这与整个 Codex 窗口是否在操作系统前台是两件事。原型没有为任意失焦输入框自动取得页内焦点
- 页面标题和原 DOM 节点绑定仅用于本次诊断，尚未完成自动恢复所需的可靠对话身份、附件/问答/语音保护及用户干预竞态处理
- 当前未测试真正发送、服务端接受请求、额度恢复完整闭环、应用重启后的状态恢复或第二台实体电脑
- API 异常、文本被用户改变或结果不明时不覆盖用户内容、不重复发送；产品接入仍需完善超时后的事务结果核对和取消清理

此前工具验证保留在 [上一份历史记录](2026-10-26-composer-input-probe.md)。剩余功能接入见 [TODO](../../TODO.md) 中“额度恢复后无暂停按钮时补发继续指令”。
