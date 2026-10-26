# Contributing / 参与贡献

欢迎改进图标识别、窗口定位、后台兼容性、文档和翻译。项目采用 [MIT](LICENSE)。

默认版本系列为 **1.1.x**，常规更新只增加补丁号。AI 可以建议升级系列，但只有维护者明确允许后才能进入 1.2.x 或更高系列。规则见 [AGENTS.md](AGENTS.md) 与 [发布指南](docs/releasing.md)。

## 反馈问题

先阅读 [FAQ](docs/troubleshooting.md) / [English FAQ](docs/troubleshooting.en.md)，并查看已有 Issues。提交时写明：

- 工具版本、Codex 版本、Windows 版本及显示缩放。
- 前台或后台模式、自动定位或手动标定，以及 Codex 是否被遮挡或最小化。
- 复现步骤、预期行为、实际行为和大致时间。
- 必要的日志行或按钮局部图，先移除私人信息。

模型容量或账号额度问题应通过 OpenAI 的产品反馈处理；本仓库处理工具的识别、监测和操作问题。

For bug reports, include utility/Codex/Windows versions, display scaling, mode, reproduction steps, expected/actual behavior, and relevant redacted log lines or a cropped button image. Use OpenAI’s channels for service capacity or account issues.

## 提交修改

1. Fork 后创建分支，围绕一个具体问题修改。
2. 在 PR 中写明触发条件、修改后的行为和验证结果。
3. 保持 C# 5 / 系统 .NET Framework 兼容和单 EXE 便携特性。
4. 根据改动运行 [开发指南](docs/development.md) 中相关检查。

涉及识别的修改，应同时验证真实恢复/运行/发送/声波样本和容易混淆的负样本；不要为了提高命中率放松恢复点击确认。涉及后台路径的修改，应核对新帧时间、取消清理，以及鼠标和前台窗口保持。

不提交 `dist/`、`release/`、`packages/`、日志、完整对话截图或私人开发记录。已有发布附件保留原版本快照；新发布遵循 [发布流程](docs/releasing.md)。仅修改文档时检查链接和事实即可。

`TODO.md` 只保留未完成工作；事项完成并满足验证与交付要求后，同轮移出当前待办，完成结论放入更新记录、验证文档或 `docs/history/`。当前事项从 1 连续编号，移除或调整顺序后重新编号；历史归档保留当时的标题与编号，跨文档引用优先使用任务名称。共享的规则与决策记录继续纳入 Git；个人草稿、原始诊断和临时代理输出放在根目录 `.local/` 等忽略目录，不进入源码包。源码包有独立的文件选择规则，不能只依赖 `.gitignore`。

For code changes, keep the C# 5 / .NET Framework and single-file constraints. Match validation to the affected behavior; documentation-only contributions need link and factual review, not desktop interaction tests.
