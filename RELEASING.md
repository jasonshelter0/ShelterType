# 发布 ShelterType

## 发布约定

当前官方渠道是 `jasonshelter0/ShelterType` 的正式 GitHub Release。版本号从 `src/TypeEasy.Windows/TypeEasy.Windows.csproj` 的 `Version` 读取，使用 `Major.Minor.Patch`；标签使用 `v` 前缀，例如 `v0.7.0`。不要用同一版本标签发布不同的程序。

发布包为 self-contained Windows x64 便携 ZIP。用户无需额外安装 .NET；源码中没有浏览器运行时或第三方更新框架。

## 构建与验证

1. 更新程序集版本和发布说明。检查 `Data/`、`Teaching/` 的变更是否为预期课程更新；不要改变现有课程 ID。
2. 在 Windows 运行 `./package.ps1`。脚本拒绝覆盖已存在的 ZIP，可通过 `-OutputDirectory` 指定新的验证目录。
3. 在全新解压目录启动 `ShelterType.exe`，检查关于页版本、课程、三语切换、续练、测速与统计。不要从真实用户目录生成包。
4. 运行 `./test.ps1 -IncludeUpdate`。更新测试应覆盖错误校验值、非法 ZIP 路径、写入失败回滚、退出等待、个人数据保留和更新后启动。
5. 检查 ZIP 根目录包含 `ShelterType.exe`、`ShelterType.dll`、`ShelterType.runtimeconfig.json`、课程和运行时文件，且不存在 `UserData/`、`TestData/`、`.git/`、SDK 或凭据。复制说明和致谢时保留第三方许可信息。

GitHub Actions 的 Windows workflow 只构建和保存检查用 artifact，没有发布权限；正式 Release 由维护者在检查后发布。

## 发布命令示例

下列命令由已经登录 GitHub CLI、具有仓库写入权限的维护者执行。先推送对应提交和标签，然后创建草稿并上传两个资产：

```powershell
git tag v0.7.0
git push origin main
git push origin v0.7.0
gh release create v0.7.0 ./artifacts/releases/0.7.0/ShelterType-win-x64.zip ./artifacts/releases/0.7.0/ShelterType-win-x64.zip.sha256 --repo jasonshelter0/ShelterType --verify-tag --draft --title 'ShelterType 0.7.0' --notes-file RELEASE-NOTES.md
```

确认附件完整、版本和说明正确后：

```powershell
gh release edit v0.7.0 --repo jasonshelter0/ShelterType --draft=false --latest
```

GitHub 自动为标签提供源码 ZIP/TAR.GZ。程序检查 `releases/latest`，忽略草稿和预发布版本；发布正式版后，旧版本才会提示更新。不要在公开发布时将测试用的较高假版本标记为 latest。

在 WSL 中可使用同一套 `git`/`gh` 命令，Windows 文件路径换为对应的 `/mnt/c/...` 路径。不要将 GitHub Token 放入源码、发布包或应用配置；公共更新不需要用户凭据。

## 更新资产兼容性

- 固定文件名：`ShelterType-win-x64.zip` 和 `ShelterType-win-x64.zip.sha256`。
- ZIP 内直接放置程序文件，不再加一层版本目录。
- 校验文件格式为一行 `64位SHA256十六进制值  ShelterType-win-x64.zip`。
- 不在更新包中放入个人记录。更新器保留包中没有出现的现有文件。
- 更新器来自本机应用内嵌代码，不从 Release 下载并执行额外脚本。
- 自动更新是下载、校验、等待退出、备份替换、失败回滚和重启，不安装服务，也不要求管理员权限。

后续如改变文件结构、运行时、更新协议或数据格式，应先验证从最近正式版本升级，再发布新版本。
