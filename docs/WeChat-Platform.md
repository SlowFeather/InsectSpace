# 微信平台适配

## 初始化与职责

平台代码位于 `Assets/InsectSpace/Runtime/Platform/WeChat`，使用独立 AOT 程序集。
微信验证构建显式启用 `BootConfiguration.useWeChatSdk`；默认编辑器 LOCAL SMOKE 不调用微信 API。
适配器在 Bootstrap 场景之前注册。Bootstrap 等待 `WX.InitSDK` 完成，超时或失败进入 Failed，
随后才初始化 YooAsset 和热更模块。SDK 的 Runtime API 有真实引用，不依赖未使用的程序集碰巧没有被裁剪。

平台适配组负责 SDK、登录码交换接口、网络桥和资源平台行为；业务组不需要修改 Bootstrap。
SDK 来源和版本选择见 `vendor/README.md`。托管编译不代表 SDK 的原生 JS 桥或真机 API 已通过。

## 网络边界

- 大厅使用 `WX.CreateTCPSocket`，复用 GF 的包编解码和四字节大端长度前缀。
- 战斗使用 `WX.CreateUDPSocket`，通过 `IClientDatagramSocket` 驱动原有 Kcp.dll 算法。
- `DatagramKcpTransportProvider` 兼容锁定的 GF alpha.7 请求、确认、ACK 和关闭握手。
  这是平台字节传输与握手适配，不是重新实现 KCP 算法。
- 协议 timer 使用 HostTick 和独立单调时钟；不把战斗 20 Hz 当成 KCP 的更新频率。
- SDK 回调由宿主线程处理，TCP 每次最多消费 64 个事件、积压最多 256 个事件/2 MiB；
  UDP 最多积压 256 个数据报，每报不超过 4096 字节，并校验远端 IP/端口。
- KCP 报文上限沿用 GF channel 的 `MaxPacketSize`，发送窗口积压有限制；房间票据不超过
  255 个 UTF-8 字节。建议使用后端签发的短期不透明票据，而非任意长度的 JWT。
- 重连前释放旧 socket/监听器；旧实例回调不影响新连接。失败不会切到桌面 socket 或本地战斗。

**当前仅支持服务端下发的 IPv4 字面量端点。** 这是明确的框架限制：GF 当前接口使用
`IPAddress`，微信不能调用桌面 DNS。域名、IPv6、WSS Gateway/TLS 必须由平台组设计并独立验收，
不能用伪造 IP 占位或偷偷调用 `System.Net.Sockets` 绕过。

原始 TCP 并不自动提供 TLS。不要直接把诊断连接变成生产身份传输；生产接入需要审核
网关/加密、域名与端口许可、鉴权、重放防护、限流和短期票据策略。

## 资源边界

微信适配器选择 YooAsset Web 文件系统并关闭 Unity 自身 Web Cache；资源版本、句柄和
内容包仍由 `YooResourceService` 管理。SDK 转换目录中的代码/首包与 StreamingAssets
部署不是同一件事，必须根据真实 CDN 配置部署资源，不能把空 CDN 的转换产物当成可用在线项目。
SDK 缓存命中、配额、淘汰、冷/热启动和中断恢复必须在微信环境单独验证。

## 已执行与未执行

- 已通过 SDK API 托管编译和 IPv4/无效地址/取消输入回归。
- 可插拔 UDP/KCP 层已经通过真实同版 GF 服务端回环、无效票据和握手/长度边界测试。
- 上述回环使用测试专用的本机 UDP 实现；**没有把它声称为微信 SDK 真机网络测试**。
- 实际微信 TCP/UDP 权限、弱网、NAT、切后台、重连、域名策略和 SDK 资源缓存尚待真机。
- 未实现微信账号后端、生产大厅协议或生产战斗业务。

真机联调需要项目自己的小游戏 AppID、资源部署地址和手机可访问的测试服。客户端和源码不存放
AppSecret 或长期凭据；当前没有创建或伪造平台审批文件。
