# CodexAutoResume 1.1.3

MIT 开源项目；完整许可证见 [LICENSE](LICENSE)。独立任务栏图标和分组，不与启动它的 ChatGPT/Codex 窗口共用任务栏身份。源码包位于 `packages/CodexAutoResume-1.1.3-source.zip`。

识别 Codex 蓝色圆按钮并自动恢复运行的 Windows 小工具。支持自动定位、多区域人工选择、备用手动标定，以及单文件便携使用。

## 后台模式（1.1.3 实验功能）

1.1.2 加入整次捕获 3 秒超时保护：超时停止监控，迟到画面丢弃，未结束的系统调用不会导致堆积更多捕获任务。1.1.3 新增内置后台自测。

勾选“后台模式”后，先将 Codex 放到前台并按主窗口显示的 F8 组合键连接。观察识别正确后勾选“允许自动点击”，随后可以切到其他应用，把 Codex 完全盖住；保持 Codex 未最小化。切换前台/后台模式会清除连接，需要重新按 F8。

后台模式使用 Windows Graphics Capture 直接获取目标窗口的新帧，再向目标子窗口投递点击消息，不主动激活目标或移动鼠标。系统可能显示黄色捕获边框。需要 Windows 10 1903+ / Windows 11 x64，使用 Windows 自带接口，无新增第三方运行库；迁移仍只复制一个 EXE。

已验证：完全遮挡的演示按钮及独立 Chromium HTML 按钮均收到点击并由三角形变为方块，鼠标和前台窗口不变；真实 Codex 在完全遮挡时连续捕获到运行方块。**真实 Codex 的暂停→恢复尚未获得实际暂停场景验证，因此仍为实验功能**。应用是否接受后台消息取决于其版本与权限等级。

最小化、隐藏、旧帧、黑屏、未知图形、窗口/进程变化时不点击；后台操作投递后仍需连续新方块帧确认，8 秒未确认会停止，不会反复发送。自动定位、多候选人工确认和 F7 手动备用在后台模式仍保留。后台监控只处理快捷键连接的那个窗口。

实现、验证和限制见 [后台模式验证记录](docs/background-mode.md)。

## 单文件迁移

新电脑可先点击“本机自检”（F2），再点击“后台自测”（F4）。后台自测会打开工具自己的按钮窗口，用不透明窗口完全遮挡它，读取新帧、后台点击一次并验证连续方块，以及鼠标/前台窗口保持不变。通常只需几秒，请暂勿操作鼠标或切换窗口；关闭自测遮挡窗口，或按主窗口显示的 F10 组合键可取消。自测期间原监控保持停止，完成后不会自动重启监控。

自测使用的代码和控件均在同一个 EXE 内，不需要另装浏览器、测试程序或携带图片。它仅测试自带窗口，不会点击 Codex 或发送消息；自测通过不能替代真实 Codex 暂停到恢复的兼容性验证。

**只需复制 `release/CodexAutoResume.exe` 一个文件到另一台 Windows 10/11 x64 电脑，双击运行。** 不需要复制 assets、源码、配置文件或整个项目，不需要 Python、SDK、API Key 或联网服务。

