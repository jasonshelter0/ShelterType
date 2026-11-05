# ShelterType 1.1.1

修复 1.0.0 / 1.1.0 在独立启动时可能立即退出的问题：图标资源加载过早，在 WPF 注册 pack URI 之前触发 UriFormatException。现在在 Application 实例初始化之后加载图标。

此版本直接使用独立 EXE 检查窗口创建和正常退出。词典资源包、深浅主题图片与资源复用更新功能保持不变。

无法启动旧版的用户，请下载 ShelterType-win-x64.zip，退出程序后完整解压覆盖原程序目录；保留 UserData 文件夹，勿删除个人记录。名称含 code 的包仅供更新器使用，不是完整安装包。

Fix a startup crash caused by loading the application icon before WPF initialized its pack URI support. Download the full ZIP and extract over the existing application folder, preserving UserData.
