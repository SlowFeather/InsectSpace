# 验收记录

日期：2026-09-30。验证环境：Windows、本机 Tuanjie Editor **1.10.4 / 2022.3.62t16**、.NET SDK 10.0.302。

## 已执行并通过

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| Luban 4.5.0 真实导表 | 通过，2 张表，客户端/服务端 bytes 完全一致 | `tools/Build-Tables.ps1` |
| 核心包完整性/架构方向 | 通过，12 个程序集；SDK SHA、无环依赖、AOT 边界、模拟层检查 | `tools/Test-Architecture.ps1` |
| .NET 基础回归 | **43/43** | `.artifacts/validation/foundation-tests.json` |
| 真实 TCP loopback | 已建立 socket 并双向收发诊断包 | 上述网络测试 |
| 真实 KCP loopback | 已完成握手及双向收发；无效票据被拒绝 | 上述网络测试 |
| 服务端启动/读表/模拟入口 | 通过，1 个配置场景、20 个空确定性帧 | `tools/Run-LocalServer.ps1 -ValidateOnly` |
| 团结真实编译/场景初始化 | 通过 | `.artifacts/validation/unity-Prepare.log` |
| Unity EditMode | **48/48** | `.artifacts/validation/EditMode.xml` |
| Unity PlayMode | **7/7** | `.artifacts/validation/PlayMode.xml` |
| HybridCLR 目标 DLL 编译 | 通过，StandaloneWindows64，独立 Gameplay.HotUpdate DLL | `.artifacts/validation/unity-Artifacts.log` |
| YooAsset Core RawFile 包构建 | 通过，包含代码与真实表数据 | `.artifacts/yoo/validation-*/StandaloneWindows64/Core/foundation-001`；准确路径见 Artifacts 日志 |
| 项目本地 HybridCLR 原生工具链 | 通过，固定 Git 提交，不改全局编辑器 | `tools/Install-NativeToolchain.ps1`、`unity-Native.log` |
| Windows IL2CPP 母包 | 通过，注入 14 份 AOT 元数据后真实加载 Gameplay；会话 SignedOut | `.artifacts/validation/native-player.log` |
| 同母包远程代码更新 | 通过，发现新 Core，运行补丁版本标记；原生 DLL SHA 不变 | `native-patch-player.log`、`native-patch-result.json` |
| WorldCommon AssetBundle 包 | 通过，真实预制体实例化、附加场景加载/卸载、handle 释放 | 原生日志 `CONTENT_LIFECYCLE_PASSED` 及 PlayMode |
| 大世界表现/输入边界 | 通过，可见集驱动对象、换线销毁、拒绝旧快照、毫米移动意图 | `WorldPresentationTests`，不代表真实多人服务器 |
| 团结 MiniGame 托管编译 | 通过，实际专用目标生成 Gameplay DLL | `.artifacts/validation/unity-MiniGame.log`，不是原生导出 |
| 团结 MiniGame 原生导出 | 通过，WASM/data、14 份 AOT、Core 18 项、WorldCommon 3 bundles | `unity-MiniGameNative.log`、`minigame-export-result.json` |
| 原始 MiniGame WASM 浏览器启动 | 通过，真实注入 AOT/Gameplay，8 模块、2 表，灰盒可见 | `minigame-browser-console.log`、`output/playwright/minigame-foundation.png`；不是微信 SDK 运行 |
| 可插拔 UDP/KCP 桥 | 同版 GF 服务端真实回环、票据拒绝、准入和长度上限通过 | Foundation 三个 `network.sdk-datagram-kcp-*` 测试，非微信真机 |
| 微信 SDK API 适配 | 初始化/TCP/UDP/Web 资源参数已编译；地址边界测试通过 | `WeChatAdapterTests`；原生转换另行验证 |
| 微信 SDK 原生转换 | 通过，SDK 固定提交；两个子包及资源齐全，Brotli WASM 与原生输出 SHA 一致 | `unity-WeChatNative.log`、`wechat-export-result.json`；AppID/CDN 均未配置，不代表微信执行 |

PlayMode 覆盖真实 Bootstrap 场景加载及脚本/相机引用、YooAsset EditorSimulate 初始化、Luban 数据读取、热更程序集入口调用、8 个模块启动、本地空战斗帧、回到 World、停止后重新启动，以及三个绑定 URP 的画质档位切换。未通过忽略 Unity 错误日志来获得通过结果。

新增的连接测试通过真实 loopback TCP/KCP 夹具串起大厅确认、进入世界、房间准入、战斗返回，覆盖旧连接/DNS 响应、双栈地址回退、过期票据、配置版本不匹配、超时、取消、断线恢复、后台挂起、宿主线程及部分创建失败清理。确认登录和房间准入仍是测试夹具响应，不是生产账号服务。PlayMode 同时确认启动上下文拥有连接控制器，smoke 不伪装成 TCP 登录，退出后连接控制器被释放。

