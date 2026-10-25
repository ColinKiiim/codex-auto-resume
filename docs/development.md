# 开发与验证

[首页](../README.md) · [贡献指南](../CONTRIBUTING.md) · [发布流程](releasing.md)

## 本机构建

在 Windows x64 PowerShell 中运行 `.\build.ps1 -BuildOnly`。使用系统 .NET Framework C# 编译器、WinForms 和 Windows WinRT 元数据，无需 .NET SDK、NuGet、Node 或 Python。源码保持 C# 5 兼容。输出位于 `dist/`；该模式不改变发布归档。

## 保留的必要验证

测试只覆盖误点、输入被打断、旧证据、更新损坏、账号串号和进程泄漏等实质风险。默认测试按以下五组输出断言结果，不按穷举组合扩张数量：识别/误点、恢复/输入、捕获证据/取消、更新完整性/回退、当前登录额度协议。

| 命令 | 用途 |
|---|---|
| `.\build.ps1 -BuildOnly` | 必要回归；当前为 73 次断言，不代表 73 个独立设备场景 |
| `.\build.ps1 -WorkflowTests` | 小型真实主窗体流程：默认仅观察、暂停保持、遮挡恢复、多候选不误点 |
| `.\build.ps1 -QuestionTests` | Windows 本机 OCR 问答保护及面板消失；可用 `-QuestionImages` 提供本机样本 |
| `.\build.ps1 -UpdateTests` | 隔离进程中的更新替换、启动确认与故障回退 |
| `.\tests\background-portable.ps1 -ExecutablePath dist/CodexAutoResume.exe` | 一份改名只读 EXE，中文/空格路径、空 PATH 和完全遮挡的本机捕获/消息点击；检查鼠标与前台保持 |
| `.\tests\usage-live.ps1` | 通过本机真实 Codex 安装读取额度并检查子进程清理；需要已登录的桌面目标，不输出账号或令牌 |
| `.\tests\release-pipeline.ps1` | 使用当前候选 EXE 验证归档字节、公开附件校验及同版不可覆盖，不依赖历史发布附件 |
| `.\tests\version-policy.ps1` | 默认 1.1.x 版本系列与无效版本拒绝 |

图形/OCR验证需要可用的 Windows 桌面。恢复点击只对自有演示窗口执行，不向用户的真实 Codex 注入测试输入。`usage-live.ps1` 通过 Windows PowerShell 加载 .NET Framework 程序集；从 PowerShell 7 调用时自动转交兼容运行时，不改变系统执行策略。

用户已要求删除非必要测试。原字体/编号/缩放/偏移穷举、布局和预览检查、重复 Chromium/后台套件、长演示流程、重复迁移自检均已移除。历史版本的验证记录仍描述当时覆盖范围，不是当前测试入口。

## 数据与源码包

临时结果放在忽略的根目录 `.local/`、已有输出目录或系统临时目录；原始私人截图不提交。共享规则、当前待办、必要决策历史和公开验证结论纳入 Git。源码包按独立白名单收集文件，不能仅依赖 `.gitignore` 排除私人材料。

## 主要模块

- `App.cs` / `MainUi.cs`：监测、首页、设置与恢复流程
- `CodexUsage.cs` / `UsageUi.cs`：当前登录额度 RPC、受控子进程、底部摘要与按需详情
- `Recognition.cs` / `RecoveryWindow.cs`：识别、恢复稳定性、1.5 秒输入窗口与低频独立核验
- `WindowCapture.cs` / `CaptureSampler.cs`：系统捕获、原始内容时间、截止时间与迟到结果隔离
- `Updates.cs` / `UpdatePrompt.cs`：更新验证、替换、回退及阅读式提示

## 版本与发布

版本由 [VERSION](../VERSION) 管理；正式发布前更新补丁版本、发布说明与索引，再运行 `.\build.ps1`。默认发布构建执行必要回归、单 EXE 后台便携检查、源码打包与本地归档；随后按用户约定推送源码和 tag，并发布 GitHub Release 的 EXE、源码 ZIP 与 SHA256。

已有发布附件保持不可变。文档可以随开发提交更新，但不能用相同版本重建或覆盖旧附件。实际验证结果和未覆盖的设备条件见 [validation.md](validation.md)。
