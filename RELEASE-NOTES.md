# ShelterType 0.8.0

新增根据本人错误生成的专属练习，课程列表用圆环显示进度。顶栏常用操作换为常规图标，错键统计突出原文语境与具体误按的对应关系。

- 空环表示未完成；进行中的课程显示小进度圆环与百分比；完成后保留完整圆环，中央显示对号。悬停可查看状态和已完成字符数。
- 进度按课程内的当前位置计算，包括长课当前段落中的输入；输入、完成和重新开始后即时刷新。
- 沿用既有练习存档，只查看列表不会改写课程记录。仅打开课程不会标记为进行中。
- 设置齿轮、测速秒表、统计柱图、侧栏和专注模式使用轻量矢量图标。保留简体中文、English、Français 的悬停说明与可访问性名称。

- 错键分析默认展示原文前后文中的具体混淆。点击每条记录，键盘同时标出应按键和误按键；点键可筛选相关记录。
- 原文语境与按误键汇总分别查看，避免重复计数。输入法提交字符以文字呈现，不推断实际物理按键。
- 新增“专属练习”：从个人错键与原文语境生成短课，区分五笔86/98、拼音、英文、数字键盘和中文输入。练到一半可继续，完成后按最新记录生成下一组；没有错误记录时不会编造练习。

## 更新

0.7.0 用户可在“设置 → 检查更新”下载并更新，个人进度自动保留。新版顶栏的设置入口是右上角齿轮。

首次使用：下载 **ShelterType-win-x64.zip**，完整解压到可写目录，运行 **ShelterType.exe**。适用于 Windows 10/11 x64，内含运行环境和完整 721 门课程。

新增只读进度检查覆盖存档恢复、完成、重置、错键、回退和课程变化；课程和统计回归通过。感谢金山打字通 2008 原作者与资料贡献者，授权与致谢见 ACKNOWLEDGEMENTS.md。

## English

The lesson list now shows completion marks and live progress percentages. Settings, speed tests, statistics, the sidebar and focus mode use lightweight vector icons with translated tooltips and accessible names. Error analysis focuses on original text contexts and expected-to-mistyped pairs. Select any error row to highlight both keys. Existing progress and all 721 lessons are preserved.

Personal practice creates short confusion and context exercises from your own mistakes. Unfinished sets remain resumable; completed sets can be followed by a new set based on updated records.

Version 0.7.0 can update from **Settings → Check for updates**. On 0.8.0, Settings is the gear icon at the top right.

## Français

La liste des cours affiche désormais leur état et leur progression en direct. Les commandes principales utilisent des icônes classiques avec des infobulles traduites. La progression existante et les 721 cours sont conservés. Pour mettre à jour la version 0.7.0, ouvrez les réglages puis recherchez une mise à jour.

Les exercices ciblés sont créés à partir de vos propres erreurs et de leur contexte. Une série inachevée reste disponible ; une fois terminée, la suivante tient compte des données actualisées.
