# 发布与历史版本

[首页](../README.md) · [版本说明](releases/README.md) · [更新日志模板](releases/TEMPLATE.md)

## 版本规则

默认使用 **1.1.x**，每次正式更新只递增补丁号；当前版本以根目录 VERSION 为准，下一版通常递增一个补丁号。新增功能、界面调整、重构或文档发布都不自动提升次版本。

只有维护者明确允许后，才能进入 1.2.x 或其他系列。AI 可以提出升级建议，但“继续工作”“迭代”“发布”等一般指令不代表同意升次版本，也不能由 AI 修改规则来自行批准。

规则保存在 [version-policy.json](../version-policy.json) 与 [AGENTS.md](../AGENTS.md)。构建、源码打包和本地发布都执行版本检查；没有用于绕过检查的命令行开关。

## Release 展示格式

每个 GitHub Release 使用统一标题 **v主.次.补丁 · CodexAutoResume**，例如 `v1.1.7 · CodexAutoResume`。版本号放在前面，左侧导航较窄时也能直接区分各版本。

浏览完整历史使用[全部 Releases](https://github.com/ColinKiiim/codex-auto-resume/releases)：左侧是版本导航，右侧可以向下连续查看。`/releases/tag/v版本号` 是单版详情页，`/releases/latest` 跳转到最新版详情。

正文先中文、后英文，两部分使用相同版本号和日期，按本次实际变化选用以下分类：

| 中文 | English | 内容 |
|---|---|---|
| 新增 | Added | 新功能及用途 |
| 变更 | Changed | 已有行为的调整 |
| 修复 | Fixed | 具体问题和修复后的行为 |
| 移除 | Removed | 本版移除的功能 |

没有内容的分类直接删掉。说明聚焦用户能看到的变化，测试记录放在开发文档。附件由 GitHub 的 **Assets** 展示，不在正文重复列文件清单。

格式参考 [Cockpit Tools 的 Release](https://github.com/jlcodes99/cockpit-tools/releases/tag/v1.3.74)。内容按本项目各版实际变化编写。

## 新版本步骤

1. 修改根目录 `VERSION`，统一编译、界面、清单与源码包的版本号。
2. 复制 [TEMPLATE.md](releases/TEMPLATE.md) 为 `docs/releases/v版本号.md`，替换版本、日期与条目，删除空分类；同步更新 CHANGELOG 和版本列表。
3. 日常验证运行 `.\build.ps1 -BuildOnly`，按修改范围运行相关专项检查。
4. 运行 `.\build.ps1`，验证候选 EXE、生成源码包、本地版本归档和 GitHub Release 导出文件。
5. 按用户默认要求，提交并推送对应源码，创建匹配的 `v版本号` tag，上传 EXE、源码 ZIP 和 SHA256SUMS.txt；完整附件检查通过后正式发布，并将最新版本标记 Latest。没有本轮限制时不再为常规推送和发布重复确认。
6. 通过官方 `releases/latest` 接口核对实际版本，检查下载附件、校验值和旧版的更新发现逻辑；仅本地生成导出目录不算完成发布。

应用版本的源头是 [VERSION](../VERSION)。已发布源码和二进制保留版本快照；开发中的文档变化在下一次发布纳入新源码包。

## 可直接上传的内容

每次本地归档会生成 `release/github/v版本号/`：

```text
v1.1.7/
  release-body.md          GitHub Release 正文
  release-draft.json       标题、tag、正文及草稿状态
  assets/
    CodexAutoResume-v1.1.7-windows-x64.exe
    CodexAutoResume-v1.1.7-source.zip
    SHA256SUMS.txt
```

GitHub 标题填写 `v1.1.7 · CodexAutoResume`，正文粘贴 `release-body.md`，将 `assets/` 内的三个文件上传到 Assets。`release-draft.json` 可用于后续发布工具集成，它不会自行创建或发布远程 Release。导出不包含仓库地址，tag 需先与实际源码对应。

普通用户下载 EXE，开发者下载 source.zip。GitHub 自动提供的 Source code ZIP/TAR 是 tag 的仓库快照；手动上传的 source.zip 是构建脚本生成的版本源码包。

GitHub 导出目录内的 `SHA256SUMS.txt` 对应其 EXE 和源码 ZIP，可在下载目录运行：

```powershell
Get-FileHash .\CodexAutoResume-v1.1.7-windows-x64.exe -Algorithm SHA256
```

## 本地版本归档

`release/v版本号/` 保存 EXE、源码 ZIP、RELEASE_NOTES.md、release.json 和完整的 SHA256SUMS.txt。`release/CodexAutoResume.exe` 是最新便携入口；`latest.json` 和 `releases.json` 提供版本索引，`release/README.md` 提供本地历史列表。

本地完整校验清单同时覆盖说明与元数据；GitHub 导出的校验清单只覆盖该目录中实际上传的两个下载文件。

已存在版本的不同二进制或源码不会覆盖原归档。相同文件可以重复执行 `tools/publish-release.ps1` 重新生成索引和 GitHub 导出内容；旧版归档不加 `-PromoteLatest`。若最新 EXE 正在运行，先关闭工具再更新最新入口。

## 检查与源码范围

`tests/release-pipeline.ps1` 使用当前候选 EXE 和临时源码夹具检查版本对应、完整性、历史保护、最新入口和下载附件校验，不依赖旧版发布附件。后台捕获/消息点击改动另运行 `tests/background-portable.ps1 -ExecutablePath release/CodexAutoResume.exe`；真实输入验证范围单独记录，不能用自带按钮自测替代。

源仓库保留源码、文档、资源、构建脚本和模板；`dist/`、`release/`、`packages/` 由 `.gitignore` 排除，生成的 Assets 单独上传到 GitHub Releases。