EditMode 验证 AOT 中间构建及独立开发 Player 的门禁，覆盖准备作用域、scripts-only、目标、路径和 Development 限定；也检查远端/fallback HTTPS、母包身份、资源发现策略和内容包路径输入。新入口没有制造平台审批文件。

PlayMode 新增真实 WorldCommon 预制体和场景的生命周期测试；世界表现测试通过 YooAsset 加载预制体后驱动可见集，覆盖两角色显示、好友目标实例式路由切换、旧对象回收、旧快照拒绝、同路由幂等绑定和移动输入暂停。测试使用本地权威会话夹具，未声称实现真实好友服务或 AOI 服务。

原生日志确认 `NATIVE_CODE_LOADED aot=14`、8 个模块、2 张表和 SignedOut 会话。补丁构建不重建 Player，验证前后记录 `GameAssembly.dll` 校验和，HTTP 请求及选择的版本写入 JSON。Native 验证使用无图形模式，Null GPU 的 shader 不支持日志不纳入画质结论；未以此声称通过渲染验收。

Unity 批量验证在 `.artifacts/unity/ValidationClient` 隔离副本执行；保留了当前打开的原工程和编辑器会话。副本只排除机器本地的可选编辑器 AI 插件，其余游戏代码、SDK、URP 和工程设置来自当前工程。原工程已经放入启动场景、资源 collector、HybridCLR 设置和程序集元数据；使用 README 的菜单步骤运行。

接入微信平台程序集后，再次执行 `MiniGameNative` 原生导出回归并通过：14 份 AOT、18 项 Core payload、3 个 WorldCommon bundle；随后重新校验独立保留的微信转换产物也通过。原始 MiniGame 与微信 SDK 导出是两个独立目标配置。这次最终回归验证构建与文件完整性，没有重新执行浏览器启动，也没有执行微信开发者工具或真机；此前的浏览器启动证据保留其原有范围。

## GitHub 首次导入前复验

2026-09-30，提交前重新执行 `tools/Test-Architecture.ps1`、`tools/Test-Foundation.ps1`，以及 `tools/Invoke-Unity.ps1 -Action EditMode` / `-Action PlayMode`：架构检查、43/43 基础回归、服务端启动、48/48 EditMode 和 7/7 PlayMode 全部通过。Unity 仍在隔离副本执行，未操作正在打开的原工程。

本次没有重新执行原生构建、浏览器启动或微信真机验证；这些项目保留上述既有证据及范围限制，不视为此次提交重新通过。`doc/参考/` 已加入 `.gitignore`，不纳入首次导入提交。

## 双引擎与 Unity 6.6 Web 验证（2026-10-01）

本节是双工程改造后的新证据；上文 2026-09-30 的原生/微信导出记录保留原有版本和范围，不视为本次重新验证。

环境：Windows，团结 1.10.4 / `2022.3.62t16`，Unity 6.6 / `6000.6.3f1`，Unity CLI `1.0.0-beta.11`，Pipeline `0.8.0-exp.1`，Microsoft Edge `154.0.4258.37`。

| 检查 | 实际结果 | 证据 |
| --- | --- | --- |
| 基础回归和本机 TCP/KCP | **43/43**，服务端读 1 场景、执行 20 空确定性帧 | `foundation-tests.json`，`Test-Foundation.ps1` |
| 双引擎架构与环境 | 通过 SDK 校验、12 程序集、版本/包隔离、共享链接、独立场景和错误目标拒绝 | `Test-Architecture.ps1`、`Test-ClientEnvironment.ps1` |
| 团结 EditMode / PlayMode | **62/62、7/7** | `.artifacts/validation/EditMode.xml`、`PlayMode.xml` |
| 团结 MiniGame 托管编译 | 通过，未据此声称原生/微信设备运行 | `.artifacts/validation/unity-MiniGame.log` |
| Unity 6.6 EditMode / PlayMode | **46/46、7/7** | `.artifacts/validation/unity6/EditMode.xml`、`PlayMode.xml` |
| Unity CLI 实时控制 | 160 个命令可发现；执行 eval、打开 Bootstrap、Play、读状态、Stop 成功；最后 ready、非编译、已停止 Play | `.artifacts/validation/unity6/cli-tools.json`、`cli-live-play.json`、`cli-editor-status.json` |
| Unity 6.6 WebGL Development Player | 构建成功，包含 Core 表与 WorldCommon 资源 | `.artifacts/validation/unity6/unity-Web.log`；输出 `.artifacts/unity6/WebDevelopment` |
| Web 浏览器实际运行 | WASM HTTP 200；`FOUNDATION_READY modules=8 tables=2`、`WEB_DEVELOPMENT_READY modules=8 tables=2`；无 console error、JavaScript 异常或启动失败 | `.artifacts/validation/unity6/web-browser-result.json`、`web-browser-console.log` |
| Web 画面检查 | 960×600 canvas 中可见灰盒地面、3 个标记和 WorldCommon 胶囊角色，状态 Ready，无开发控制台红色错误 | `.artifacts/validation/unity6/web-development.png` |

