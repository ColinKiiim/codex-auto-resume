# CAR 读取 Codex 侧栏的只读调研

2026-10-27（UTC+8）。用户要求调研并尝试现有 CAR 接口能否读取侧栏聊天标题、区分项目与对话，供后续指定恢复范围使用；本轮为调研与 TODO 记录，没有修改正式恢复逻辑。

## 结论

可以读取标题、项目和归属。现有 CAR 使用的 Windows UI Automation（UIA）已足以读取项目/聊天树；补充原生 UIA 属性读取还能直接确认当前聊天。当前聊天标记和项目展开状态是不同信息，不能将展开、悬停、焦点或背景色视作当前选择。

| 信息 | 本机实测 | 接入含义 |
| --- | --- | --- |
| 项目名称 | 读到截图中的 Yu-Develop、GPT Mini、ChatGPT、Codex-Win 等 | 从项目容器和项目按钮读取，不解析整行拼接名称 |
| 聊天标题 | 读到“优化 CAR 弹窗位置”“分析最近5小时用量读取”“切换到 codexautoresume”“实现 Codex 暂停键自动恢复”等 | `Button.Name` 返回完整可访问标题，截图省略号不限制返回文本；原始标题若含异常字符，也会如实返回 |
| 项目归属 | 项目 `Group` 下有聊天 `List`、`ListItem` 和主聊天 `Button` | 按树结构归属，避免将项目操作、新聊天、置顶、归档按钮算成聊天 |
| 当前聊天 | 原生 `AriaProperties` 返回 `current=page`，本次只有一个聊天命中 | 可与文档标题及聊天工具栏交叉确认；正式 CAR 尚未读取该属性 |
| 当前聊天所属项目 | 聊天工具栏中有 `Image.Name = 项目：Yu-Develop`，与侧栏祖先项目一致 | 能确认当前聊天归属，不代表存在独立的“项目已选中”布尔值 |
| 项目展开状态 | 项目按钮支持 `ExpandCollapsePattern`；原生 ARIA 有 `expanded=true` | 多项目可以同时展开，不能据此决定恢复目标 |
| 标准选中状态 | 本次没有项目/聊天暴露 `SelectionItemPattern`；原生选择属性也不支持 | 不可把不支持当作 `false`，也不可要求所有版本必须支持此模式 |
| 离屏、位置、状态 | 读到 `IsOffscreen`、边界、runtime ID；当前聊天有“处理中”状态 | 离屏不等于不存在；运行状态只作当前快照证据 |
| 稳定身份 | 项目/聊天 `AutomationId` 为空，文档 Value 为 `app://-/index.html`；未从所查属性取得项目路径或对话 UUID | runtime ID 是当前 UI 实例标识，不能作为跨重启稳定 ID；本次发现一组同名项目 |

首次托管 UIA 快照共读取 324 个控件，按所观察结构识别到 8 个项目容器和 24 个聊天主按钮，其中 4 个项目容器、14 个聊天主按钮被标记为离屏。17 个聊天可沿祖先链找到项目容器，其余 7 个没有该祖先证据，不能强行归入最近项目。该数字仅描述本次已暴露的树，不代表全部账号记录，也不能保证折叠、虚拟化、侧栏隐藏后仍能读全。

实测界面的当前聊天为“调研 CAR 接口读取聊天标题”；这是本轮对话新增后的实际标题。截图中的五个 Yu-Develop 历史聊天仍能读取。

## 实测路线与现有实现边界

环境：本机商店版 Codex `26.1021.3705.0`，正式 CAR `1.1.16.0`。没有启用或发现主进程的 remote debugging port/pipe；没有重启或修改 Codex 配置。本轮没有使用 computer use、OCR、内置聊天/项目列表工具、会话数据库或额外 app-server 列表来替代 CAR 的能力。

