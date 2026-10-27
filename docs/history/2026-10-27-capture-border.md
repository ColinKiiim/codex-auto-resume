# 真实 Codex 捕获黄色边框实测

2026-10-27（UTC+8）。用户要求 CAR 打开时不要给 Codex 外缘增加黄色边框，要求实际测试并加入 TODO。用户附图为另一台电脑的现象，并确认本机同样出现；随后确认 Codex 一直在第二块屏幕前台。

## 结果

本机可以去除：在真实 Codex 窗口上，使用 CAR 的 WGC 捕获实现创建默认会话后，桌面截图出现黄色边框；将同一会话的 `GraphicsCaptureSession.IsBorderRequired` 设为 false 后，截图中的黄框消失。随后官方无边框权限请求返回 `Allowed`，再次设为 false 后仍无黄框，CAR 的 Snapshot 调用仍成功返回捕获图像。

这是临时测试 EXE 的 API 与视觉结果，**没有修改正式 CAR，也没有验证另一台电脑**。未将本机结果推断为所有系统/权限条件下都能隐藏。

| 阶段 | 接口结果 | 第二屏真实 Codex 的桌面截图 |
| --- | --- | --- |
| 默认 WGC 会话 | `IsBorderRequired=true`，Snapshot 返回 | 外缘有黄框 |
| 同一会话设 false，尚未在本次真实窗口流程请求权限 | 属性读回 false | 黄框消失 |
| 请求 `GraphicsCaptureAccessKind.Borderless` | `Allowed`，未自动操作任何授权提示 | 不需点击系统授权才能继续本次测试 |
| 授权后再设 false | 属性读回 false，Snapshot 再次成功返回 | 黄框仍消失 |

“本次真实窗口流程请求权限之前”不代表首次授权：此前自有窗体探针已请求过同一测试 EXE 的权限并返回 Allowed。没有清理授权或修改全局隐私设置，因此不能从第二阶段推断任何无权限环境都能直接设 false。

## 环境与方法

- Windows `10.0.26300.0`，商店版 Codex `26.1021.3705.0`，加载正式 CAR `release/CodexAutoResume.exe`（1.1.16）的实际 `WindowCapture.Snapshot` 实现，并访问该测试进程自己的捕获会话。未向 Codex 注入代码，也未输入或发送消息。
- 测试 EXE 为普通 .NET Framework GUI 程序，沿用项目 Win32 manifest；本轮没有创建、安装 MSIX 或在 Appx manifest 添加 capability。本机该测试程序的实际权限结果仍为 Allowed；不能以此忽略微软文档对其他部署环境的要求。
- 真实目标位于第二屏，捕获边界为 `(2720,244,1600,861)`。主屏为 `(0,0,2560,1440)`，第二屏为 `(2560,173,1920,1080)`。屏幕截图按目标实际边界外扩 6 像素，使用系统屏幕复制取得，能够包含 WGC 捕获指示；没有用通常不含该指示的 WGC 图像自身判断边框。
- 三次桌面对照分别保存于本机临时目录：`codex-shot-2026-10-27_12-15-46.png`（默认）、`codex-shot-2026-10-27_12-15-47.png`（false）、`codex-shot-2026-10-27_12-15-49.png`（授权后）。临时截图位于 `C:/Users/86153/AppData/Local/Temp/`，含个人侧栏与会话内容，不加入共享源码或发布包。
- 初期自有窗体试验在固定主屏坐标截图，没有拍到测试窗体，故这些截图不算视觉验收；其 Snapshot 和 Allowed 结果仅作接口证据。收到用户双屏说明后，改按真实 Codex 坐标完成上述有效对照。
- 测试过程限定 45 秒，外层 finally 清理测试进程，测试程序 finally 释放捕获会话并关闭自有隐藏窗体。没有改动正在运行的 CAR 会话，也没有持续保留测试捕获。

本轮使用 [screenshot 技能](C:/Users/86153/.codex/skills/screenshot/SKILL.md)的 Windows 辅助脚本取得桌面对照；没有使用 computer use 控制 Codex。原始日志、截图索引、探针源码和 EXE 留在被忽略的 `.local/`。

## 原因和正式接入范围

黄框来自 Windows Graphics Capture 的系统捕获指示，不是 UIA 当前聊天标记或 CAR 自绘焦点框。现有源码有两处 `CreateCaptureSession`：`EnsureStream` 创建持续会话，`IndependentCore` 创建临时复核会话，均直接 StartCapture，未设置无边框属性。

正式接入应统一处理这两处及会话重建，先检测系统能力，按官方机制请求权限，再在每个会话启动前应用无边框设置；拒绝/不支持/异常时维持可用捕获并说明实际状态。只改持续会话可能在临时复核时再次亮框。还需验证第二台电脑、重连和并发会话，不以普通截图替换后台 WGC 路线。

[微软 IsBorderRequired 文档](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.graphicscapturesession.isborderrequired?view=winrt-26100)说明：无权限时，即使将属性设置为 false 成功，系统仍可能保留边框；另一软件对同一目标要求边框时也会继续显示。[RequestAccessAsync 文档](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.graphicscaptureaccess.requestaccessasync?view=winrt-26100)描述了 Borderless 授权及包 manifest capability 要求。本轮依照本机返回与实际截图记录结论，没有绕过系统授权。

待实施与跨设备验收已登记在 [TODO](../../TODO.md) 的“CAR 捕获 Codex 时不显示系统黄色边框”。本轮未改版本或发布。
