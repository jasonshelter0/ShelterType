# ShelterType 1.1.0

词典图片改为单包读取，减少解压与更新时处理的小文件；暗色模式下字形图片使用应用底色和浅色笔画。

- 6,763 张词典图片集中存放于 Dictionary/Glyphs.zip，查询时只读取选中的图片，不在磁盘展开。文字匹配不等待图片；近期图片有容量受限的内存缓存。
- 图片后台读取与主题转换，快速切换时忽略旧请求。压缩数据先读入独立内存后再解码。
- 亮色模式保留白底黑字；暗色模式将背景映射为当前应用底色，保留笔画边缘的灰度过渡。
- 支持资源校验后选择较小更新包：词典资源完全相同时不再下载该资源；资源缺失、损坏或改变时使用完整包。
- 更新安装会跳过字节完全相同的文件，减少备份与写入；小包安装前再次核对保留的资源，校验失败不替换程序。
- 首次运行新版本时，在后台核对并清理与新资源包一致的旧散装图片。修改过的图片和未知文件保留，UserData 不受影响。

## 下载与更新

首次下载请选择 **ShelterType-win-x64.zip**，完整解压后运行 ShelterType.exe。

从 1.0.0 升级仍下载一次完整兼容包。安装 1.1.0 后，后续版本可按资源校验值自动使用名称含 `code` 的较小包；它供更新器使用，不是独立安装包。源码与原始图片继续保留，方便维护。

未增加第三方运行框架、服务或浏览器组件。应用、词典和练习均可离线使用。

## English

Dictionary glyphs are now read on demand from one indexed ZIP, with background decoding and a bounded cache. Dark mode uses the application background and light strokes. New clients reuse byte-identical dictionary resources and select a smaller update package; missing or changed resources fall back to the full package. Legacy clients still receive a complete compatible ZIP. Existing personal records are preserved.
