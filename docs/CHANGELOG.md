# 更新日志（Known）

## V2.1.0（2026-04-27）

🔨[优化]-为 `CodeWF.NetWeaver`、`CodeWF.NetWrapper` 建立独立更新日志文档  
🔨[优化]-`UdpSocketClient` 连接组播时优先复用 TCP 已连接的本地端点地址，缺失时自动回退 `0.0.0.0`  
🐛[修复]-修复 `UdpSocketClient` 在 `endpoint` 为空或格式异常时 `IPAddress.Parse` 直接抛出异常的问题
