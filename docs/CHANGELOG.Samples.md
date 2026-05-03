# 更新日志

## Unreleased（2026-05-03）

🔨[优化]-`SocketTest.Client` 与 `SocketTest.Server` 升级到 `.NET 11`、`Avalonia 12.0.2` 与 `Semi.Avalonia 12.0.1`
🔨[优化]-示例工程改用 `CodeWF.AvaloniaControls.ProDataGrid` 和 `ProDataGridSemiTheme`，替代旧版免费 `Avalonia.Controls.DataGrid` 方案
🔨[优化]-统一示例发布配置与根目录 `publish_all.bat` / `publishbase.bat` 脚本，发布输出集中到根目录 `publish/`
🐛[修复]-适配 Avalonia 12，将示例界面中的 `TextBox.Watermark` 替换为 `PlaceholderText`

## V2.1.0（2026-04-27）

😄[新增]-在 `Directory.Build.props` 中增加 `TestSamplesVersion`，独立维护测试示例版本号  
😄[新增]-为测试示例建立独立更新日志文档  
🔨[优化]-服务端启动后后台静默预热首轮进程快照，`RequestServiceInfo` 改为直接读取缓存并快速响应  
🔨[优化]-客户端连接握手调整为先请求 `RequestServiceInfo`，收到响应后再请求 `RequestUdpAddress`  
🔨[优化]-服务端与客户端对进程结构变化通知和整表刷新增加 1 秒防抖，减少高频抖动  
🔨[优化]-客户端和服务端补充请求响应耗时日志，便于定位通信慢链路  
🐛[修复]-修复服务端配置非法 IP 时的启动异常，自动回退到可用地址  
🐛[修复]-修复远程文件浏览器初始化、目录应用和重置时跨线程访问 UI 导致的异常  
🐛[修复]-修复 UDP 连接链路因空端点或非法地址导致的 `An invalid IP address was specified.` 异常