Unity 工程不含团结微信 SDK 及其条件测试，所以 EditMode 数量与团结不同。两边共享玩法、启动代码和表数据，但独立保存 Packages、ProjectSettings、引擎资产与 Library；团结验证副本会将 junction 展开成普通文件。

迁移中通过团结 AssetDatabase 导出原资产 GUID，再转换不可移植的 `.meta` 编码；没有随机生成原资产身份。Unity 侧的三个 `.scene` 经实时 CLI / AssetDatabase 移为 `.unity` 并核对 GUID 不变。PlayMode 实际验证 Bootstrap、WorldCommon 场景加载/卸载及 prefab 句柄生命周期。

浏览器验证最初暴露 GF 反射工厂、YooAsset Web 文件系统构造函数和 AssetBundle 中 CapsuleCollider 的裁剪问题。最终仅在隔离 Web 开发构建中生成类型保留清单并关闭引擎代码裁剪；Web 开发画质设置使用浏览器刷新节奏。重新构建后，严格 console error 检查和截图复验均通过。浏览器有音频需要用户手势、部分未使用后处理 shader 的非阻断提示；未据此宣称音频或完整特效验收。网络记录保留一次被取消的 `.data` 请求，最终引擎、表数据和内容确实加载完成，不将单次取消当作运行失败。

本次还定位到 URP 材质升级弹窗会让 CLI `status` 报 ready 而主线程命令超时；完成 Unity 独立资产升级后命令恢复。当前可用 `tools/Run-Web.ps1` 启动 loopback HTTP 预览，`-Build` 在隔离工程重新构建。开发服务器不对局域网/公网发布。

**验证边界：**当前 Web 是明确标识的 LOCAL WEB DEVELOPMENT，玩法编译进 Player，未运行 Unity 6 的 HybridCLR 原生注入或浏览器代码热更新，未实现浏览器在线传输。团结原生导出、微信开发者工具、真机 TCP/UDP/缓存及生产发布门禁未在本次重做。SDK 兼容包、共享元数据、平台启动/构建代码仍需平台负责人评审；未生成审批文件，也未代替审批合并或发布。

## 客户端功能 Demo 续做验证（2026-10-01～2026-10-02）

交付本地教学场景 `Assets/InsectSpace/Demos/ClientDemoHub.unity`，12 个中文可交互主题，配套源码入口、动手练习、操作记录、全部重置和资源释放入口。讲解手册见 [客户端功能 Demo](Client-Demos.md)。业务教学代码位于各组 `HotUpdate/Modules/<Owner>/Demo`，没有修改参考 GF 仓库或复制 SDK 源码到玩法目录。

2026-10-02 在本机 Unity **6000.6.3f1** / Unity CLI **1.0.0-beta.11** / Pipeline **0.8.0-exp.1** 重新执行最终回归。10 月 1 日的逐页截图和人工触发 TCP 诊断保留其原始范围；下表明确区分。

| 检查 | 实际结果 | 证据 |
| --- | --- | --- |
| Unity CLI 实时编译 | 通过，无编译错误 | `.artifacts/validation/client-demo/compile.json` |
| Unity EditMode（10-02） | **46/46** | `.artifacts/validation/client-demo/EditMode.json` |
| Unity PlayMode（10-02） | **16/16**，其中 Demo 专项 9 项 | `.artifacts/validation/client-demo/PlayMode.json` |
| Foundation / 服务端启动（10-02） | **43/43**，真实 TCP/KCP 回环，1 场景 / 20 空确定性帧 | `.artifacts/validation/client-demo/foundation.log`；`.artifacts/validation/foundation-tests.json` |
| 架构与双引擎环境（10-02） | SDK 校验、12 程序集、依赖方向、纯模拟与共享链接/引擎隔离通过 | `.artifacts/validation/client-demo/architecture.log`、`environment.log` |
| 12 页画面检查（10-01） | 实际 Game 视图 1440×900；主题、按钮、状态、预览与教学源码可读，长页可滚动 | `.artifacts/validation/client-demo/pages/`、`contact-sheet.png` |
| TCP 诊断成功与停服失败（10-01） | 真正收发 foundation.hello/ready；停服后失败，前后均 SignedOut，Bootstrap 保持 Ready | `.artifacts/validation/client-demo/tcp-success.json`、`tcp-failure.json` |
| 本地启动入口 | 打开正确教学场景，并等待 `CLIENT_DEMO_READY lessons=12`；未将“进入 Play”当作启动成功 | `.artifacts/validation/client-demo/launch.json`；`tools/Start-ClientDemo.ps1` |
| 最终首页复验（10-02） | Ready、12 课、1440×900，停留首页可直接演示；当前 Console 无错误 | `docs/images/client-demo-overview.png`；`.artifacts/validation/client-demo/console-final.json` |

