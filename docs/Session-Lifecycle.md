# 大厅与战斗连接生命周期

本文件描述框架层，不定义实际账号、任务、装备或技能协议。框架使用 GF 的 TCP/KCP 实现，不自建 KCP 协议栈。

## 单一宿主

`BootContext.Connections` 是启动层持有的 `SessionConnections`，也注册到冻结的 `ServiceRegistry`。GF Base 负责底层网络驱动，Bootstrap 每帧调用连接控制器的 `Tick`，业务模块不能再重复驱动 `GameFrameworkEntry.Update`。

控制器持有大厅和战斗两类独立 channel。大世界位置/AOI 消息可以通过大厅接入层传递，但不是战斗 lockstep 帧。两个网络连接的生命周期与 `HomeRealmId`、世界实例 ID、战斗房间 ID 分离。

所有公共操作、事件处理和 Tick 都在创建控制器的宿主线程执行；后台 DNS 只投递结果。连接取消、超时或替换后，旧 DNS 结果不会触发新连接。每个连接使用进程内单调递增的 `ConnectionId`，跨启停也不会重用旧 ID。

## 大厅

1. 协议组提供大厅 `INetworkPacketCodec`，调用 `ConnectLobby(endpoint, codec)`。
2. 收到 `TransportConnected` 仅表示 TCP/WSS 连接成立，仍处于 SignedOut。
3. 协议/身份层用 `Send(connectionId, packet)` 发送平台登录凭证；收到并验证服务器响应后，调用 `ConfirmLogin(connectionId, playerId)`。
4. 服务器批准世界路由后，通过 `ConfirmWorldRoute(connectionId, route)` 进入世界。

登录、初始入场都受超时约束，不允许用旧连接的响应确认新会话。桌面 DNS 支持多个地址；一个地址明确连接失败后会尝试下一个地址，并保留同一逻辑 ConnectionId 和总超时预算。当前尚未实现并发 Happy Eyeballs 或按候选地址分配的黑洞超时。

`FoundationPacketCodec` 只用于诊断测试，不是生产登录协议。控制器不理解字符串 `"login"`，也不会自行把 socket 连通视为已登录。

## 本地战斗

业务遇敌事件调用 `BeginLocalEncounter`，Battle 模块选用本地 `IFrameInputSource` 驱动共享 `BattleSession`。结束时调用 `CompleteLocalBattle`。本地模式不创建 KCP channel，不生成伪造网络玩家，也不能借本地 smoke 身份发起在线房间。

当前启动 smoke 已通过此控制器执行 20 个空模拟帧，再返回世界。敌人、技能和掉落仍由业务组实现。

## 在线战斗

1. 大厅服务下发 `BattleTicket`；客户端用实际已加载的 simulation/config/map 三元组构造 `BattleCompatibility`。
2. `BeginOnlineEncounter(ticket, compatibility, codec)` 先检查登录态、版本组合、票据过期和平台能力，然后自动创建/连接独立 KCP channel。
3. 票据与 endpoint 在发起时复制，调用后修改原 DTO 不会改变连接目标或认证令牌。
4. KCP 握手成立后仍停在 ConnectingBattle；业务协议必须等到房间准入和初始帧/快照确认后调用 `ConfirmBattleReady(connectionId, roomId)`。
5. 确认后的网络帧才进入 `OrderedFrameInbox`，缺帧等待。控制器只管理会话，帧编解码/确认格式属于战斗协议。
6. 服务端确认战斗结束/返回后，调用 `CompleteBattleReturn(connectionId)`，只销毁战斗连接，保留大厅和世界路由。奖励不在该方法里结算。

可用 `CancelBattleJoin` 取消尚未准入的房间；旧房间后续确认会被拒绝。不能用这个方法取消已开始的权威战斗。版本不匹配会在修改会话前失败。

## 断线与恢复

- 大厅掉线：销毁本控制器的两个 channel，保留当前玩家与最后世界路由，进入 Recovering。
- 大厅重连：必须重新鉴权；恢复不能换成另一个玩家，换号需要显式 SignOut。之后服务器必须下发更高 Epoch 的路由。
- 已准入的战斗掉线：保留可用大厅，进入 Recovering；服务器可经大厅下发更高 Epoch 的世界路由。不会自动转为本地战斗。
- 连接战斗过程中超时/失败：退出待加入房间，回到原世界，保留已登录大厅。
- 应用挂起：非 smoke 模式调用 `Suspend`，关闭两个连接并等待重新鉴权/权威恢复，不在后台继续推进旧战斗。
- `Dispose` 只移除本控制器创建的 channel 和事件订阅，不销毁其他系统的 channel；在 GF 关闭前释放。

`PacketReceived` 中的 Packet 由 GF 池管理，回调内解码/复制成不可变 DTO 后再交给业务，禁止持有原 Packet 跨帧。

## 平台与后续工作

桌面使用 `DesktopChannelFactory` 和异步 DNS resolver。微信使用独立平台工厂/resolver 插槽，尚未安装 SDK 桥时明确拒绝连接，不走桌面 socket 回退。

生产协议组还需接入实际身份校验、请求关联、心跳策略、世界消息、房间首帧确认与续传。本文档及本地 TCP/KCP 夹具不代表这些服务已经完成。