识别样本、中文使用说明、演示窗口均已内嵌。程序依赖 Windows 自带的 .NET Framework 4.x；这是使用系统运行库的单文件程序。Windows 10/11 自带的具体版本见 [Microsoft 官方兼容表](https://learn.microsoft.com/en-us/dotnet/framework/install/versions-and-dependencies)。本次验证包括将 EXE 单独放到全新临时文件夹中运行，尚未在另一台实体电脑上验证。

日志与主动保存的局部诊断图写到当前用户的 `%LOCALAPPDATA%\CodexAutoResume\logs`，不要求 EXE 所在目录可写。无需将日志带到另一台电脑。设置、窗口句柄和坐标不会跨启动恢复。

## 推荐操作：自动定位

1. 启动 EXE，将 Codex 窗口置于前台。
2. 按工具主窗口显示的 **F8 组合键**：自动连接前台 Codex，寻找按钮并开始观察，鼠标不必放在按钮上。
3. 默认只观察。切回工具查看局部预览，确认识别正确后勾选“允许自动点击”，再切回 Codex。
4. 工具每帧重新定位；移动窗口、最大化、改变窗口大小或按钮内部位置后，会使用新坐标。
5. 如有多个候选，弹窗显示编号、上下文预览与缩略图；必须选中一个并点击“使用所选区域”。取消会停止监控。确认前不会点击。

工具会保持用户选中的区域。若原选区域消失，或窗口大小、布局变化后无法可靠关联，会再次要求选择，不会擅自切换到另一个区域。只有一个候选且没有此前的人工区域限制时，无需确认。

## 快捷键与手动备用

| 功能 | 默认组合 |
|---|---|
| 自动定位并开始观察 | Ctrl+Alt+F8 |
| 备用手动标定并开始观察 | Ctrl+Alt+F7 |
| 开始 / 停止观察 | Ctrl+Alt+F9 |
| 紧急停止 | Ctrl+Alt+F10 |

默认组合被占用时，自动尝试 Ctrl+Alt+Shift，再尝试 Ctrl+Shift。**以主窗口显示的实际组合为准**。本机实测为 Ctrl+Alt+Shift。

备用手动模式：鼠标放到蓝色按钮中心，按 F7 组合键。手动模式使用窗口内相对坐标，移动整个窗口后仍有效；改变窗口大小、DPI 或输入框布局后需要重新标定。F8 随时切回自动模式。“手动取样(px)”只影响备用手动模式，自动模式自行计算取样大小。

点击“使用说明”或按 F1 可查看内置说明，迁移时无需携带 README。可先点“打开演示”，测试图标切换、双区域、按钮换位置及窗口最大化。

在主窗口按 F2 执行本机自检；F3 查看内嵌 MIT 许可证；F4 执行完全遮挡后台自测。独立图标、许可证和自测均在 EXE 内，迁移仍只需要一个 EXE。

## 四种状态

| 图标 | 显示状态 | 自动行为 |
|---|---|---|
| 白色向右三角形 | 可恢复 | 连续稳定出现后点击一次 |
| 白色小方块 | 运行中 | 不点击；用于确认恢复成功 |
| 白色向上箭头 | 发送模式 | 不点击 |
| 白色声波 | 语音模式 | 不点击 |
| 其他图形 | 未知 | 不点击 |

三角形和方块使用严格形状交并比。发送和语音使用允许少量像素偏差的线条匹配，适应细线缩放；较宽松的线条识别不会触发恢复点击。

## 点击与定位规则

- 每 400ms 观察一次。自动模式扫描所连接 Codex 窗口的下方约 45% 区域，寻找蓝色圆形及中央白色图标。
- 连续 4 帧三角形才准备点击，鼠标键盘需空闲至少 0.7 秒。位置改变或暂时不可见时重新累计稳定帧。
- 点击前再次扫描，核对窗口、遮挡、候选选择和当前图形，变化时取消点击。
- 每次暂停只尝试一次；连续 3 帧方块确认恢复后才允许下一轮，并有 5 秒冷却。
- 点击后 8 秒未确认方块会停止，需要检查后重新连接或标定。
- 只处理快捷键连接时的 Codex 窗口和进程。前台模式切到其他应用或被遮挡时不点击；后台模式允许完全遮挡。两种模式均不处理最小化或关闭的窗口。
- 扫描图只在内存中处理，不自动保存对话或整屏截图。“保存局部诊断图”仅保存上一帧按钮区域。

当前支持样本中的蓝色圆按钮和白色图标。前台模式需要按钮可见；后台模式获取已连接窗口的画面，不遍历其他聊天。找不到按钮时持续等待，可使用 F7 手动备用。换主题或应用更新导致图标变化后可能识别为未知。

## 开发与验证

运行 `build.ps1`：使用系统 .NET Framework 的 csc 编译，不下载 SDK 或 NuGet 包；自动执行识别、状态机、定位、选择逻辑测试，以及单文件隔离自检；全部通过后输出 release 中的单个 EXE。

当前版本 458 项自动检查全部通过，包括捕获超时、迟到结果隔离和恢复采样。新增进程和窗口独立 AppUserModelID、内嵌图标、MIT 许可及窗口属性清理检查。演示窗口的自动恢复、按钮换位置、最大化后重新定位、多区域确认与手动备用均完成实测；真实 Codex 的只观察自动定位也已验证。仍通过独立目录、中文文件名、只读 EXE、不同工作目录与空 PATH 的迁移验收，详见 [迁移验收报告](docs/migration-audit.md)。

- `src/Recognition.cs`：四状态图像识别、自动区域搜索、单次恢复状态机。
- `src/Native.cs`：窗口校验、自动/手动定位、截图与输入。
- `src/WindowCapture.cs`：WinRT/D3D11 后台窗口捕获与新帧校验。
- `src/BackgroundClick.cs`：子窗口坐标换算和后台消息输入。
- `src/BackgroundDiagnostics.cs`：内置完全遮挡自测，支持 F4 和命令行 `--background-self-check 报告路径`。
- `src/Selection.cs`：多区域确认、所选区域跟踪。
- `src/App.cs`：中文界面、内置说明与交互演示。
- `src/Portable.cs`：内嵌资源自检、版本与用户数据目录。
- `src/DesktopIdentity.cs`：独立进程/窗口 AppUserModelID、图标与 MIT 许可。
- `tools/build-icon.ps1`：生成各分辨率图标；`tools/package-source.ps1`：打包开源源码。
- `tests/Tests.cs`：真实图片、缩放位移、负样本、定位和状态机测试。
- `tests/portable-smoke.ps1`：将一个 EXE 单独复制到新临时目录并自检。
- `tests/background-portable.ps1 -ExecutablePath release/CodexAutoResume.exe`：对隔离的单 EXE 运行内置后台自测；需要可交互桌面，期间请暂勿操作。
- `build.ps1 -BackgroundTests`：完全遮挡的演示按钮端到端测试。
- `build.ps1 -ChromiumTests`：使用本机 Chrome 和隔离临时配置测试 HTML 按钮；只用于开发，不是程序运行依赖。
- `build.ps1 -TargetProbe`：真实 Codex 完全遮挡时的只读捕获探测，不点击目标。
- `docs/validation.md`：实际验证结果及未覆盖范围。

调研依据：本机 Codex 未通过辅助功能接口暴露目标按钮，因此采用图像方案。[Windows UI Automation](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-controlpatternsoverview)可在应用提供控件时调用按钮；本版使用 [CopyFromScreen](https://learn.microsoft.com/en-us/dotnet/api/system.drawing.graphics.copyfromscreen?view=windowsdesktop-9.0)读取像素、[SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput)点击、[RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)注册热键。没有修改 Codex 文件或依赖私有协议。

任务栏分组同时设置 [进程 AppUserModelID](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-setcurrentprocessexplicitappusermodelid) 和 [窗口 System.AppUserModel.ID](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-appusermodel-id)，在创建窗口时应用、销毁前清理。无需修改 Windows 全局任务栏设置。

## 许可与发布

原创代码、脚本、应用图标和文档采用 MIT；第三方名称及参考 UI 的说明见 [NOTICE](NOTICE)。版权行使用 `CodexAutoResume contributors`。单 EXE 内含完整 MIT 许可，可在主窗口按 F3 查看。源码包排除 EXE、日志、本机检查结果和临时文件；包含源码、构建脚本、识别样本及完整许可，可直接用于公开仓库或源码分发。
