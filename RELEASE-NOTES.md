# ShelterType 0.7.0

ShelterType 首个 GitHub 正式版本。原生 Windows 打字练习，保留熟悉的课程体验，支持离线使用。

## 下载

下载 **ShelterType-win-x64.zip**，完整解压到可写目录，双击 **ShelterType.exe**。适用于 Windows 10/11 x64，自带 .NET Windows Desktop 运行环境，无需安装器或管理员权限。

## 本版内容

- 正式更名 ShelterType，增加项目入口与授权资料致谢。
- 完整 721 门课程，包含五笔 86/98、英文、中文、拼音及入门指法。
- 每课续练、方形键帽与分指配色、指法演示和专注模式。
- 深浅主题，中英法界面。
- 打字测速、个人趋势、每日练习热力图、文章速度热力图和键盘错键分析。
- 自动检测 GitHub 正式更新，可在设置中关闭或手动检查；发现新版本后提醒，点击下载并更新即可完成校验、保存进度、更新和重启。
- 更新失败回滚；保留 UserData 和包外的本地文件。

## 从旧测试版迁移

关闭旧程序，将旧版程序旁的 **UserData** 整个文件夹复制到新 **ShelterType.exe** 旁边。课程编号和内容保持一致，可以继续既有进度。自动更新从本版起提供；首次从旧测试版迁移需要解压新版。

## 验证与说明

核心、课程和统计 1305 项断言通过；更新器 120 项检查和独立安装/回滚复核通过；中英法更新界面和最终包安装重启已验证。发布包不包含个人记录。

本版提供自包含便携包，更新需要该目录有写入权限。SHA-256 文件用于下载完整性校验；当前程序未进行 Authenticode 代码签名。源码及课程资料的授权说明见 README、ACKNOWLEDGEMENTS.md 和 THIRD-PARTY-NOTICES.md。

感谢金山打字通 2008 原作者及课程、词库、教学资料贡献者。原版资料经项目维护者确认取得授权后随本版提供，版权归原权利人所有。ShelterType 是独立项目，并非金山官方产品。

## English

First public ShelterType release: a native Windows typing tutor with 721 lessons, resumable practice, Wubi 86/98, finger guidance, speed tests, local statistics, and a Chinese/English/French interface.

Extract the complete **ShelterType-win-x64.zip** and run **ShelterType.exe**. The Windows 10/11 x64 portable package includes .NET. Updates are checked automatically and installed only after you choose to download and update. Practice data stays in **UserData**. To migrate from the earlier TypeEasy Next preview, close it and copy its UserData folder beside the new executable.
