# 总体架构

日期：2026-09-30。架构基线：0.1.0。

## 目标与当前范围

题材采用原创虫修/蛊道修炼、角色成长、探索与剧情扩展的结构；现有参考资料仅用于内部研究，不把原作人物、地名、剧情、录屏或美术资源作为发布资产。正式内容授权与原创性由项目负责人另行审核。

本阶段只做底座：稳定启动、模块边界、资源/代码/数据工具链、网络承载抽象、世界/战斗会话边界和验证。角色、装备、蛊虫、自动探索、剧情、任务和奖励的具体玩法留给业务组。

## 依赖图

```text
Bootstrap (AOT)
  -> GF Base / FSM / 网络管理器
  -> YooAsset Core 包 -> Luban 二进制数据
  -> HybridCLR 入口 -> Gameplay.HotUpdate
                            -> 显式模块注册 / 依赖排序 / 逆序释放
                            -> Config / Platform / Lobby / Player
                            -> World / Battle / Content / Presentation
Gameplay -> AOT services -> Contracts / Foundation / Network / Simulation
Gameplay -> generated Config -> Luban.Runtime
Rendering -> Contracts / URP
Server -> 同版本 Contracts / Simulation / Network / GF DLL / Luban 表
```

AOT 不能引用 `Gameplay.HotUpdate` 或生成表类型；启动层通过反射入口返回 `IHotUpdateApplication`。公共 DTO、平台服务、生命周期接口留在 AOT。稳定确定性内核本阶段也留在 AOT，模拟规则升级需要发行基线变更，不能将修改底层 DLL 当成普通内容补丁。

## 启动链

GF FSM → 配置校验 → 平台初始化（微信目标等待 SDK Ready）→ YooAsset 初始化 → 发现/锁定 Core 版本及 manifest → 下载缺失文件 → 读取 Luban 表 → 母包/目标/SHA 检查 → AOT metadata 与热更代码入口 → 模块依赖排序/启动 → Ready。

任一阶段失败都会停在 Failed、释放本次启动资源，不以本地模式掩盖错误。代码注入不可卸载；部分注入失败必须重启进程。编辑器采用已编译热更程序集，明确不声称验证了原生解释器。

模块按依赖拓扑顺序启动，逆序停止；失败模块本身也进入清理；清理一个模块失败仍会继续清理其他模块。注册阶段结束后服务表冻结，不允许业务组运行中替换核心服务。

## 网络与服务部署

```text
微信平台登录码
  -> HTTPS Identity 服务（换取平台身份，签发短期业务会话）
  -> 接入 Gateway（微信已验证的 TCP 适配或 WSS -> 内部 TCP）
  -> Lobby TCP 服务
       -> Player / Inventory / Gu / Quest / Reward
       -> World Router -> World Instance / AOI
       -> Match / Room -> 临时票据 -> KCP Battle 服务
```

当前 `server/InsectSpace.Server` 仅运行 loopback TCP/KCP 诊断握手和读表，**不是生产登录服务**。共享 `FoundationPacketCodec` 是诊断协议，不作为今后全业务的万能字符串协议。生产协议使用独立 schema，保留版本、请求序号、幂等键、长度上限、心跳、错误码及兼容策略。

未配置的平台传输工厂默认 fail-closed。桌面 socket 适配和微信平台桥不是同一个实现。微信适配组通过 AOT 的 `IClientPlatformAdapter` 在 Bootstrap 前向 `PlatformServices.Install` 注册网络工厂、地址解析和资源文件系统；启动后不可替换。专用微信适配器已经接入 SDK 初始化、TCP、UDP/KCP 和 Web 资源参数，启动先等待 SDK；当前仅支持服务端下发的 IPv4 端点。小游戏不走桌面 `System.Net.Sockets`，SDK 传输/缓存仍待真机验证，详见 [微信平台适配](WeChat-Platform.md)。