1. 调用正式 `release/CodexAutoResume.exe --composer-worker`，请求 `Operation=inspect`，原样使用现有身份校验与 UIA 后端。结果为 `Result=Observed`、`Stage=inspect`、`Backend=UIA`、`Running=true`、`Safe=true`、`Empty=false`，标题绑定到本轮对话。此处 `Empty=false` 只记录接口返回，未用其推断真实草稿或输入安全，也未尝试写入。
2. 用独立、限时的临时 .NET 进程沿用 CAR 的 `AutomationElement`、`TreeWalker.ControlViewWalker` 读取树和控件属性，未调用操作模式。项目行与聊天行都是 Button，但项目按钮支持展开模式，聊天在项目子列表中；不能只凭 ControlType 或最近的任意 ListItem 分类。
3. CAR 现用的 .NET Framework 托管 UIA 封装未直接暴露本次需要的 ARIA 属性，另用系统原生 COM UIA 的 `GetCurrentPropertyValueEx(30102, true)` 做只读补充。当前聊天返回 `current=page`，普通聊天及项目按钮没有；项目按钮的 `expanded=true` 与当前聊天标记独立。还检查了标准选择、Legacy state、帮助、描述、Value 与 AutomationId，没有找到可用的稳定业务 ID。

两个临时探针都有控件数/树深限制，并由外层进程执行 25 秒超时与终止；正式 Composer 探测使用 22 秒外层等待。读数前后前台窗口相同。没有调用 Invoke、Select、Expand、SetFocus、SetValue、滚动、键鼠输入或发送消息。原生探针验证了同一 Windows UIA 服务的补充读取能力，不等同于正式 CAR 已支持该属性。

现有 `ComposerWorker.cs` 的 `AccessibleComposer.BuildContext()` 通过文档标题、聊天工具栏与唯一同名侧栏项绑定，返回 `ComposerContext` 的 Backend、Conversation、Editor、Title、Port。`Conversation` 是文档与侧栏元素 runtime ID 的组合，不是 Codex 对话 UUID；没有项目字段，也没有侧栏目录返回结构。`DebugComposer.cs` 的 CDP 备用路径仅连接已启用且通过所有权校验的端口，本机未走通该路线。

本轮只证明当前显示状态下的读取能力。没有主动点击项目/聊天，因此“点击项目会导航还是折叠”、切换后标记如何变化、同名聊天、隐藏/折叠与重启后的行为仍需后续验收。不能把项目展开状态或 CSS 高亮当作这部分验收。

## 后续设计建议（尚未实施）

- 将读取结果区分为目录项、当前上下文和取证状态。目录项至少含项目/聊天类型、完整标题、祖先项目、运行时元素标识、离屏及展开信息；当前上下文记录 `current=page`、文档标题和工具栏项目的相互一致性。
- 优先将指定范围做成动作前的身份约束；项目范围是否需要主动切换并轮询多个聊天，属于待确定的产品行为。读取和枚举成功本身不授权自动导航或发送。
- 范围核验必须覆盖普通三角形点击以及额度暂停、恢复和继续输入，不能只改 Composer 补发分支。动作前重新核验，切换、歧义、超时或不支持时等待。
- 跨重启保存指定对象需要稳定项目/对话身份来源，或明确重新确认策略。名称及 runtime ID 只能参与校验，不能独自解决同名、改名和重建的问题。

未完成需求已登记在 [当前 TODO](../../TODO.md) 的“自动恢复支持指定项目与对话”。原始快照、临时诊断源码和可执行文件仅保留在被忽略的根 `.local/`，没有将个人完整侧栏或会话正文放入共享记录。

## 参考边界

- [Microsoft：UI Automation 属性标识](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-automation-element-propids) 定义 ARIA 属性字符串；[GetCurrentPropertyValueEx](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationelement-getcurrentpropertyvalueex) 可忽略默认值以区分不支持。这里对 Codex 返回内容的结论来自本机实测。
- [OpenAI Docs：Codex as a platform](https://developers.openai.com/blog/codex-as-a-platform) 将 app-server 描述为应用接入协议；该文档不承诺桌面侧栏选中状态接口。本轮没有将另起 app-server 或聊天管理工具的目录能力归入 CAR。
