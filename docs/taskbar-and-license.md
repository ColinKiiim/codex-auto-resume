# 1.1.0：MIT 开源与独立任务栏窗口

2026-10-24，本地 Windows 验证。

## 任务栏

此前程序未主动声明应用身份，由 ChatGPT/Codex 启动时可能被 Shell 归入宿主应用组。现在启动入口首先设置进程 AppUserModelID `CodexAutoResume.Desktop`，在任何窗口出现前完成；主窗口与演示窗口的属性也明确设置同一 ID，销毁句柄前清理。主窗口开启 ShowInTaskbar，并使用内嵌的独立图标。

验证包括进程 ID 读回、窗口属性写入/读回/清理，以及独立 EXE 迁移检查。另对实际任务栏做了局部屏幕截图：ChatGPT 的白色标志与 CodexAutoResume 的蓝色播放图标是两个独立按钮。截图仅保存在本机临时目录，未打入公开源码包。

实现依据：[进程 AppUserModelID](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-setcurrentprocessexplicitappusermodelid)、[窗口 AppUserModel.ID](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-appusermodel-id)、[窗口属性存储](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shgetpropertystoreforwindow)。

## MIT 与源码包

根目录 LICENSE 使用标准 MIT 文本，版权行为 `Copyright (c) 2026 CodexAutoResume contributors`；NOTICE 说明独立项目身份和第三方 UI 参考素材。EXE 内嵌完整许可证，主窗口 F3 可查看，不需要额外分发文本文件。源码、脚本与文档的许可范围在 README 与 NOTICE 中注明。

`tools/package-source.ps1` 生成 `packages/CodexAutoResume-1.1.0-source.zip`，包含源码、识别样本、图标生成器、构建脚本、许可证及文档，排除可执行文件、日志、机器测试报告与临时文件。源码包已在无关目录解压、重新构建通过，并通过完整测试与迁移检查。文件生成在本地，未执行任何外部仓库发布。

1.1.0 共 448 项检查通过，单文件隔离、中文路径、改名、只读 EXE、不同工作目录和空 PATH 测试继续通过。
