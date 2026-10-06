# 大世界客户端接入

本阶段提供复制状态、表现和输入边界，不包含自动探索规则、真实 AOI 服务或角色玩法。

## 服务与路由

Gameplay 启动时注册 `WorldPresenceStore` 及只读 `IWorldPresence`。World 模块跟随 `SessionCoordinator` 的权威路由；换线清空旧可见集，登出清理状态，同一路由重复绑定不清空刚接受的快照。

大厅协议组在 `SessionConnections.ConfirmWorldRoute` 完成权威路由确认后，通过 `WorldPresenceStore.ApplyForSession(session, instanceId, epoch, tick, snapshots)` 投递全量可见集。它先核对当前会话和路由，再绑定并应用，覆盖“路由和首批快照同一次回调到达”的情况。旧实例、旧 Epoch、旧 tick 和未处于世界/战斗会话的数据不会被接纳。底层 `Apply` 保留给已绑定的内部管线和测试。

`IWorldRouter.JoinFriendAsync` 是服务端路由请求接口，不授权客户端指定任意实例。服务端返回新 Epoch 后仍走同一条接入链。归属区、世界集群/实例和战斗房间保持独立。

## 表现

`WorldActorPresenter.Bind` 接受只读可见集、外观创建 factory 和回收 callback。World/Rendering 组先通过 YooAsset 加载预制体并持有 handle，再向 factory 提供对应外观；也可在 callback 接入对象池。

Presenter 根据 actor ID 创建/移除对象，外观变化时替换对象，用毫米位置及毫度朝向插值表现。`FindActor` 可提供跟随相机目标。它不执行权威碰撞、移动判定、伤害或掉落。创建出的对象由 Presenter 独占管理，不应由其他模块直接销毁；先 Unbind、销毁实例，再释放预制体资源。

Bootstrap 默认仍是明确标记的本地框架灰盒，不自动生成“在线玩家”冒充多人服务器。PlayMode 的可见集测试加载真实 WorldCommon 预制体，验证两角色显示、换线清理和旧快照拒绝。

## 输入

触屏、摇杆、鼠标点地面和自动探索均可将目标点交给 `WorldMovementEmitter.TryMove`。Emitter 编码为包含实例、Epoch、递增序号和毫米坐标的 `WorldMoveIntent`，通过注入的 `IWorldMovementSink` 发往世界服务，不直接改权威位置。

遇敌、断线、后台和路由迁移时由业务状态机 Suspend 输入；恢复必须绑定新权威 Epoch。镜头、导航表现和客户端预测由世界组扩展，不能替代服务端可达性、速度、碰撞和作弊检查。战斗输入另走帧同步接口，不复用世界移动协议。

## 本地竖屏移动样例

`tools/Start-WorldNavigationDemo.ps1` 通过 Unity CLI 打开 `LocalWorldNavigation.unity`。这个场景使用真实 3D 几何、斜俯视相机和运行时 NavMesh，提供 NPC 自动前往、点击地面、键盘和触控摇杆接管。自动到达目标后停止，不会循环传送或自动执行坊市、喂养、炼制操作。

镜头采用透视投影、50° 视野，相对角色偏移 `(13,16,-15)` 并看向角色上方 0.65 米，俯角约 38°，可看到建筑侧面。相机平滑跟随角色；这只是参考《剑与远征：启程》的斜俯视方向，场景仍为灰盒原型。

靠近三个 NPC 站点 1.8 米以内，手动点击交互按钮可分别打开坊市、蛊囊/喂养、炼蛊页；返回地图保持角色位置。面板显示期间暂停地图输入，自动到达只解锁交互，不提交经济操作。养炼仍使用独立本地 TCP 服务；空配置或连接失败会显示错误，不回退到本地赠送。`WorkshopPort` 默认为本机联调端口 7779。

该场景是明确标记的 LOCAL 表现样例：`LocalNavigationMotor` 只负责寻路和输入体验，生产接入时应由 `WorldMovementEmitter` 把目标转换为 `WorldMoveIntent`，交给服务器校验后由 `WorldPresenceStore` 快照驱动位置。场景不代表在线 AOI、服务器移动权威或微信真机触控验收。