Demo 专项验证包括：模块拓扑/失败回滚；角色 Revision 与输入输出复制；任务条件顺序及重复回执；移动等待快照与旧 Epoch 拒绝；整数战斗、有序收件箱缺帧等待及逐帧 hash 回放；资源加载/释放三轮；异步失败不计体验进度；带资源重置、主动结束和直接销毁后重启；复用已有传输的真实 TCP 成功/失败及连接清理。PlayMode 未屏蔽 Unity 错误日志以获得通过结果。

最终 Console 记录仍有 3 条字体引用清理警告；可读性已用实际页面截图核对，不将“无错误”描述为“无警告”。

续做中通过实测修正的问题：

- Foundation 的 Play 起始场景覆盖会使 Demo 或 Test Runner 进入普通 Bootstrap；启动脚本清除覆盖，测试脚本在每轮测试前清除并在结束后恢复原场景设置。
- 资源按钮的协程守卫曾提前 Dispose YooAsset 的可等待句柄；现在将 handle/operation 交还 Unity 等待，仅释放真正拥有的资源，并加入按钮实际调用路径的回归。
- Bootstrap 之后再创建 DesktopChannelFactory 会重复注册 KCP；本地诊断使用受限 loopback TCP 工厂复用已注册传输，不改变共享工厂或生产会话。
- 增加场景先卸载、Bootstrap 后释放的清理顺序，恢复进入 Demo 前的画质/FPS/VSync/相机状态。
- Game 视图沿用竖屏预设会使横向教学界面很小；启动脚本选择 Editor 本地 1440×900 预设，可用 `-KeepGameView` 保留原设置。
- Pipeline 在编译/Play 域重载期间可能暂时丢失 HTTP 响应；只读查询有界重试，测试启动响应丢失时先核对已保存请求，不重复启动同一轮测试。

**验证边界：**这是 Unity Editor 本地教学。角色、装备/蛊虫、任务、奖励回执、世界路由与战斗指令是明确标识的示例；真实框架调用不表示生产玩法/后台已完成。没有新增微信真机、原生 HybridCLR、线上身份、真实多人 AOI、CDN、性能或发布结论。既有平台改造仍遵循平台评审与 `WeChat-Release-Gates.md`，本次没有制造审批或发布证据。

## 验证中修正的问题

- 按 YooAsset 3.0.5 的实际 API 接入 `IRemoteService`、`RawFileObject`，编辑器使用 `VirtualRawBundle`。
- 区分团结批处理空场景与交互编辑场景，避免创建场景时影响未保存内容。
- 将 .NET 中间文件移到 `.artifacts/dotnet`，避免被 Unity 作为包资源导入。
- 补齐三个参考 SDK 包缺失的 `.meta`，生成独立 `insectspace.1` 包装版本；原始归档和核心 DLL 未变。
- 数据包编解码对错误版本、非法长度与无效 UTF-8 返回明确错误；不让格式错误穿透诊断服务。
- 大厅与战斗统一连接控制，明确区分传输连通、账号确认、房间准入和权威恢复；平台创建中途失败也回收已分配 channel。
- 修正 IPv6 首地址拒绝连接时不尝试后续 IPv4 地址的问题。
- 修正发布门禁与 AOT metadata 生成的前置依赖循环；限定项目内中间构建，不放宽普通 Player 发布检查。
- 增加项目内原生安装、独立 Development Player 门禁、AOT 源码指纹及同母包更新验证。
- 增加实际远端版本发现和母包标识校验；内容包版本与 Core 绑定，不分别追踪 latest。
- 单独重编译热更 DLL 会撤销旧代码清单，避免重用旧 payload SHA；完整 Player 流程必须重新 Stage。
- 常规资源收集器和灰盒产物已纳入原工程，不依赖验证副本残留；修正批处理未保存空场景的生成限制。
- 远程测试同时提供 Core 与其引用的 WorldCommon 清单，修正只提供 Core 导致内容包 hash 请求 404 的问题。
- 增加平台 SDK 注入点，避免平台组直接修改受保护的 Bootstrap；增加世界接入幂等边界与输入/表现组件。
- 关闭 MiniGame slim metadata，增加限定范围的原生导出；运行时目标比较支持引擎枚举别名，同时拒绝数字和错误平台。
- 原始 WASM 浏览器检查发现并修复 MiniGame 错用桌面资源文件系统的问题；新增离线/远程 Web 文件系统配置测试。
- 实测 SDK 0.1.32 原生缺失 JS 桥符号，改为完整且逐文件核验的官方 0.1.34 提交；没有补改 vendor DLL。
- 微信转换入口显式选择 WebGL 2，兼容既有线性色彩空间，并同时设置 SDK 要求的绝对/相对导出路径。
- 新增 SDK 初始化等待、TCP/UDP 桥、可插拔 GF KCP 字节传输与受限 IPv4 解析；KCP 桥以真实同版服务端回环验证。

