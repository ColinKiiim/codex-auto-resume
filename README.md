# CodexAutoResume — Codex 暂停后自动恢复运行

[下载最新版](https://github.com/ColinKiiim/codex-auto-resume/releases/latest) · [历史版本](https://github.com/ColinKiiim/codex-auto-resume/releases) · [反馈问题](https://github.com/ColinKiiim/codex-auto-resume/issues/new/choose)

[English](README.en.md) · [常见问题与排查](docs/troubleshooting.md) · [使用指南](docs/user-guide.md) · [更新记录](CHANGELOG.md) · [MIT](LICENSE)

**Codex 频繁提示 at capacity，任务停下来后总要手动点继续？** CodexAutoResume 是一个 Windows 桌面小工具：当 Codex 输入框右下角出现向右三角形的恢复按钮时，自动识别并点击，再通过白色方块确认恢复运行。单个 EXE，双击使用，无需 API Key。

**Windows auto-resume helper for Codex desktop.** Automatically clicks the resume button when a paused task exposes it, including interruptions reported as “at capacity”. It reduces manual retries; model availability and account limits still apply.

## Codex at capacity 频繁出现，应该怎么处理？

先看当前界面是否有可恢复的任务：

| 当前情况 | 如何处理 |
|---|---|
| 任务暂停，右下角出现向右三角形 | 本工具可以持续监测，在按钮稳定可识别时自动恢复 |
| 显示账号用量或重置时间 | 查看账号用量与恢复时间；自动点击不会增加可用额度 |
| 服务持续不可用，或没有恢复按钮 | 查看服务状态和客户端提示；本工具继续观察，不能替服务恢复容量 |
| 正在等待回答、确认权限或选择方案 | 由用户处理；本工具识别问答面板后等待 |
| 已变成白色方块、发送箭头或声波图标 | 分别表示运行、发送、语音状态，不点击 |

程序根据**窗口中的按钮图形**判断状态，不读取 `at capacity` 报错来诊断原因，也不修改 Codex、模型或账号设置。详细说明见 [Codex at capacity / atcapacity 常见问题](docs/troubleshooting.md)。

## 下载与开始使用

1. 打开本仓库的 **Releases**，下载最新版本附件 `CodexAutoResume-v版本-windows-x64.exe`。源码 ZIP 面向开发者。
2. 双击 EXE，并打开 Codex 桌面窗口。工具自动寻找目标，启动时只观察。
3. 核对识别结果，勾选 **“允许自动点击”** 即开始，无需快捷键或手动标定。
4. 想切换到其他应用工作时，可勾选 **“后台模式”**。Codex 可以被其他窗口完全遮挡，保持未最小化。
5. 点击 **“停止”** 或关闭工具结束监测。

需要 Windows 10 1903+ / Windows 11 **x64** 和系统 .NET Framework。复制一个 EXE 即可迁移，图片、图标和许可证已内嵌，无需 Python、Node.js 或额外模型下载。问答面板识别使用 Windows 本机 OCR 语言。

本地构建后，程序也位于 `release/CodexAutoResume.exe`。[使用指南](docs/user-guide.md) 包含窗口选择、备用标定、快捷键和自测方法。

## 四种按钮，分别处理

| 图标 | 状态 | 工具行为 |
|---|---|---|
| ![右三角形恢复按钮](docs/images/resume.svg) | 暂停，可恢复 | 连续稳定识别后点击；点击前再次核对 |
| ![白色方块运行按钮](docs/images/running.svg) | 正在运行 | 继续观察，作为恢复成功的确认 |
| ![向上箭头发送按钮](docs/images/send.svg) | 发送 | 只观察 |
| ![声波语音按钮](docs/images/voice.svg) | 语音 | 只观察 |

蓝色引用编号和其他未知图形不列为自动按钮候选。多个真实按钮出现时持续扫描，恢复单候选后自动继续；也可点击“选择区域”确认。多个目标窗口需要用户选择。

## 适合长期工作的监测方式

- **窗口位置可变**：自动重新定位，手动标定保留作为备用。
- **遮挡后台模式**：通过窗口捕获和后台消息尝试恢复，不移动系统鼠标、不抢前台焦点。实际支持情况可用“后台自测”检查。
- **持续监测**：拖动窗口、暂时无法识别、捕获失败或未确认方块时继续观察，恢复点击至少间隔 5 秒。
- **状态可见**：每 1 分钟或 5 分钟显示监测心跳，本机详细日志每 10 秒记录一次。
- **独立窗口**：自己的任务栏图标，方便切回查看。

后台模式依赖目标窗口提供新画面并接受消息。最小化时等待还原；画面过期时显示“状态待确认”，继续尝试恢复捕获。黄色边框是 Windows 的捕获指示。详见 [后台模式](docs/background-mode.md) 与 [日志说明](docs/monitoring-logs.md)。

## 常见问题

**能彻底解决 Codex “Selected model is at capacity” 吗？**

它负责在恢复按钮出现时自动操作。服务是否接受下一次请求，仍由 Codex 和服务端决定。遇到持续报错，先核对服务状态、用量和客户端提示，见 [排查指南](docs/troubleshooting.md)。

**会自动发送输入框内容、回答问题或点停止吗？**

只对严格识别的右三角形执行恢复点击；发送箭头、声波、运行方块和问答面板都继续观察。默认自动点击关闭。

**能用于 Codex CLI、浏览器版、macOS 或 Linux 吗？**

当前实现面向 Windows x64 上的 Codex 桌面窗口。CLI 和浏览器的重试逻辑需要对应的实现。

**软件好像一直没动，怎么看是否还在工作？**

查看界面心跳、取样计数和最近新帧时间；点击“打开日志”可查看 `%LOCALAPPDATA%\CodexAutoResume\logs`。图标没有变化时不会每轮刷屏。

## 隐私与反馈

图像识别和 OCR 在本机完成。程序无需账号密码或 API Key，不自动上传画面，也不把问答文字、聊天标题或完整截图写入日志。“保存局部诊断图”由用户主动触发，仅保存按钮区域。

反馈问题时，请在本仓库 **Issues** 提供工具版本、Codex 版本、Windows 版本、显示缩放、前台/后台模式和相关状态记录。涉及图标误识别时，可附不含对话内容的局部截图。[贡献指南](CONTRIBUTING.md) 说明了复现和验证方法。

## 开发与文档

当前版本见 [VERSION](VERSION)，历史变化见 [版本说明](docs/releases/README.md)。1.5.3 已通过 4,216 项本机检查，包括数字引用负样本和不同缩放的真实按钮；另有主界面流程、后台遮挡及单 EXE 隔离迁移检查。具体覆盖范围见 [验证记录](docs/validation.md)。

```powershell
# 编译并运行回归检查，不改动已发布版本
.\build.ps1 -BuildOnly
```

- [开发与专项验证](docs/development.md)
- [发布与历史版本](docs/releasing.md)
- [GitHub 上线与可发现性](docs/github-launch.md)
- [英文 FAQ](docs/troubleshooting.en.md)
- [文档索引](llms.txt)

MIT 开源，完整许可见 [LICENSE](LICENSE)。这是独立社区项目；Codex / ChatGPT 为 OpenAI 产品名称，详见 [NOTICE](NOTICE)。
