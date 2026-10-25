# 1.1.2 后台模式（实验）

1.1.2 增加整次捕获 3 秒截止时间，覆盖显卡调用和会话释放阶段可能卡住的情况。超时后停止监控；未完成任务结束前拒绝新的捕获，迟到画面立即释放且不能用于点击。新增 10 项正常、超时、异常、迟到结果丢弃及恢复采样检查，总计 458 项回归检查通过。

本机验证日期：2026-10-24，Windows 11 x64。此版本继续采用 MIT，分发只需要 release/CodexAutoResume.exe。

## 实现

Windows Graphics Capture 的 HWND 捕获经由系统 WinRT 投影、原生 D3D11 与 DXGI 完成，不引用 NuGet 或第三方 DLL。编译使用本机系统 WinMetadata，运行时使用目标电脑的 Windows 接口。通过 CreateFreeThreaded 建立新捕获请求；只接受请求开始后时间戳的新帧。捕获在工作线程执行，单次等待新帧上限 2 秒，主窗口和停止快捷键保持响应。黄色系统捕获边框保留。D3D 设备在进程内复用并串行访问；逐帧释放 WinRT 帧、表面、会话及池，设备异常后重建。

每帧重新扫描目标窗口下方区域，坐标由捕获边界换算到屏幕及目标子窗口客户区。多候选仍使用同一人工选择规则。手动模式保留窗口内相对位置；改变尺寸或 DPI 后须重新标定。

点击前再发起一次独立捕获，核对三角形、位置、窗口/进程身份、有效时间与用户开关。目标原生按钮使用 BM_CLICK，其他绘制控件使用 WM_MOUSEMOVE/WM_LBUTTONDOWN/WM_LBUTTONUP。不调用后台 SendInput、不移动鼠标、不主动激活窗口、不注入目标进程、不修改 Codex 文件。消息成功只记一次尝试；连续 3 帧新方块才计为恢复成功，8 秒未确认停止。

关闭、切换模式、重新连接、取消自动点击均使未完成的异步捕获失效，完成后的旧结果不会触发点击。最小化或隐藏窗口不处理。

## 真实验证

| 场景 | 结果 |
|---|---|
| 448 项现有识别、选择、状态机、便携与任务栏回归 | 通过 |
| 不透明窗口完全盖住演示窗口 | 屏幕截图只有遮挡层；WGC 识别到下面的三角形 |
| 演示原生按钮后台点击 | 计数 0→1，变为方块；鼠标、前台窗口不变 |
| 独立 Chrome/Chromium HTML 测试页完全遮挡 | WM 消息触发 DOM click handler，三角形→方块；鼠标、前台窗口不变 |
| 真实 Codex 完全遮挡、保持运行状态 | 连续 3 个新帧识别到 Running；帧年龄约 46–53ms；前台窗口不变 |
| 真实 Codex 暂停→恢复 | 未验证，当前没有可用于测试的暂停场景 |
| 主程序界面及异步监控链路 | 切到工具后仍恢复演示按钮，已点击 1、已确认恢复 1 |
| 过期帧、窗口移动、取消校验、最小化、隐藏 | 均拒绝定位或点击 |
| 连续捕获资源检查 | 预热后两组各 30 次捕获，进程句柄 593→593→593 |
| 单个 EXE 隔离目录、中文/空格/单引号路径、改名、只读、无 PATH | 通过 |
| 第二台实体电脑 | 尚未测试 |

测试中普通 WinForms 自绘 Control 忽略了后台鼠标消息；UI Automation Invoke 则改变了焦点。这说明仅凭消息成功不能判断兼容性。演示控件现为具有真实按钮语义的 Button，通过 BM_CLICK 路径验证；Chromium 测试另行覆盖鼠标消息路径，没有用演示结果代替真实 Codex 验证。

## 使用范围

支持被其他窗口完全遮挡，但必须保持目标未最小化、未隐藏。部分应用在遮挡时会停止绘制，部分拒绝后台消息；这种情况会等待或停止，不能保证所有客户端版本都能恢复。权限等级不同可能被 Windows UIPI 拒绝。保持工具和 Codex 以相同普通用户权限运行。

为避免复用旧画面，每轮新建捕获请求，成本高于持续录屏；当前以每 400ms 尝试一轮。不要据此视为已完成长时间性能或所有显卡验证。锁屏、远程桌面断开、睡眠、最小化、HDR、多显卡/混合 DPI 组合尚未完成系统性验证。

## 研究来源

- [Win32CaptureSample](https://github.com/robmikh/Win32CaptureSample)：HWND 捕获、CreateFreeThreaded、快照流程；参考架构，未复制其代码或二进制。
- [OBS Window Capture](https://obsproject.com/kb/window-capture-sources) 与 [winrt-capture.cpp](https://github.com/obsproject/obs-studio/blob/master/libobs-winrt/winrt-capture.cpp)：被其他窗口遮挡时的窗口捕获方案；未引入 OBS GPL 代码。
- [AutoHotkey ControlClick 文档](https://github.com/AutoHotkey/AutoHotkeyDocs/blob/v1/docs/lib/ControlClick.htm)：面向窗口/控件的后台消息路线和兼容限制；未复制其源码。
- [WGC 遮挡绘制问题 #103](https://github.com/robmikh/Win32CaptureSample/issues/103)：应用自身的遮挡优化可能影响帧更新。
- [PostMessage](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-postmessagew)：消息投递及 UIPI 限制。
