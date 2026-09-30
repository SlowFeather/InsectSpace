# 微信发布门禁

**状态：未获准发布。** 已验证编辑器、共享内核、本机 TCP/KCP、Windows IL2CPP 运行/远程热更、原始 MiniGame WASM 的原生导出和浏览器启动，以及官方微信 SDK 原生转换。AppID/CDN 尚未配置，不声称微信开发者工具或真机运行已经通过。

## 必须核验的事项

| 门禁 | 负责人 | 要求的证据 |
| --- | --- | --- |
| 引擎与解释器 | 核心平台 | 团结 1.10.4 专用小游戏目标、HybridCLR 8.5.0 的原生适配/授权/发行组合确认 |
| 平台 SDK | 平台接入 | SDK 固定版本、小游戏基础库与 iOS/Android 支持矩阵 |
| 登录 | 服务端/平台 | 登录码后端换取身份、会话过期、重放防护；客户端不含 app secret |
| 大厅传输 | 网络 | 真机已验证的 SDK TCP，或 WSS gateway → 内部 TCP；域名、TLS 与超时/重连测试 |
| 战斗传输 | 网络/战斗 | SDK UDP 与 GF KCP 接口适配、票据认证、NAT/弱网/丢包测试，不把 UDP 能力视为默认 |
| 前后台恢复 | 平台/世界 | 挂起停止模拟推进，恢复后重鉴权、世界快照/战斗帧恢复 |
| 内容热更 | 平台 | 签名与版本组合验证、目标匹配 metadata、失败恢复、冷缓存首进 |
| 资源缓存 | 平台/渲染 | 包体/下载/CDN/缓存配额、按需分包、内存压力、缓存清理 |
| 通服 | 服务端 | 跨归属区好友同图、迁移租约、旧 Epoch 数据拒绝、双写与重复结算测试 |
| 画质 | 渲染/QA | 低中高档性能基准、内存峰值、发热、可见人数与技能同屏压力 |
| 安全与内容 | 项目负责人 | 网络暴露面、审查/平台规范、原创或授权素材确认 |

这些是验收清单，不是对平台当前支持范围的未经验证断言。平台组应以选定 SDK 与目标版本的官方说明及真机结果为准。

## 代码中的防误用

- `BootConfiguration` 的 Player 校验不允许 `editorSimulate` / `localSmokeMode`。
- `UnconfiguredMiniGameChannelFactory` 明确抛出未适配异常，不偷偷回退到桌面 socket。
- `IClientPlatformAdapter` 是 AOT 平台接入接口，必须在 Bootstrap 之前注册网络、解析器和资源文件系统。微信专用构建使用已接入的 SDK 初始化/TCP/UDP-KCP/Web 资源适配；它目前只接受 IPv4 字面量，编译与本机 KCP 桥回归不等于真机验收。
- `ReleaseGate` 默认阻止缺少 code manifest / 平台审核记录的 Player 构建。
- `ProjectSettings/InsectSpacePlatformApproval.json` **未预造**。后续由平台组提交带目标、SDK/工具链版本、对应验收报告路径和评审记录的审核文件。

该文件存在性检查只是工程防误操作，不是安全认证；最终发布权限、审批、签名和审计由 CI/仓库管理实施。

`Invoke-Unity -Action MiniGame` 使用实际团结 MiniGame 编译目标生成托管程序集，用于尽早发现程序集/API 问题，不生成可上线小游戏。`Native` 的受限 Development 入口仅适用于 Windows64 验证，不能被用于放行微信发布。

`MiniGameNative` 是另一个受限 Development 入口，输出原始 MiniGame WASM，关闭 slim metadata，
并验证目标匹配的代码和资源包。浏览器已实际启动该 WASM；详细边界见 [MiniGame 原生验证](MiniGame-Validation.md)。
它不放宽普通发布门禁，也不证明微信平台传输和缓存能力。

## 后续核验入口

- HybridCLR 项目文档：`https://www.hybridclr.cn/`
- 团结引擎手册：`https://docs.unity.cn/cn/tuanjiemanual/Manual/`
- 微信小游戏 API：`https://developers.weixin.qq.com/minigame/dev/api/`
- YooAsset 官方工程：`https://github.com/tuyoogame/YooAsset`
- Luban 官方工程：`https://github.com/focus-creative-games/luban`

已查阅 HybridCLR 8.5.0 官方平台/引擎支持说明，其包含团结引擎适配要求：
`https://www.hybridclr.cn/docs/8.5.0/basic/supportedplatformanduniyversion`。
该通用说明不能替代本项目的 Tuanjie 1.10.4 原生构建、小游戏 SDK 和真机验证，平台门禁仍未签署。
