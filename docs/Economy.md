# 元石、仙元石与战斗储备

日期：2026-10-02。范围：Unity 6 本地客户端 + 独立 .NET 服务端的数据结构、真实 TCP 钱包通讯、KCP 资源帧重放及权威结算。当前是单玩家资源房间；数据库、Redis、真实支付、生产账号鉴权和完整多人战斗不在本次范围。

## 规则

| 数据 | 产生/使用方式 | 权威位置 |
| --- | --- | --- |
| 元石 `YuanShi` | 活动/打怪有机会获取；由奖励服务判定资格、掉落概率及数量后调用发奖入口 | 服务端钱包 |
| 仙元石 `XianYuanShi` | 只由可信支付服务确认的充值回执入账；活动/怪物入口不能发仙元石 | 服务端钱包 |
| 战斗储备 `Reserve` | 玩家选择部分元石/仙元石，准备时从可用钱包转入托管；未开战可取消 | 服务端托管与权威战斗状态 |
| 空窍仙元 `ImmortalEssence` | 普通帧同步技能消耗的资源，独立于仙元石 | 整数确定性战斗状态 |

普通帧同步施法不读钱包、不自动扣石头。本次将“带入、没花完带出”实现为：**显式使用储备石头恢复仙元**，才会消耗对应的储备；仙元已满或储备不足不扣石头。若后续要改成其他道具/技能用途，可替换动作与服务器规则，不改钱包/托管边界。所有恢复比例、技能成本和初始余额均为明确标记的本地联调参数，未确定正式经济数值。

战斗可以不携带石头。服务器仍创建独立储备结算 ID，并绑定服务器分配的房间。战斗结束按服务器已执行帧的剩余量退款，客户端不能提交“剩余数量”或请求任意结算。已消费部分不返还，断线不自动退款，重复结算不重复入账。普通战斗消耗后的仙元保留当前值，不因战斗结束补满。

TCP 大世界动作使用服务端价格，确认后立即扣除可用钱包。储备不是可用余额，大世界消费不会再次扣托管金额；在独立战斗中拒绝世界消费。`WorldEpoch` 来自当前服务器路由，不能使用旧路由请求扣款。房间、世界实例和归属区独立。当前世界动作是成本通讯样例，不包含完整技能、怪物掉落或 AOI 服务。

## 数据结构与代码

- `HotUpdate/Modules/Player/Economy/Protocol/EconomyContracts.cs`：不可变金额、请求/响应、钱包/储备/仙元快照及错误码。数量用非负 `long`，不使用浮点货币。
- `HotUpdate/Modules/Player/Economy/Protocol/EconomyPacket.cs`：GF TCP 上的独立二进制 codec，不复用诊断字符串协议。
- `HotUpdate/Modules/Player/Economy/Protocol/BattleResourcePacket.cs`：TCP 房间准入及 KCP 输入、校验点、资源帧和结算通知的独立协议。
- `HotUpdate/Modules/Player/Economy/EconomyClient.cs`：身份校验、请求关联、单个未完成请求、版本校验、超时与原操作号重试；只显示服务器余额，不预扣。
- `HotUpdate/Modules/Player/Economy/LocalEconomyTcpClient.cs`：明确的 Editor-only loopback 适配器；GF tick 由宿主驱动。平台在线适配器以后使用同一读模型，不能将该本地适配器作为认证失败后的回退。
- `HotUpdate/Modules/Player/Economy/LocalEconomyBattleClient.cs`：TCP 准入后连接真实 KCP，提交动作意图、等待权威帧；重连从服务器校验点恢复，完成结算后自动刷新 TCP 钱包。
- `HotUpdate/Modules/Battle/Economy/BattleResources.cs`：整数帧资源状态，按 ActorId/Sequence 排序、严格连续帧、整批非法命令原子拒绝、稳定 hash；不依赖 Unity、时间或随机数。
- `HotUpdate/Modules/Battle/Economy/BattleResourceReplica.cs`：最多缓存 256 个帧，缺帧等待、重复帧去重，重放后核对状态与 hash，再提交客户端状态。
- `server/InsectSpace.Server/Economy/EconomyService.cs`：内存存储、奖励/充值可信入口、托管、世界扣款、开战/帧执行/结算。
- `LocalEconomyHost`、`LocalEconomyBattleHost`、`LocalEconomyConsole`、`LocalEconomyPanel`：TCP 服务、KCP 房间调度、可信服务端操作示例和 Unity 联调界面。