GF KCP 继续复用已有实现，不重新实现 KCP 算法。传输驱动和逻辑帧驱动分别调度；20 Hz 战斗帧不是 KCP 协议 timer。所有业务回调在宿主线程分发。生产部署还需要限流、背压、连接数上限、重连、取消和可观测性。

框架已经加入 `SessionConnections` 作为大厅/战斗的统一连接控制器，并接入 `BootContext` 和模块服务表。它提供连接代次、异步 DNS 隔离、登录/房间确认屏障、票据与版本校验、超时、取消、返回世界及恢复状态；实际账号/帧协议仍由业务协议实现注入。操作与所有权规则见 [连接生命周期](Session-Lifecycle.md)。

## 通服与好友同图

从第一天分离三个身份：

- `HomeRealmId`：角色归属、经济与长期持久化边界，不随临时地图切换。
- `WorldClusterId + InstanceId + SceneId + Epoch`：当前大世界路由与分线；Epoch 防止跨线后接受旧连接数据。
- `RoomId`：独立战斗实例，结束后回到原世界会话。

首期建议一个逻辑世界集群，保留角色归属区概念，地图按容量分线。分区和通服是服务端路由策略，不做硬编码服务器列表。好友进入通过 `JoinFriend(friendId)` 请求路由器检查地域、版本、容量、权限、组队关系后返回目标；客户端无权指定任意实例。

真正跨服包括统一玩家 ID、身份/社交目录、路由器、实例预约、单写租约、迁移与超时回滚、跨区经济一致性、幂等结算。这些是后续服务端工作，当前 `WorldRoute` 与会话测试只是协议基础，不能宣称已经实现通服集群。

## 世界同步与战斗同步

大世界采用服务器权威的 AOI 状态复制：进入/离开可见集、角色外观、位置与朝向、世界 tick；客户端插值显示和输入上报。不是把所有在线玩家塞进一个帧同步房间。

`WorldPresenceStore` 对路由 Epoch、实例 ID、递增 tick 做检查，已注册到模块服务表。`ApplyForSession` 先校验当前会话，支持权威路由和首批快照在同次回调到达；重复绑定同一路由不会清掉新快照。当前支持全量 interest-set 快照，增量复制后续另行版本化。`WorldActorPresenter` 已提供按 ID 创建/回收、外观替换和位置/朝向插值；`WorldMovementEmitter` 只发送版本化移动意图。对象池、LOD、导航、预测、具体输入 UI 和真实 AOI 服务仍留给相关组。见 [World-Client.md](World-Client.md)。

遇敌后进入独立战斗会话：

1. 本地战斗：`BeginLocalEncounter`，本地帧输入源驱动同一个确定性世界。
2. 在线战斗：收到服务端票据及 config/map/simulation 版本，`BeginOnlineEncounter`，进入 ConnectingBattle；只有连接及校验完成才 `BattleConnected`。
3. 在线输入经 KCP 交付给有界 `OrderedFrameInbox`，缺帧必须等待，不能生成空帧替代。
4. 两者共用 `BattleSession`、GF `FrameSimulationWorld`、FP64、命令排序、状态 hash、回放接口。Unity 只承担表现，不参与权威物理。
5. 返回世界不清掉角色归属和世界路由。大厅链路应保持；断线进入 Recovering，由服务器重新下发快照与新 Epoch，禁止盲目继续旧帧。

当前本地验证只执行空场景 20 帧，尚不包含敌人、技能或掉落。测试使用有状态系统验证不同到达顺序的确定性。

## 留存内容边界

World 消费场景/区域配置；Content 消费剧情节点、任务条件及展示用奖励描述；Player 提供角色/装备/蛊虫只读模型；Battle 只产出可验证的战斗记录。离线收益、任务完成和掉落入账由服务器判定，客户端展示值不能成为奖励事实。

版本扩展采用稳定 ID、区域包/章节包、任务状态机、配置校验、存档迁移。先保留表与模块接口，不在本次提交虚构完整剧情、经济数值或技能系统。
