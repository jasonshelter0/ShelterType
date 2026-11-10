# ShelterType

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="Branding/ShelterType-lockup-dark.svg">
  <img src="Branding/ShelterType-lockup-light.svg" alt="ShelterType — Shelter 双三角形 S 与键帽标志" width="450">
</picture>

精简的 Windows 原生打字练习程序。保留熟悉的五笔、英文、中文课程体验，加入续练、暗色模式和个人统计；使用 WPF，不包含 WebView、Electron 或浏览器内核。

## 下载与运行

前往 [Releases](https://github.com/jasonshelter0/ShelterType/releases/latest)，下载 `ShelterType-win-x64.zip`，完整解压到有写入权限的文件夹，再双击 `ShelterType.exe`。

适用于 Windows 10/11 x64。发布包自带 .NET 8 Windows Desktop 运行时，无需安装 .NET，也无需管理员权限。请勿直接在压缩包内运行；移动时保留整个文件夹。

本项目选择便携版，便于迭代、备份和迁移。程序、课程与个人记录放在同一目录，无后台服务。要移除程序，关闭后删除该目录即可；需要保留记录时，先备份 `UserData/`。

## 品牌

Shelter 双三角形 S 是跨项目品牌标记，ShelterType 使用带键帽的 A 方案。深浅背景、单色及名称组合源文件保存在 [Branding](Branding/使用说明.txt)。

## 功能

- 内置五笔词典与字根表；课程和测速中按 F2 随时查词，测速计时继续。
- 适用课程可乱序复习，已完成课程可重新练习；测速支持随机文章与 Enter 开始。
- 五笔 86/98、英文、中文、拼音和入门指法课程，按层级分类。
- 每门课独立保存进度，包括长课中当前项内的输入位置。
- 课程列表以空环、进度圆环、圆环中央对号区分未完成、进行中与已完成；进度随输入即时更新。
- 设置、测速、统计、侧栏和专注模式采用常规矢量图标，保留三语悬停说明与键盘访问。
- 方形键帽、分指配色和指法演示；课程侧栏可收起，F11 进入专注模式。
- 深色、浅色和跟随系统主题；简体中文、English、Français 界面。
- 限时测速、速度与正确率趋势、每日 GitHub 风格热力图。
- 键盘上的频次、错键、错误率、混淆与上下文统计；文章逐字速度热力图。
- 按原文前后文查看“应按 → 误按”记录，点击每条记录在键盘上联动标出两个键；也可按误键汇总查看。
- 专属练习根据本人的错键生成易混输入和语境巩固短课；未完成的一组可以继续，完成后按最新记录生成下一组。
- 检查 GitHub 正式版本、下载更新并在退出后替换程序，保留个人练习记录。

语言选择改变界面文字，保留课程原文。指法键盘采用 QWERTY 布局。键盘上下文分析是本地统计关联，不判断错误的因果关系；可选的 AI 解读需要另行配置。

## AI 解读与文章管理

在设置中打开 AI 连接与对话，填写 OpenAI 兼容的 API Base URL（如 `https://服务域名/v1`）、模型名称和 API Key。也可使用本机兼容服务的 `http://localhost:端口/v1`。接口格式可选 OpenAI Chat Completions、OpenAI Responses、Anthropic Messages，分别追加 `/chat/completions`、`/responses`、`/messages`；地址应填写到 `/v1`，不要包含最终接口路径。远程服务须使用 HTTPS。Responses 请求设置 `store: false`；Anthropic 使用 API Key 认证。不内置模型、代理服务或额外运行框架。

测试结束、单篇热力图和个人统计均提供 AI 入口。点击发送前可查看摘要；只发送压缩统计和最近六条消息，不发送整篇练习文章。对话保存于 `UserData/AI`，密钥由 Windows 当前用户加密，迁移到另一台电脑后需要重新填写。离线功能无需 API。第三方服务的费用与保留政策由服务提供者决定。

文章管理可导入 UTF-8 TXT、打开原文目录、移除或恢复文章。自有文章在 `UserData/Articles`；移除表示从课程列表隐藏，保留文件和练习记录。内置文章也可隐藏，后续更新不会撤销隐藏状态。

课程旁的乱序图标会直接打乱当前适用课程，并重置本轮进度；新顺序和进度自动保存，重开仍在原课程继续。测试页的随机文章选择保持独立。

统计中的“全部文章”表示全部已保存测试，并非当前课程累计数据。总览中的“累计错键（课程与测试）”保留旧版累计统计；旧测试缺少逐键记录时只展示其成绩，不推算不存在的详情。

## 词典资源与更新大小

词典的 6,763 张图片在发布包中集中为 Dictionary/Glyphs.zip。搜索使用文字索引，选中结果后才在后台读取单张图片；不解压图片到磁盘。暗色模式使用应用底色与浅色笔画，亮色模式保留白底。

1.2.0 起只发布完整 Windows ZIP 和 SHA-256 校验文件，不再发布 code 包。更新器仍兼容旧版资源复用协议；没有 code 附件时自动下载完整包。安装会保留个人记录，并跳过内容相同的文件。词典继续使用单个图片资源包。

## 更新与数据

更新来自本仓库的 GitHub Releases。启动后约 5 秒检查，每 24 小时最多自动检查一次。检测到较新的正式版时，以非模态通知提醒；点击下载并更新后，程序下载 ZIP、核对 SHA-256、保存记录，再由独立更新器完成替换和重启。练习期间不会静默替换正在使用的程序。

`UserData/` 保存课程进度、设置、测速和热力图数据，不随版本发布，也不会被更新包覆盖。课程和教学素材可以随新版本更新。网络检查与下载不上传练习内容或个人统计；练习本身可离线使用。关闭自动检查后，仍可手动检查更新。

更新包必须包含同一 Release 下的两个固定资产：

```text
ShelterType-win-x64.zip
ShelterType-win-x64.zip.sha256
```

SHA-256 用于校验下载完整性，不等同于 Windows 代码签名。当前发布包未进行 Authenticode 签名。

## 本地构建

在 Windows 安装 .NET 8 SDK 和 PowerShell，从仓库根目录运行：

```powershell
./package.ps1
```

脚本从源码创建全新的 self-contained Windows x64 发布目录，复制明确列出的 `Data/`、`Teaching/` 与说明文件，然后在 `artifacts/releases/<版本>/` 生成 ZIP 和校验文件。首次构建需要访问官方 NuGet 源下载 Microsoft 运行时包。它不会从已使用的 `app/` 目录打包，也不会更改全局 NuGet 配置。

只编译程序，可运行 `./build.ps1`；使用自定义 SDK 时传入 `-Sdk 'C:\path\dotnet.exe'`。每次打包使用新的暂存目录；若相同版本的发布文件已存在，请为 `package.ps1` 指定新的 `-OutputDirectory`。

运行回归测试：`./test.ps1 -IncludeUpdate`。测试先验证异常捕获边界，再顺序构建、运行练习、统计、热力图、指法、课程与更新用例，所有写入均位于隔离的临时测试目录。课程测试默认读取仓库根目录的 `Data/` 和 `Teaching/`，不依赖开发者的 D 盘路径。

主要目录：

| 目录 | 内容 |
| --- | --- |
| `src/TypeEasy.Core` | 课程读取、续练、测速和统计；历史内部名称保留以减少迁移风险 |
| `src/TypeEasy.Windows` | ShelterType 的原生 WPF 界面 |
| `src/ShelterType.Update` | GitHub 更新检查、下载校验与独立更新器 |
| `tests` | 练习、课程、统计与更新测试 |
| `Data`、`Teaching` | 经项目维护者确认获准分发的课程与教学素材 |

发布流程见 [RELEASING.md](RELEASING.md)，用户测试步骤见 [使用与测试.txt](使用与测试.txt)。Windows 构建是当前重点；核心逻辑与界面分离，以便将来迁移其他平台。

## 致谢与许可

感谢金山打字通 2008 的原作者、课程及教学素材创作者。项目维护者已确认获得将原版资料随 ShelterType 发布的同意。资料的权利仍归原权利人，详情见 [致谢](ACKNOWLEDGEMENTS.md) 和 [第三方说明](THIRD-PARTY-NOTICES.md)。ShelterType 为独立项目，并非金山官方产品。

本项目尚未为自有源码选定开源许可证；公开源码不代表所有源码及素材已采用相同许可。

## English

ShelterType is a lightweight native Windows typing trainer with Wubi 86/98, English and Chinese lessons, resumable progress, finger guidance, speed tests and local heatmaps. Its interface supports Simplified Chinese, English and French. It uses WPF and does not include a web engine.

Download `ShelterType-win-x64.zip` from [Releases](https://github.com/jasonshelter0/ShelterType/releases/latest), extract the entire archive to a writable folder and run `ShelterType.exe`. The Windows x64 portable package includes .NET; no separate runtime installation is required. Updates preserve `UserData/`. Original course materials are included with permission confirmed by the project maintainer; see the acknowledgements above.

## 设备迁移与开发交接

新设备构建步骤、个人数据迁移、产品决策和历史故障注意事项见 [MIGRATION-HANDOFF.txt](MIGRATION-HANDOFF.txt)。个人记录与 AI 密钥不在仓库中；请另行迁移 UserData，并在新设备重新填写密钥。

## 坚果云备份（1.3.0 开发版）

设置 → 备份与恢复中填写坚果云邮箱与第三方应用密码，可连接、上传及选择恢复云端备份。文件位于坚果云 /ShelterType；无需服务器或额外运行框架。最多 200 个备份，达到数量限制后请在坚果云中清理旧备份。单个压缩包上限 50 MB，展开上限 200 MB。

备份包括进度、统计、热力图、乱序与专属练习、文章和收藏；AI 对话默认不包含，可自行勾选。AI 密钥、坚果云凭据、本机设置、临时文件不上传。云端数据由你的坚果云账号管理，此版本不另加端到端加密。

恢复采用替换而非合并：下载校验后在下次启动时应用，先将当前记录保存在 UserData/CloudBackup/before-restore-*.zip。可使用“恢复本机备份”撤回到旧记录。应用密码在本机使用 Windows 用户加密；换设备后重新填写。实际连接需用户自己的坚果云账号验证。