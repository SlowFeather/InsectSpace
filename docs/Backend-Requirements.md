# WSL2 后端与客户端需求基线

日期：2026-10-06。按用户本轮确认收敛范围；不覆盖其他玩法分支的内存原型。当前交付摘要见 [工程状态](Project-Status.md)。

| 领域 | 当前交付 | 暂不交付 |
| --- | --- | --- |
| 部署 | WSL2 原生 MySQL/Redis；Gateway、Identity、Lobby、World、Battle、Worker 六个 Linux 进程；脚本编排 | 多机高可用、生产 TLS、容灾与容量验收 |
| 身份 | 可替换 IIdentityProvider、local/test provider、手机号 OTP 契约、白名单直登 | 微信真实身份适配 |
| 短信 | IPhoneCodeSender；明确的 dev/test 验证码；production 无供应商即失败 | 用户尚未选择供应商，签名/模板/密钥暂空 |
| 登录缓存 | 首次手机号登录后保护本机 Token，启动自动校验；默认 30 天可配置；退出撤销 | 微信/移动端安全存储适配及真机验收 |
| 大厅/世界 | Unity UGUI 登录、大厅、世界路由、WorldCommon 场景加载 | 真实 AOI 广播与多人同步 |
| 经济/背包/任务/活动 | 领域命令契约、持久化 inbox、幂等键与服务边界 | 正式规则、数值、奖励/支付/背包结算处理器 |
| 跨服对战 | 归属区、世界实例、独立房间身份分离，房间创建/复用契约 | 多人匹配、跨机迁移、PvP 帧同步及结算 |
| 更新 | 沿用 YooAsset/HybridCLR 构建门禁；WSL 只读托管、版本/hash/真实资源读取验证 | Editor 程序集模式不等于原生 HybridCLR 或微信发布验收 |

## Token 语义

- “一个月”落实为签发起固定 **30 天 / 720 小时**，不是自然月。`INSECTSPACE_SESSION_HOURS` 可设 1–8760 整数小时；空值为 720，非法值启动失败。
- 修改配置只影响新签发会话，已签发会话到期不变；恢复登录不续期、不返回新 Token。
- 短信/白名单登录均签发 Token。MySQL 的到期/撤销记录是权威；Redis 旧缓存不能复活已失效会话。
- Windows 使用当前用户 DPAPI，缓存按 Identity 地址隔离。损坏/过期/HTTP 401、403 清缓存；断网保留并可重试。
- 其他平台通过 `ISessionCacheProtector` 适配安全存储；未接入时明确提示不持久化，不降级到明文 PlayerPrefs。
- 用户指定白名单号码仅保留本机忽略配置，不进入代码、示例、截图和 Git。

执行结果及命令见 [Backend-Validation.md](Backend-Validation.md)。兼容、回滚及平台评审状态见 [ADR-0006](ADR-0006-Backend-Client-Session.md)。后续正式业务规则、短信供应商和微信发布另行确认。