客户端目录相对于 `client/unity/InsectSpaceClient/Assets/InsectSpace/`。服务端用 Compile Link 编译相同协议和纯战斗源码，不依赖 Unity 或热更 DLL。没有修改 `shared/`、`vendor/`、平台 Runtime/Editor 或 asmdef；业务协议正式提取与发布要求见 [ADR-0002](ADR-0002-Economy-Protocol.md)。

## TCP 钱包协议 v1（2001）

GF TCP 提供外层长度分帧。内部小端编码，头为 `Int32 Version=1`、`Int32 PacketId=2001`、`Byte Kind`（1 请求、2 响应）。最大消息 1024 字节；字符串为单字节长度 + UTF-8，最多 96 字节，仅允许 ASCII 字母、数字、`-_.`；版本/类型/字段/边界/尾部多余字节不匹配均拒绝。

请求字段依次为 `Command:byte`、`RequestId:int64`、`OperationId:text`、`ExpectedRevision:int64`、`Amount.YuanShi:int64`、`Amount.XianYuanShi:int64`、`TargetId:text`、`WorldEpoch:int64`。

| Command | 值 | 含义 |
| --- | --- | --- |
| Snapshot | 1 | 获取当前权威快照；不产生经济修改 |
| PrepareReserve | 2 | 指定两种石头的数量，原子转入托管 |
| CancelReserve | 3 | TargetId 为当前储备 ID，只能取消 Prepared 状态 |
| WorldAction | 4 | TargetId 为服务端动作 ID，Amount 必须为零，服务器查价扣款 |

响应字段为请求号、操作号、结果码、是否重复操作，以及完整 `EconomySnapshot`。快照包含 PlayerId、HomeRealmId、Revision、Wallet、Reserve、ReserveId、Phase、RoomId、ImmortalEssence、EssenceCapacity。请求不携带玩家身份、房间路由、充值金额声明或结算余量；身份由服务器连接上下文提供。本地端口固定绑定一个演示角色，绝不当成生产鉴权。

结果码见 `EconomyResult`：余额不足、旧版本、储备占用、状态错误、旧世界路由、幂等冲突、容量上限等。首次修改请求缓存成功或失败结果；重复同一内容返回缓存结果与**当前**快照，同操作号换参数拒绝。发生版本冲突须先刷新，确认后使用新操作号提交；网络超时结果未知时保留原操作号重试。

充值订单在整个内存存储中唯一；活动/怪物事件按来源 + 事件 ID 唯一。一个事务锁内校验及修改两种余额，计入托管总额后检查溢出，防止先奖励后退款溢出。内存幂等记录上限 4096，达到后明确拒绝，不丢弃旧记录来重新允许重复消费。

## TCP/KCP 资源房间协议 v1（2002）

沿用相同 1024 字节上限、小端版本头与 96 字节 ID 约束，独立 PacketId=2002。TCP 钱包端口只接受该协议的准入请求；资源房间使用单独 KCP 端口。

| Kind | 承载与方向 | 内容与约束 |
| --- | --- | --- |
| AdmissionRequest / Admission | TCP，客户端请求 / 服务端响应 | 请求只有请求号与储备 ID；服务端绑定本地角色、分配 RoomId、KCP 端口和运行期房间凭据，返回规则与资源状态 |
| Join / Checkpoint | KCP，客户端加入 / 服务端确认 | 校验房间凭据；服务器确认角色、帧号、输入序号、仙元、余量、规则及 hash，客户端恢复可验证状态 |
| Input | KCP，客户端 → 服务端 | RoomId、递增 Sequence、ResourceAction；不能上报价格、奖励、退款数额或权威帧号 |
| Frame | KCP，服务端 → 客户端 | 20 Hz 资源帧、已采纳动作及权威状态/hash；客户端按顺序重放，重复输入不再次消费 |
| Finished | KCP，服务端 → 客户端 | 撤离意图经权威帧执行，服务器按剩余储备结算后通知；客户端随后通过 TCP 获取余额 |
| Rejected | KCP，服务端 → 客户端 | 拒绝错误房间、顺序、动作冲突或容量超限；客户端停止推进，重连核对服务器状态 |

`CastSkill` 只扣仙元；`UseYuanShiReserve` / `UseXianYuanShiReserve` 显式使用储备；`LeaveBattle` 只提交撤离意图。本地规则为技能 30 仙元、每颗元石恢复最多 10 仙元、每颗仙元石恢复最多 50 仙元。服务器每帧最多采纳一个输入，空帧由服务器产生。宿主网络调度用经过时间触发帧，纯资源模拟只接收整数帧号与命令。