## 尚未验证或实现

- **未验证微信开发者工具/真机运行、SDK 实际 TCP/UDP、WSS 网关、平台缓存和前后台恢复。适配代码和本机协议回归不是设备验收。**
- **Windows、原始 MiniGame 及官方微信 SDK 原生转换已验证；微信环境中的 HybridCLR 运行/远程更新仍未验证。全局编辑器未修改。**
- 未实现生产账号登录、真实玩家数据/装备/蛊虫、AOI 广播服务、好友进线或跨服集群。
- 未实现实际战斗技能、任务、剧情、奖励、持久化、完整生产美术或性能指标。
- 未完成签名启动 manifest、母包兼容矩阵、灰度与回滚的生产发布系统。
- 基础启动场景仍为灰盒；独立草地渲染样板见本文末节。无真机 GPU、内存、发热或弱网性能结论。
- CI 文件已经落地并配置 GitHub 远程；本次本地回归不代表 GitHub Actions 已执行或通过。尚未确认真实 CODEOWNERS 团队及受保护分支，不能声称服务端审批已经生效。

微信 app ID、选定平台 SDK、域名/账号后端和目标真机验收不能由 Windows 原生或 MiniGame 托管编译替代。普通 Player 发布门禁保留，不为了生成“成功”结果而关闭。

## Unity 6 草地渲染样板复验（2026-10-02）

本次针对 `Assets/InsectSpace/Scenes/MeadowShowcase.unity`，使用 Unity **6000.6.3f1**、Unity CLI **1.0.0-beta.11**、Pipeline **0.8.0-exp.1** 和实时 Editor **Direct3D11**。最终实现与启动命令见 [渲染样板](Rendering-Showcase.md)。

