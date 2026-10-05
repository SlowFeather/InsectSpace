# 后端基础骨架验收

日期：2026-10-05。

本阶段在本机 WSL2 `Ubuntu-24.04` 原生运行 MySQL 8 和 Redis 7，Windows 侧运行 .NET 宿主进程。Gateway、Identity、Lobby、World、Battle、Worker 使用独立端口 `8080` 至 `8085`，共享 MySQL/Redis。

## 手机号白名单

通过环境变量 `INSECTSPACE_PHONE_WHITELIST` 配置 E.164 手机号，支持逗号、分号和换行分隔。未配置时白名单关闭。白名单手机号调用 `/v1/identity/phone/login` 时，`code` 可以省略、为 `null` 或为空字符串，直接创建业务会话；不在白名单的手机号始终要求六位 OTP。白名单号码不会写入仓库，密钥和本地环境文件位于被忽略的 `.artifacts/` 路径。

身份和短信分别通过 `IIdentityProvider`、`IPhoneCodeSender` 契约注入。当前仓库提供 local/test provider；生产短信模式没有配置供应商时 fail-closed，不会伪造发送成功。

## 已执行检查

| 检查 | 结果 |
| --- | --- |
| `dotnet build server/InsectSpace.BackendHost/InsectSpace.BackendHost.csproj --nologo` | 通过，0 警告、0 错误 |
| `tools/Test-Backend.ps1` | 通过：WSL2 MySQL/Redis、六进程健康检查、白名单免验证码登录、非白名单 OTP、世界路由、跨服房间、领域命令幂等 |
| `tools/Test-Foundation.ps1` | 通过，85/85 |
| `tools/Test-Architecture.ps1` | 通过 |
| `git diff --check` | 通过 |

运行方式：先执行 `./tools/Initialize-WSLBackend.ps1`，再执行 `./tools/Test-Backend.ps1`。测试脚本会停止自己启动的六个宿主进程，但不会删除 WSL2 数据库；因此路由 epoch 和测试操作号必须按可持久化环境处理。

## 范围边界

这是可替换 provider、持久化边界、服务角色和 HTTP 契约的正式骨架。真实短信供应商、微信身份交换、生产结算规则、CDN、微信传输和真机发布门禁尚未配置或验证；本地/test provider 不代表生产认证。
