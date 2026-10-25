# 发布与历史版本

`release/README.md` 是面向使用者的历史下载列表。每个 `release/v主.次.补丁/` 包含：

- `CodexAutoResume-v版本-windows-x64.exe`：单文件便携程序。
- `CodexAutoResume-v版本-source.zip`：该版本源码快照。
- `RELEASE_NOTES.md`：变化、限制及历史补构建说明。
- `release.json`：版本、Windows x64、MIT、归档时间、资产来源、大小和 SHA256。
- `SHA256SUMS.txt`：EXE、源码 ZIP、说明和元数据的 SHA256。

`release/CodexAutoResume.exe` 始终是最新版复制入口；`latest.json` 指向版本资产；`releases.json` 是完整历史索引。只有最新入口和索引会更新，历史资产不能同版本换内容。校验值用于检查文件完整性，EXE 尚未进行代码签名。

## 日常开发

运行 `build.ps1 -BuildOnly`，编译和运行回归检查，不发布、不改动 release/packages。专项测试仍使用原来的 `-WorkflowTests`、`-BackgroundTests` 等开关。

## 发布新版本

1. 修改根目录 `VERSION`（如 `1.5.2`）。构建脚本据此生成程序集版本、窗口显示版本和清单版本，源码 ZIP 也使用同一版本。
2. 新建 `docs/releases/v1.5.2.md`，首行以 `# v1.5.2 ` 开始，记录变化和限制；更新 CHANGELOG 与 README。
3. 执行适合本次变化的专项测试，再运行 `build.ps1`。它先编译、回归和单文件隔离检查，再打包源码、归档版本并更新最新入口。
4. 如改动后台输入，另运行 `tests/background-portable.ps1 -ExecutablePath release/CodexAutoResume.exe`。

已存在同版本源码且内容未变时复用原 ZIP。内容变化则要求增加 VERSION。EXE、源码、发布说明中任一资产变化都不能覆盖历史目录；系统 C# 编译器重编译可能产生不同二进制，因此同版本日常验证请使用 `-BuildOnly`，需要再次发布时增加补丁号。

如果新版本已归档，但最新版 EXE 被运行中的工具锁定，关闭工具后可使用 `tools/publish-release.ps1` 对归档内相同文件重试并加 `-PromoteLatest`；不会改写历史资产。历史导入不加此开关，避免退回旧版。不要手工编辑版本目录内文件，完整性检查会拒绝被修改的资产。

## 旧版本的来源

1.2.2、1.3.0、1.3.1、1.4.0 的原 EXE 没有完整留存，本次从对应原始源码 ZIP 补构建，各自通过归档源码自带的回归和便携迁移检查。元数据明确使用 `rebuilt-from-archived-source`，不冒充当年原始二进制。旧版问题也写入各自发布说明。1.5.0 为此前交付的原始文件，SHA256 保持不变。

发布脚本会拒绝 EXE/源码版本不符、损坏资产、同版本替换和意外降低 latest；使用发布锁避免并发写入。`tests/release-pipeline.ps1` 在临时目录执行 20 项检查，不修改真实历史；此集成测试需要本地已有 v1.5.0 与 v1.4.0 的归档附件，纯源码下载者可从历史发布补齐后运行。

## 用于 GitHub Release

现有文件布局可以直接作为 GitHub Release 附件来源：以 `v1.5.2` 为 tag/标题，使用对应 RELEASE_NOTES.md，将版本目录下的 EXE、源码 ZIP、SHA256SUMS.txt、release.json 与说明上传。源仓库保留 VERSION、CHANGELOG、各版本说明和构建脚本；生成的 dist/release/packages 由 .gitignore 排除。

当前整理的是本地归档，没有创建远程仓库或远程发布。这里没有虚构 tag、提交或下载链接。
