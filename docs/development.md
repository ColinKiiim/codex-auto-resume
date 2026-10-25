# 开发与验证

[首页](../README.md) · [贡献指南](../CONTRIBUTING.md) · [发布流程](releasing.md)

## 本机构建

在 Windows x64 的 PowerShell 中，进入项目目录运行：

```powershell
.\build.ps1 -BuildOnly
```

使用系统 .NET Framework C# 编译器、WinForms、Windows WinRT 元数据和 D3D11。无需下载 .NET SDK 或 NuGet 包。代码保持 C# 5 兼容。输出在 `dist/`，该模式不会修改已发布的 EXE、版本归档或源码包。

## 专项检查

| 命令 | 覆盖范围 |
|---|---|
| `.\build.ps1 -WorkflowTests` | 真实主窗体：勾选启动、后台恢复、多候选恢复、心跳、问答和停止 |
| `.\build.ps1 -BackgroundTests` | 遮挡输入、定位、手动标定、尺寸变化、句柄、持续会话与取消清理 |
| `.\build.ps1 -QuestionTests` | 本机 OCR 问答面板，跳过/提交及面板消失 |
| `.\build.ps1 -ChromiumTests` | 隔离 Chrome 测试窗口的后台输入路径 |
| `.\build.ps1 -TargetProbe` | 真实 Codex 完全遮挡的只读捕获，不点击 |
| `.\tests\release-pipeline.ps1` | 发布版本对应、完整性、历史保护、并发锁和源码快照 |
| `.\tests\background-portable.ps1 -ExecutablePath release/CodexAutoResume.exe` | 隔离单 EXE 的内置后台自测 |

交互与后台专项检查需要可用的 Windows 桌面。自测期间保持鼠标和前台窗口不动。Chromium 测试只对隔离的测试实例关闭遮挡后台节流，Chrome 不是应用运行依赖。发布流程测试需要本地已有 v1.1.4 和 v1.1.3 附件。

`-QuestionImages` 可以向问答测试提供本地截图。不要提交包含对话的完整截图；临时诊断和私人样本不进入源码包。

## 主要模块

| 文件 | 职责 |
|---|---|
| `src/App.cs` | 窗口、监测循环和演示 |
| `src/Recognition.cs` | 图标识别、引用排除与恢复门控 |
| `src/Native.cs` | 窗口发现、身份和定位 |
| `src/Selection.cs` / `src/WindowPicker.cs` | 区域与窗口选择 |
| `src/WindowCapture.cs` / `src/CaptureSampler.cs` | 持续后台捕获、原始帧时间及超时隔离 |
| `src/BackgroundClick.cs` | 后台消息操作 |
| `src/QuestionDetector.cs` | 本机问答 OCR |
| `src/Monitoring.cs` | 心跳、日志与统计 |
| `src/BackgroundDiagnostics.cs` | 内置遮挡自测 |
| `src/Portable.cs` / `src/DesktopIdentity.cs` | 便携检查、资源和独立任务栏身份 |

## 版本与源码包

应用版本由根目录 [VERSION](../VERSION) 管理。正式发布前增加版本并准备对应说明，再运行 `.\build.ps1`；它会验证候选 EXE、生成源码 ZIP 和版本目录。

文档可以在开发分支随时修改；已有 Release 及其源码 ZIP 保留该版本快照。文档变更后，不要用相同版本号重新生成发布附件。GitHub 默认分支包含最新文档，Release 源码包对应发布时的内容。

验证结果及实际覆盖范围见 [validation.md](validation.md)，捕获细节见 [background-mode.md](background-mode.md)。
