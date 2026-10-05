# ADR-0006：WSL 原生拓扑与客户端持久化会话

日期：2026-10-05。状态：开发分支已实现并完成本地回归；**核心平台/服务端正式评审待完成**。本记录不是发布批准。

## 决策与所有权

- BackendHost 按六角色在 WSL 启动原生 Linux 进程，共用 MySQL/Redis。Gateway 当前聚合 HTTP 入口，尚无生产连接网关和完整业务处理器。
- Identity 使用 32 字节随机 opaque Token，持久化 hash、玩家、归属区、固定到期/撤销状态；每次校验读取 MySQL 权威。默认 30 天可配置。
- 手机号/Token 登录、大厅 UI、HTTP DTO 和安全缓存契约放在 HotUpdate/Modules/Lobby；不修改 shared/vendor。
- Runtime 平台区增加 BootConfiguration 可选服务字段、显式 Editor 开关、启动进度 Overlay 和 YooAsset 进度属性。这些修改待平台评审；AOT 不引用热更程序集和生成表。
- ISessionCacheProtector 暂属 Lobby 契约，Windows DPAPI 在热更程序集实现；Editor 已验，原生 HybridCLR P/Invoke/AOT 保留及微信安全存储仍需验证。
- WSL 资源宿主只暴露既有发布清单文件，复制到 ext4 并校验 SHA-256，沿用 YooAsset/HybridCLR 发布格式和母包门禁。

## 兼容与迁移

BootConfiguration 新字段保守默认，原 localSmokeMode 不变；未配置后端的 online 入口明确失败。Editor 的 INSECTSPACE_EDITOR_BACKEND=true 由启动脚本设置，停止清除并恢复场景，不写正式 JSON。正式构建拒绝 localBackendMode，仅明确开发环境允许 loopback HTTP。

新增 `/v1/identity/session/login` 使用 Bearer Token，返回 playerId/homeRealmId/expiresAt，不返回凭据/hash。数据库 schema 不变，旧会话保持原期限。health 新增 OS/PID。缓存为可重建本机数据；不兼容、损坏或解密失败时清理重新认证。

## 回滚与发布

停止客户端/六宿主后回滚本轮开发提交，数据库无需降级；缓存可清理后重新登录。缩短配置不回溯修改旧 Token，如需强制退出使用服务端撤销。资源按不可变版本切换，停止宿主后指定验证过的版本。

Foundation、Architecture、Unity EditMode/PlayMode、WSL 后端、真实 UI 登录/恢复/撤销、YooAsset WSL 读取均有本地验证，见 [Backend-Validation.md](Backend-Validation.md)。本轮未重建/运行原生 HybridCLR 母包；历史原生记录不覆盖本轮 Runtime/DPAPI 变化。短信、微信、TLS、AOI、PvP 与正式结算未验收，发布仍需 WeChat-Release-Gates 和真实平台评审。
