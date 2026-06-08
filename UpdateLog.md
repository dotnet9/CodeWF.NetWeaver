# 更新日志

## 2.1.2.3 (2026-06-08)

- 🔨[优化]-补齐根目录 logo.svg、logo.png、logo.ico 三件套，子工程通过 MSBuild Link 引用根 logo，避免维护多份图标副本。
- 🔨[优化]-统一目标框架：NuGet 包项目支持 `net8.0;net10.0`，Demo、App、测试与内部应用项目升级到 `net11.0` / `net11.0-windows`。
- 🔨[优化]-保留运行时帮助、Markdown 示例、内置备忘录和业务设计文档，仅收敛仓库级重复文档入口。

---

## 旧分支更新日志归档

# 更新日志

## 2.1.2.2 (2026-06-08)

- 统一版本号维护入口，只在仓库根目录 `Directory.Build.props` 中定义 `<Version>`。
- 清理英文/双语文档入口，后续仅维护简体中文文档。
- 完善 NuGet 发布配置，补充 Source Link、符号包和标签格式规范。


## Unreleased（2026-05-04）

🔨[优化]-补充仓库级 `.editorconfig` 与 `.gitattributes`，统一 UTF-8、行尾和 C# 基础样式规则
🔨[优化]-调整核心 NuGet 包描述与标签，提升包检索信息的专业度
🔨[优化]-仓库切换为 `Directory.Packages.props` 中央包管理，统一类库、测试项目与示例项目的 NuGet 版本入口
🔨[优化]-开发基线升级到 `.NET 11`，不再维护 `global.json`，并补齐根目录 `pack.bat` 打包脚本
🔨[优化]-统一仓库级打包元数据与 README/UpdateLog 注入逻辑，完善解决方案清单与开发文档说明

## V2.1.0（2026-04-27）

🔨[优化]-为 `CodeWF.NetWeaver`、`CodeWF.NetWrapper` 建立独立更新日志文档  
🔨[优化]-`UdpSocketClient` 连接组播时优先复用 TCP 已连接的本地端点地址，缺失时自动回退 `0.0.0.0`  
🐛[修复]-修复 `UdpSocketClient` 在 `endpoint` 为空或格式异常时 `IPAddress.Parse` 直接抛出异常的问题

---

## 旧分支更新日志归档

# 更新日志

## Unreleased（2026-05-04）

🔨[优化]-优化 README 中的安装、运行示例和代码片段，补充 `CodeWF.NetWrapper` 的安装入口
🔨[优化]-打磨 AOT 示例代码，使用显式类型构造替代运行时动态创建，减少示例构建告警噪声
🔨[优化]-优化客户端与服务端示例界面圆角、工具栏换行、状态栏截断和传输进度展示
🔨[优化]-`SocketTest.Client` 与 `SocketTest.Server` 升级到 `.NET 11`、`Avalonia 12.0.3` 与 `Semi.Avalonia 12.0.1`
🔨[优化]-同步自研与测试依赖版本：`CodeWF.EventBus 3.4.5.5`、`CodeWF.LogViewer.Avalonia 12.0.3.1`、`CodeWF.Tools.* 1.3.13.2`、`coverlet.collector 10.0.1`
🔨[优化]-示例工程改用开源 `ProDataGrid` 和自研开源 `CodeWF.AvaloniaControls.ProDataGrid.Themes 12.0.3.2`，替代旧版免费 `Avalonia.Controls.DataGrid` 方案
🔨[优化]-统一示例发布配置与根目录 `publish_all.bat` / `publishbase.bat` 脚本，发布输出集中到根目录 `publish/`
🐛[修复]-适配 Avalonia 12，将示例界面中的 `TextBox.Watermark` 替换为 `PlaceholderText`

## V2.1.0（2026-04-27）

😄[新增]-为测试示例建立独立更新日志文档  
🔨[优化]-服务端启动后后台静默预热首轮进程快照，`RequestServiceInfo` 改为直接读取缓存并快速响应  
🔨[优化]-客户端连接握手调整为先请求 `RequestServiceInfo`，收到响应后再请求 `RequestUdpAddress`  
🔨[优化]-服务端与客户端对进程结构变化通知和整表刷新增加 1 秒防抖，减少高频抖动  
🔨[优化]-客户端和服务端补充请求响应耗时日志，便于定位通信慢链路  
🐛[修复]-修复服务端配置非法 IP 时的启动异常，自动回退到可用地址  
🐛[修复]-修复远程文件浏览器初始化、目录应用和重置时跨线程访问 UI 导致的异常  
🐛[修复]-修复 UDP 连接链路因空端点或非法地址导致的 `An invalid IP address was specified.` 异常
## 2026-06-08 仓库规范整理

- 统一文档维护入口：每个仓库只保留根目录 `README.md` 和根目录 `UpdateLog.md`，清理重复日志、英文文档和语言切换入口。
- 统一版本维护入口：包版本只在仓库根目录 `Directory.Build.props` 的 `<Version>` 节点维护，移除散落的程序集版本配置。
- 不再维护 `global.json`，SDK 选择交给本机或 CI 环境；NuGet 包和应用的目标框架在项目文件中明确声明。
- 统一 NuGet 包文档入口：包 README 统一引用仓库根 `README.md`，更新日志统一引用仓库根 `UpdateLog.md`。