断线后不生成客户端空帧，也不自动退款。重新连接 TCP 并点击“进入/恢复战斗”，服务器复用未结算房间，替换旧 KCP 连接并下发新校验点。房间凭据仅存在内存及准入通讯中，不写日志或客户端配置；房间结束后不可用于重新加入，当前未实现独立的定时过期/轮换策略，不能当作生产登录票据。

房间/准入历史及输入历史默认各有 4096 的本地上限；达到上限后停止接受新房间或普通输入，仍允许现有房间重连与一次终结撤离，避免储备被容量门禁卡住。不清除旧记录以重新允许重复消费。此实现不包含多人输入汇总、战斗胜负、敌人、伤害或真实 AOI 广播。

## 本地操作

在仓库根目录的 PowerShell 7 执行：

```powershell
# 终端一：TCP 7779 钱包 + KCP 7780 资源战斗，保留原 7777/7778 诊断服务
./tools/Run-LocalServer.ps1 -LocalEconomy -EconomyConsole

# 终端二：Unity 6 已打开；先停止当前 Play 并保存场景
./tools/Start-EconomyDemo.ps1
```

启动器用 UnityCLI 在 Editor 内创建/打开 `Assets/InsectSpace/Demos/LocalEconomy.unity`，再进入 Play。`-OpenOnly` 只打开，`-Port` 可指定经济 TCP 端口（需与服务端 `-EconomyPort` 一致）；服务端 `-EconomyBattlePort` 指定 KCP 端口并在准入时下发，客户端不自行指定房间地址。

服务器默认演示角色为 1、归属 `local-home`、世界 `local-world`/Epoch 1。初始 100 元石来自活动种子回执，10 仙元石来自**模拟充值回执**；仙元 100/100。数据只活在当前服务器进程中，重启丢失；不接入 MySQL/Redis，不保存身份票据。

客户端可刷新、转入/取消储备、进入/恢复战斗、施法、显式使用两种储备、撤离并等待结算、发起两种世界消费、断线重连及重试未确认操作。界面不提供“发奖励/充值成功/指定退款金额”网络按钮。服务端控制台保留以下明确的本地辅助操作：

| 命令 | 本地演示行为 |
| --- | --- |
| `activity` / `monster` | 确认一个新奖励事件，分别入账 10 / 5 元石；不是正式掉落概率 |
| `recharge` | **模拟**已验证订单，入账 5 仙元石；无真实支付 |
| `battle-start`、`cast`、`use-yuan`、`use-xian`、`battle-end` | 仅供控制台内部资源服务演示；已有 KCP 房间时拒绝这些命令，避免绕过有序网络输入。正常联调用客户端战斗按钮 |
| `status` / `help` | 显示本地状态 / 命令 |

控制台发奖/模拟充值后，客户端点击“刷新服务器快照”。钱包使用 TCP 请求/响应，战斗使用 KCP 权威帧流；钱包生产推送订阅尚未实现。资源战斗的网络闭环不依赖控制台推进。

已实测客户端按钮及自动化序列：初始钱包 (100,10) → 托管 (20,4)、钱包 (80,6) → KCP cast 只令仙元 100→70 → 使用一颗元石变为 80 仙元 → 断线重连恢复原房间 → 使用一颗仙元石，储备 (19,3)、仙元回到 100 → 撤离后结算钱包 (99,9) → TCP 世界技能再扣 3 元石，钱包 (96,9)。

## 验证与下一阶段

```powershell
./tools/Test-Foundation.ps1
./tools/Test-Architecture.ps1
./tools/Test-ClientDemo.ps1 -Mode All -Filter 'InsectSpace.'
# 独立服务器运行、LocalEconomy 场景处于 Play 后执行真实双进程验收
./tools/Test-EconomyDemo.ps1
```

`Test-EconomyDemo.ps1` 只通过 UnityCLI 调用客户端意图，不直接改服务器余额。它要求无活动储备、至少 20 元石/4 仙元石及 100 仙元，按起始余额断言结果；十个阶段保存至 `.artifacts/validation/economy/kcp-live-*.json`。要复现上文 (100,10) 的确切余额，先停止并重新启动本地内存服务器，再连接客户端；不要把重启当作生产恢复方案。

实际结果、失败修复及逐项核验见 [Validation](Validation.md)。正式接入还需要可信账号/连接绑定、支付回执验签、持久化事务与长期幂等账本、正式掉落概率、多人战斗生命周期、钱包推送及微信真机验证。本次本地通过不代表这些已完成。