实际游戏画面参考来自 [GameTyrant 的 AFK Journey 评测](https://gametyrant.com/news/afk-journey-review-a-little-more-than-just-idle)及用户提供的战斗截图。Google Play 的宣传合成图未作为纯实机截图结论。`docs/参考/webwxgetvideo.mp4` 为 **0 字节**，无法读取，因此没有声称完成视频分析；版权截图未加入项目资产或仓库。

| 检查 | 实际结果 | 证据 |
| --- | --- | --- |
| Unity CLI 场景生成 | `RebuildMeadow.Run` 完成，固定种子 42017，221 份生成网格、176 个 Renderer | `AgentScripts/RebuildMeadow.cs`；`Showcase/Painterly/` |
| CC0 角色与真实动画 | KayKit Mage 与同 FBX 的 `2H_Melee_Idle`；Animator 开启，骨骼实际变化、角色根稳定、蒙皮 bounds 正常 | `Showcase/KayKitAdventurers/source.json`、`LICENSE.txt`；专项动画测试 |
| 三档草量 | **11,639 / 23,278 / 34,917** 根，36 块无空块，shaderErrors 为空 | `.artifacts/validation/meadow-showcase/quality-captures.json` |
| Play Mode 画面 | 1440×900，相机捕获并逐张查看；角色、草坪、小径、花、树、岩石和深色前景可见，六边形覆盖层可选 | `Assets/Screenshots/Meadow_Quality_0.png` ～ `Meadow_Quality_2.png`、`Meadow_Tactical.png` |
| Foundation 回归 | **43/43**，服务端 1 场景 / 20 个确定性空帧 | `tools/Test-Foundation.ps1`；`.artifacts/validation/foundation-tests.json` |
| Architecture 回归 | SDK hash、12 程序集、依赖方向、确定性纯净及双引擎版本/包隔离通过 | `tools/Test-Architecture.ps1` |
| Unity 6 实时 EditMode | **46/46** | `.artifacts/validation/meadow-showcase/All-EditMode.json` |
| Unity 6 实时 PlayMode | **19/19**，包含新增 3 项草地测试 | `.artifacts/validation/meadow-showcase/All-PlayMode.json` |
| 专项命令完整执行 | **3/3**，随后自动生成四张截图；停止 Play 并恢复画质 | `tools/Test-MeadowShowcase.ps1`；`MeadowPlayMode.json`、`quality-captures.json` |
| 一键启动 | 正确进入 MeadowShowcase 的 Play，画质 2、Animator 时间推进、角色根稳定、默认网格关闭 | `tools/Start-MeadowShowcase.ps1`；`.artifacts/validation/meadow-showcase/launcher.json` |
| Console / shader | 最新捕获区间无 error/warn 条目，ShaderUtil 无错误；早一轮出现过 3 条字体清理 warning，不描述为所有运行均零警告 | `console-before.json`、`console-capture-session.json` |

新增专项验证重新从磁盘载入保存场景，检查材质与网格持久化、一台相机、无覆盖构图的跟随组件、36 个草块和三档网格替换；动画测试实际采样骨骼与烘焙蒙皮，而不是仅检查 Animator.enabled。低/中/高草地三角形为 **34,917 / 69,834 / 104,751**；MeshFilter 三角形合计为 **70,909 / 105,826 / 140,743**，后者不包含 SkinnedMeshRenderer，不是完整场景 GPU 三角形预算。

历史失败保留：早期隔离副本的无图形 PlayMode 曾有 **5 个失败**，日志包含 `RenderTexture.Create failed`（`.artifacts/validation/unity6/PlayMode.xml`、`unity-PlayMode.log`）。该次没有通过，也不用于证明渲染成功。后续在实际 Direct3D11 Editor 重跑完整套件得到 19/19，并捕获画面，不将历史失败改写成成功。

早期 Kenney 方案出现异常蒙皮 bounds，当前换成 KayKit 同源骨骼/动画并保持 Animator 启用，未继续采用禁用 Animator 加 `ShowcaseMotion` 的替代方式，也不把未经充分确认的根位移解释当作根因。生成网格更新改为显式写入并上传顶点/索引数据，修复旧 GPU 缓冲和顶点色空间导致的道路/草坪显示异常。

**边界：**这是参考 AFK Journey 色彩和构图的本地渲染研究，使用 CC0 角色和原创程序化植被；没有复刻原作商业美术、UI 或完整玩法。此次没有新增平台 Runtime/Editor、共享契约、确定性模拟或在线身份修改；未进行微信真机、移动端 GPU/内存/发热、生产构建或上线验收，现有平台门禁继续适用。

## Unity 6 天空盒功能验收（2026-10-02）

实现与接入说明见 [风格化天空盒](Stylized-Sky.md)。Unity CLI 实时创建 SkyShowcase、原创无缝云图、三套天空预设和可复用控制器；原 MeadowShowcase 保留。天空支持日间 / 黄昏 / 月夜、平滑切换、独立双层云相位、画质变体及场景环境恢复。使用者提供的参考图只用于风格分析，未加入原作资源。

最终证据在 .artifacts/validation/stylized-sky/：SkyPlayMode.json **6/6**；All-EditMode.json **46/46**；All-PlayMode.json **25/25**。Foundation 重新执行 **43/43**，Architecture 通过。quality-captures.json 覆盖三预设 × 三档画质，九组 shaderMessages 为空；console-captures.json 最终区间无新增 warning/error。日间、黄昏、夜晚竖屏与纯天空横屏已视觉检查。一键启动 Start-SkyShowcase.ps1 已实测进入正确场景，900×1600，材质实例有效、云相位推进。

保留 Interrupted-All-PlayMode.json 中的历史失败：全量测试期间收到并发截图命令，内容测试因 No camera found 日志失败、世界测试启动超时；顺序重跑 25/25。更早的 Pipeline 请求超时与颜色精确相等断言也没有作为成功证据。未验证 Android/iOS/微信真机性能、温升、耗电或发布构建；ASTC 配置和本机渲染不代替这些验收。

## 五域两天策划与青茅山地图构图研究（2026-10-02）

本次交付位于 [世界地图设计](../design/world/README.md)：五域两天及九天残片分层、38项主要场景索引、古月山寨11组场景提示词、统一色板与资产拆图规范。小说设定与空间改编分开标注，以本地《蛊真人》文本的章节/行号为依据。正式地名采用青茅山与古月山寨；青茅村仅为工作称呼。

通过Unity CLI在当前Unity 6000.6.3f1项目新增独立 QingMaoLayoutStudy 场景与研究材质，未修改平台Runtime/Editor源码、shared或vendor。重新载入后验证五层家主阁、单台45°正交相机、596个Renderer、无缺失/错误材质、无Collider；相机截图1600×1000已查看。它是压缩核心街区的色块布局，不是完成美术、真实山地或可游玩地图。截图后关闭附加场景并恢复此前SkyShowcase。

本任务实际执行：Foundation **43/43**、服务端1场景/20确定性空帧；Architecture通过（生成地图资产后复跑）；复用草地专项PlayMode **3/3**。证据为 `.artifacts/validation/world-map-foundation.log`、`world-map-architecture.log`、`world-map-meadow-playmode.json`、`world-map-study.json`。场景脚本为 `AgentScripts/BuildQingMaoMapStudy.cs`。

共享Editor最初的完整测试请求/返回被同时进行的天空测试覆盖，未取得本任务独立完整EditMode结果；并发地图截图还干扰了天空全量测试，历史失败与其后顺序重跑见上节，不改写为首次通过。之后本任务在Editor空闲时独立执行专项测试与地图复查。详见 [Unity地图研究与验证](../design/world/Unity-MapStudy.md)。未生成AI图片，未验证导航、在线世界、微信真机、移动端性能或发布构建。

## 玩家剧情、服务器活动与成长经济策划（2026-10-02）

交付入口为 [玩家剧情与长期世界设计](../design/narrative/README.md)。正文包含九幕二十四章；第一幕青茅山细分五章、二十个主线节点。配套文档覆盖主要人物行程、个人／世界进度分离、三王九种小游戏、晚加入补课、元石与仙元石分工、一至九转成长及五升六的准备、机缘与保底提案。原著事实与游戏原创分开标注，并核对本地参考文本关键章节；未声称通读全文，未修改原参考文件。

本任务实际执行 `tools/Test-Foundation.ps1`：**43/43 通过**，服务端读入 1 个场景、执行 20 个空确定性帧；执行 `tools/Test-Architecture.ps1`：**通过**，包括 SDK 校验、12 个程序集、依赖方向、模拟层与双引擎清单检查。证据为 `.artifacts/validation/narrative/foundation.log`、`architecture-result.json`，后者如实记录本任务工具输出，并非额外重跑的原始日志。

文档检查 **8/8 通过**：参考文件 SHA-256 保持不变、幕章数量、青茅任务 ID 唯一性、三王房间数量、内部链接与编码、示例收支计算、升仙概率计算、Foundation 结果证据。检查记录与文件哈希见 `.artifacts/validation/narrative/design-review.json`。这些仅验证文本结构、算术与已有底座回归，不证明经济平衡、概率服务、任务闭环或活动已实际运行。

**本次未运行 Unity EditMode／PlayMode**：修改范围仅为剧情策划、README 导航和本条验证记录，没有 Unity 脚本、场景、资产或配置改动，因而无相关新增 Unity 测试。未接入 Luban 正式任务表、在线奖励、充值、存档迁移或任何发布流程。所有时间、数值、概率与活动阈值均为初测设计；原著具名内容继续沿用内部研究与发布内容审核边界。

## GitHub 双引擎、Demo 与渲染变更提交前复验（2026-10-02）

本次汇总当前双引擎、客户端教学 Demo、草地/天空/地图研究及策划文档变更，在独立分支提交供评审。两种引擎均在 `.artifacts` 隔离副本执行，没有修改参考 GF 仓库。新增本地证据位于 `.artifacts/validation/github-submission/`，该目录不纳入 Git。

| 检查 | 本次实际结果 | 本地证据 |
| --- | --- | --- |
| Foundation 与服务端启动 | **43/43**；真实 TCP/KCP 回环，服务端 1 场景 / 20 空确定性帧 | `foundation.log` |
| Architecture | SDK SHA、12 个程序集、依赖方向、模拟纯净与双引擎包/版本检查通过 | `architecture.log` |
| 双引擎工程环境 | 共享目录链接、SDK 一致性、独立引擎资产与 MiniGame 目标门禁通过 | `environment.log` |
| Unity 6000.6.3f1 EditMode | **46/46**，无失败或跳过 | `Unity-EditMode.xml` |
| Unity 6000.6.3f1 PlayMode | **25/25**，Direct3D 11，无失败或跳过 | `Unity-PlayMode.xml`、`Unity-PlayMode.log` |
| 团结 2022.3.62t16 EditMode | **62/62**，无失败或跳过 | `Tuanjie-EditMode.xml` |
| 团结 2022.3.62t16 PlayMode 图形重跑 | **16/16**，Direct3D 11，无失败或跳过 | `Tuanjie-Graphics-PlayMode.xml`、`Tuanjie-Graphics-PlayMode.log` |

保留首次团结 PlayMode 无图形运行的 **11/16 通过、5 项失败**：Demo、内容和世界表现测试触发 `Not implemented GfxDevice::DrawBuffers() is called` 断言。原始失败结果为 `Tuanjie-PlayMode.xml`；随后在同一隔离工程启用 Direct3D 11 重跑得到 16/16，没有忽略错误日志、跳过失败测试或修改测试断言。两个引擎的图形运行均由日志确认使用实际 AMD Radeon 图形设备。

**验证边界：**本次没有重新构建 Web、IL2CPP 或微信导出，也没有执行微信开发者工具、真机、在线后台、性能或发布验收。上述结果是本地回归，不能代替 GitHub Actions 结果或平台审批；SDK 兼容包、共享元数据、平台 Runtime/Editor 与工程设置仍需平台负责人评审后才能合并/发布。本机缓存、构建产物和 `doc/参考/`、`docs/参考/` 均不纳入本次提交。

## 2026-10-05：WSL 原生六进程与 30 天 Token 客户端闭环

本条更新前述 Windows 宿主状态：六角色现均在 WSL2 原生 Linux 进程运行，MySQL/Redis 仅绑定本机。新增默认固定 30 天、可配置时长的 Token；Windows DPAPI 本机缓存、启动恢复、不续期、撤销/过期拒绝。白名单与密钥仅存在忽略配置。真实短信供应商按用户决定留空。

- `Test-Backend.ps1` 通过：Linux 六进程、MySQL/Redis、白名单/OTP、防重放、固定 30 天与 2 小时配置、重启/撤销/暖缓存过期拒绝、路由/房间/inbox 幂等。
- `Test-BackendClient.ps1` 9 项通过：真实 Unity UI 登录、WorldCommon 场景、重启 Token、断网缓存/重试、撤销清理，以及 test OTP 后免短信恢复。
- `Manage-WSLResources.ps1 -Action Test` 通过：现有发布包 11 文件 hash、越界和写入拒绝。`Test-WSLResources.ps1` 使用真实 YooAsset 远程文件系统读取 foundation-001 的两张表，共 76 bytes；下载进度完成。
- Foundation **85/85**、Architecture 通过、Unity EditMode **46/46** / PlayMode **55/55**，编译 0 error / 0 warning。
- 本轮修复 Overlay 根对象误销毁、协程异常处理编译错误、WSL 路径/工作目录、MySQL 精度、截图路径和 WebGL 文件系统选择后复验通过。

本轮 UI 场景使用 Editor simulation，WSL 远程读取另行验证；没有将 Editor assembly 模式视作 HybridCLR 原生成功。Runtime/DPAPI 变化未重新通过原生母包或微信真机验收，平台评审仍待完成。正式业务规则、多人 AOI/PvP、短信与生产部署不在已验证范围。需求、命令、证据位置和兼容/回滚见 `Backend-Requirements.md`、`Backend-Validation.md`、`ADR-0006-Backend-Client-Session.md`。

## 2026-10-05：WSL 远程完整启动与原生热更复验

本节更新上一条资源与原生验证边界。沿用现有 YooAsset / HybridCLR 构建工具，通过 Unity CLI 发布当前资源，再由 WSL2 原生只读宿主提供；六个后端角色、MySQL 和 Redis 继续在 WSL2 运行。手机号白名单只存于忽略的本机配置；Token 默认固定 30 天，可通过 `INSECTSPACE_SESSION_HOURS` 配置，恢复不续期。真实短信供应商按用户决定留空。

- `Build-WSLClientResources.ps1 -StartHost` 与 `Test-BackendClient.ps1 -RemoteResources`：9 项通过；同一启动链实际下载 Core 版本、manifest、4 张表和 WorldCommon 场景，再验证白名单登录、大厅、世界场景、Token 重启恢复、断网保留/恢复、退出撤销和 test OTP 后免短信恢复。证据：`.artifacts/validation/backend-client/result.json`。Unity 6 Editor 使用已编译玩法，不声称原生代码注入。
- WSL 资源宿主：当前发布包 14 文件 hash 一致，越界、目录、未知文件及写入拒绝通过。`Test-WSLResources.ps1`：2 张基础表共 76 bytes、进度完成、远程 prefab 实例化/释放与 scene 加载/卸载通过；证据：`.artifacts/validation/backend-resources/test-result.json`、`unity-download.json`。
- 团结 `2022.3.62t16` Windows IL2CPP 的 `Native`、`NativePatch` 与 `Test-WSLNativePatch.ps1` 通过：14 份 AOT metadata、8 模块、4 张表，实际加载新代码 revision；WSL 冷缓存 28 次下载、3 个 WorldCommon bundle，母包 `GameAssembly.dll` SHA-256 保持不变。原生 DPAPI 合成凭据加密写入/恢复/清理通过。证据：`.artifacts/validation/native-player.log`、`native-patch-result.json`、`backend-resources/native-wsl-result.json`。
- 回归：Foundation **85/85**、Architecture 通过，Unity 隔离 EditMode **46/46**、连接图形编辑器的 PlayMode **55/55**，Unity CLI 编译通过。证据：`.artifacts/validation/backend-foundation-final.log`、`backend-architecture-final.log`、`unity6/EditMode.xml`、`client-demo/PlayMode.json`。

原生验证先后因缺少 IL2CPP 模块、导出 Visual Studio solution 设置与旧导出目录冲突失败；用户批准官方安装后，锁定可执行 Player 输出并隔离旧产物后通过。冷缓存首次移除内置目录导致缺少 BuiltinCatalog，改为空清单后通过，测试后恢复内置资源与缓存。无图形 PlayMode 曾因 RenderTexture.Create 失败得到 33/55，改用真实图形编辑器复验 55/55，没有屏蔽失败。团结编码的共享脚本 GUID 通过既有 ExportGuids / Convert-PortableMetadata 工具兼容转换后，Unity 编译恢复。

测试针对当前工作树，包含用户尚未提交的四表、玩法及渲染内容；本轮提交仅包含后端客户端/资源验证相关增量，不接管其他改动。Runtime/Editor 变更的核心平台/服务端正式评审仍待完成。本阶段交付骨架、契约及本地闭环，正式经济/任务/活动结算、多人 AOI/PvP、生产运维、真实短信、微信网络/安全存储与真机发布未验收。启动与复验命令见 [Backend-Validation.md](Backend-Validation.md)。
