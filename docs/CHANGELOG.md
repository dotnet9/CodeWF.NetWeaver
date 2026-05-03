# 更新日志

## Unreleased（2026-05-03）

🔨[优化]-仓库切换为 `Directory.Packages.props` 中央包管理，统一类库、测试项目与示例项目的 NuGet 版本入口
🔨[优化]-开发基线升级到 `.NET 11`，新增 `global.json` 锁定 SDK，并补齐根目录 `pack.bat` 打包脚本
🔨[优化]-统一仓库级打包元数据与 README/CHANGELOG 注入逻辑，完善解决方案清单与开发文档说明

## V2.1.0（2026-04-27）

🔨[优化]-为 `CodeWF.NetWeaver`、`CodeWF.NetWrapper` 建立独立更新日志文档  
🔨[优化]-`UdpSocketClient` 连接组播时优先复用 TCP 已连接的本地端点地址，缺失时自动回退 `0.0.0.0`  
🐛[修复]-修复 `UdpSocketClient` 在 `endpoint` 为空或格式异常时 `IPAddress.Parse` 直接抛出异常的问题
