问答检测新增系统 Windows.Media.Ocr 依赖；无外部分发文件，需要已安装对应 Windows OCR 语言。缺少语言只影响问答标注，图标监测继续。本机自检会显示 OCR 可用语言。

# 单文件迁移验收

验收日期：2026-10-25（Asia/Shanghai）。当前交付版本：1.1.6，Windows 10/11 x64；后台功能需要 Windows 10 1903+。下方早期迁移交互记录来自 1.2.1；1.1.4 重新通过全部隔离迁移检查，并新增隔离单 EXE 的内置后台自测。

## 结论

本地已验证可以只复制 `release/CodexAutoResume.exe` 运行，无需项目、外部图片、配置、Python、Node、API Key 或开发工具。依赖系统 .NET Framework、WinForms、Drawing、Windows Graphics Capture、D3D11/DXGI，以及 user32/kernel32/combase/dwmapi 系统接口；没有新增分发 DLL。没有内置本机用户名、工作目录或 OneDrive 路径。

这次额外修复了应用识别对安装目录的依赖：保留已知安装布局的识别，并增加 OpenAI 公司/产品元数据匹配，支持其他磁盘与自定义安装路径。它用于应用辨识，不是数字签名验证。

## 已执行的检查

| 检查 | 结果 |
|---|---|
| 图像、状态机、区域选择、安装路径识别、任务栏身份及内嵌许可回归 | 448 项通过，0 失败 |
| 仅复制一个 EXE 到全新目录 | 通过 |
| 中文、空格、&、单引号目录 | 通过 |
| EXE 改名为中文名称 | 通过 |
| EXE 文件设置为只读 | 通过；未修改目录 ACL |
| 工作目录与 EXE 目录不同 | 通过 |
| 子进程 PATH 清空，移除 Python/Node/API 环境变量 | 通过 |
| 外部程序集检查 | 仅 mscorlib、System、System.Windows.Forms、System.Drawing |
| Win32 必需导出与 x64 输入结构布局 | 通过；INPUT=40 字节 |
| WinForms 窗口和控件创建 | 通过 |
| 当前用户数据目录读写 | 通过 |
| EXE 同级目录无其他依赖文件/新文件 | 通过 |
| 独立进程 AppUserModelID | CodexAutoResume.Desktop，设置及读回通过 |
| 独立窗口 AppUserModelID | 设置、读回及清理通过 |
| 内嵌多分辨率图标和 MIT 许可 | 通过 |
| 编译产物扫描本机用户名及项目路径 | 未发现 |

## 复制出去的 EXE 的实际操作

将一个 EXE 复制到 C 盘临时目录，目录名含中文、空格和 &，文件改名为 `便携监控 renamed.exe` 并设置只读。以 Windows 目录为工作目录启动，子进程 PATH 为空。

用这个副本完成界面操作：点击“本机自检”全部通过；打开内嵌演示；允许自动点击；F8 自动定位；三角形被点击一次，计数从 0 变为 1，并通过方块确认恢复；F10 成功停止。该目录始终只有一个 EXE。

因此验收覆盖了真正的界面、快捷键和鼠标点击，不只是在开发目录内跑测试。普通文件只读验证不等同于目录禁止写入；程序正常运行的所有写入路径均指向当前用户数据目录，没有向 EXE 目录写入的代码。

## 明天新电脑的最短验证步骤

1. 仅复制 `CodexAutoResume.exe`，双击运行。
2. 点“本机自检”或按 F2，应显示通过。
3. 点“后台自测”或 F4，暂勿操作鼠标/切换窗口，应完成完全遮挡下点击一次并确认方块。它只点击工具自己的按钮，不会操作 Codex。
4. 可选：打开演示，勾选自动点击即可，无需快捷键；前台模式切到演示窗口；应点击一次，变成方块。
5. 切到 Codex，先用仅观察模式按 F8，核对图标状态；再启用自动点击。

快捷键可能与本机不同，以窗口说明为准。完整操作与 F7 手动备用仍内置在 EXE 的“使用说明”里。

## 本地不能替代的新机验证

尚未实际测试第二台机器的 Codex 版本、主题、DPI、多显示器组合或进程权限差异。适用范围是 Windows 10/11 x64 且系统 .NET Framework 正常可用；不包含 macOS、Linux、32 位 Windows 或 ARM64 原生支持承诺。

没有第二台机器时，可以确认文件独立性和本机行为，但不能保证所有新机环境完全相同。当前没有发现阻碍单文件迁移的本机依赖。

验证依据及报告：`tests/last-result.txt`、`tests/portable-result.txt`、`tests/migration-result.txt`。后者记录了最终被验收 EXE 的 SHA256，可用于检查复制前后文件是否一致。Windows 系统组件依据：[Microsoft .NET Framework 兼容表](https://learn.microsoft.com/en-us/dotnet/framework/install/versions-and-dependencies)、[应用清单](https://learn.microsoft.com/en-us/windows/win32/sbscs/application-manifests)。
