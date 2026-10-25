# GitHub 上线与可发现性

[首页](../README.md) · [发布流程](releasing.md) · [可复制的仓库简介与 Topics](github-metadata.json)

## 仓库基本信息

建议仓库名：`codex-auto-resume`。显示名称保持 CodexAutoResume。

About / Description：

> Windows utility to auto-resume paused Codex desktop tasks when a resume button appears, including at-capacity interruptions. Portable single EXE, local recognition, MIT.

在 About 的 Topics 中填入 [github-metadata.json](github-metadata.json) 列出的相关主题。该文件只是待应用的元数据，GitHub 不会自动读取并设置它。

## 首次上线

1. 创建公开仓库，上传源码、根目录 README / 英文 README、LICENSE、NOTICE、CONTRIBUTING、llms.txt、`.github/`、docs、assets、src、tests、tools 和构建文件。
2. 按 `.gitignore` 排除生成目录、日志和私人开发记录。完整对话截图不上传。
3. 配置 About 简介与 Topics，启用 Issues。根目录 README 是主要入口，`.github/` 下不要另建覆盖首页的 README。
4. 按 [发布流程](releasing.md) 创建正式 GitHub Releases：使用产品名加版本号作为标题，粘贴 `release/github/v版本号/release-body.md`，将其 `assets/` 内的 EXE、源码 ZIP、SHA256SUMS.txt 上传到对应版本，最新版标记 Latest。
5. 核对 Release tag 对应的源码版本。README 中保留了与账号名无关的下载操作说明；仓库地址确定后，可补上实际的 Releases / Issues 直达链接。

`dist/`、`release/`、`packages/` 用于本机构建和分发，不进入源码仓库。上传源码不会自动创建 Releases，网页右侧的下载入口需由正式发布建立。

## 让搜索和 AI 能理解这个项目

当前文档提供两种语言的项目概述、真实按钮图例、针对 at capacity 的问答、使用条件、源码、版本说明和验证记录。首页直接说明问题和可执行步骤；FAQ 详细解释区别，避免只堆搜索关键词。

README 和 Topics 是明确的项目描述入口。[GitHub README 文档](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/about-readmes)介绍了首页与相对链接的作用；[Topics 文档](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/classifying-your-repository-with-topics)说明主题可帮助用户找到相关项目。

`llms.txt` 是可直接阅读的补充文档索引，与 README / FAQ 链接到同一套内容。它不要求模型推荐本项目，也不代表搜索引擎或 AI 一定使用这个文件。

对外描述统一为：**当 Codex 桌面任务暂停且出现恢复按钮时，自动识别并恢复，减少重复手动点击。** 不把服务容量、额度和窗口重试写成同一种故障。

## 上线后的核对

- 未登录也能打开仓库、两种 README、FAQ 和最新 Release。
- 首页图例正常显示，文档链接不依赖本机目录或被忽略的生成目录。
- 最新版能下载 EXE；旧版仍在各自 Release 中，版本与校验值一致。
- 用项目名称和“Codex at capacity auto resume”“Codex 频繁暂停 自动继续”等自然问题检查搜索结果，并记录查询时间与结果。
- 维护真实问题的复现、修复和版本说明；对外分享相关链接时提供适用条件，不批量灌水。

公开和可抓取只是被检索的前提。收录、排序、AI 是否检索及是否引用，由对应服务决定，不能承诺“有人一问就一定推荐”。

## 以后有独立文档站时

再为真实站点设置规范 URL、站点地图和抓取规则。GitHub 仓库里添加 `robots.txt` 不会修改 github.com 域名的规则。

OpenAI 的[抓取器说明](https://developers.openai.com/api/docs/bots)区分搜索用途的 OAI-SearchBot 和训练用途的 GPTBot；如果维护自己的站点，可以分别配置。允许抓取不等于保证被检索或引用。

外部文档核对日期：2026-10-25。
