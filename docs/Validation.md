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
| Web 浏览器实际运行 | WASM HTTP 200；`FOUNDATION_READY modules=8 tables=4`、`WEB_DEVELOPMENT_READY modules=8 tables=4`；无 console error、JavaScript 异常或启动失败 | `.artifacts/validation/unity6/web-browser-result.json`、`web-browser-console.log` |
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

## 新手引导与第一章第一节细化（2026-10-02）

新增 [第一章·第一节：把心交给希望](../design/narrative/Chapter01-Section01-Onboarding.md)：六段约 90 秒的人祖寓言分镜、八个生活／仪式场景、渐进 UI 教学、希望蛊群像、玩家控制前行与随机资质、登记和下一节交接。按用户新要求替换原稿的统一初始资质，同步剧情总纲、系统稿、内容入口与原著核对。保留原有 QM01-01／QM01-02 任务 ID，首节奖励示例为 55 元石，没有扩出重复领奖节点。

本次实际重新执行 Foundation：**43/43 通过**，服务端 1 场景、20 空确定性帧，证据为 `.artifacts/validation/onboarding/foundation.log`。Architecture **通过**，成功工具输出记录在 `architecture-result.json`；该文件为本次输出的转录，不冒充额外测试。

文档检查 **9/9 通过**：原参考哈希不变、八个场景、六段连续 90 秒分镜、资质概率合计 100% 且步数区间不重叠、首节奖励锚点、存档节点唯一、链接与编码、旧统一资质规则替换、二十四章与青茅二十个主线 ID 保持。证据为 `.artifacts/validation/onboarding/document-review.json`。这些检查仅针对文本结构、算术、来源与底座回归，不证明随机公平性、资质平衡、演出可读性或真实新手完成率。

本次仅修改 Markdown 文档，**未运行 Unity EditMode／PlayMode**，没有新增脚本、场景、动画、语音、UI 资产、在线任务或随机服务。实际制作后的九项体验验收清单已列于细稿，均未宣称执行。资质概率、元海范围、改善路线、镜头时长与奖励数值是初测提案，不是已部署功能。

## 人物、积云与迷雾画风研究（2026-10-02）

交付 [人物、积云与迷雾研究](Hero-Cloud-Study.md)：Unity 6000.6.3f1 场景 `HeroCloudStudy.unity`，银发 Valerya 人物、Mixamo 自动绑定及 Breathing Idle、程序化草地、晴天/黄昏/夜晚/迷雾预设。原模型 CC BY 与 Mixamo 动画来源分别记录。没有修改受保护 SDK、shared、平台 Runtime/Editor 或项目渲染管线配置。

用户反馈云边毛刺后，移除云轮廓高频噪声，重新生成轮廓距离图集；Shader 通过屏幕导数重建约一个像素宽的抗锯齿覆盖率。当前桌面预览使用无压缩三线性采样，不用模糊后处理掩盖轮廓。云图集内存成本增加，移动 ASTC 版本和真机性能未验收。

本轮实际验证与本地证据位于 `.artifacts/validation/hero-cloud-study/`：

| 检查 | 实际结果 | 证据 |
| --- | --- | --- |
| Foundation | 43/43；服务端 1 场景 / 20 确定性空帧 | `final-foundation.log` |
| Architecture | SDK、12 个程序集、依赖方向及双引擎清单通过 | `final-architecture.log` |
| Unity EditMode，云边修复后全量 | 46/46，无失败/跳过 | `EditMode.json` |
| Unity PlayMode，云边修复后全量 | 29/29，无失败/跳过 | `PlayMode.json` |
| FBX 显式重算法线后人物专项 | 4/4，7 个网格法线数量与顶点一致 | `Normals-PlayMode.json`；CLI 导入输出 |
| 模型转换器 | `--mixamo` 连续执行两次成功；ZIP 为 1 OBJ + 1 MTL + 7 贴图，最终来源记录未覆盖 | 本次 CLI 执行输出 |
| Shader 与最终状态 | 三个专用 Shader 无错误、诊断为空；Day/Q1、动画速度与 timeScale=1 | `final-live-audit.json` |
| 画面核验 | 同机位/同姿态的 Day/Mist 三档截图、云边 200% 原像素对照、3 个相近云相位裁切 | `Day-Mist-Comparison.png`、`Cloud-Edge-Comparison.png`、`Cloud-Motion-Crops.png`；`Assets/Screenshots/HeroCloud_*` |

保留首次 PlayMode 的 27/28 结果：新加的动画循环测试在 `.99` 时间设置后同步推进取样过早，读取到 `.99000001`。修复为先应用状态，再等待实际帧推进；没有跳过变形或循环断言。新增迷雾测试后顺序全量重跑为 29/29；初次失败证据 `Initial-PlayMode.json` 未覆盖。测试覆盖待机网格实际变形、根位置固定、循环、三档草量、预设切换以及雾在禁用/切场景时恢复。

全局告警没有被描述成零：回归期间可见已有 CS0618、字体引用和资源服务退出告警。FBX 缺法线告警已改为显式 Calculate，两个本次 Shader 的混合行尾也已统一。曾有工具截图请求使用错误 source 参数，已改为 screen 并成功捕获，历史命令错误保留在 `final-console.json`。最后导入核验的 Console error 数为 0，仍可见已有代码的弃用 API 告警，见 `final-preview-console.json`。

本轮未验证团结、WebGL/微信构建、移动 GPU 性能、真实在线场景或发布门禁。自动蒙皮只验待机，未证明头发/盔甲在战斗大幅动作下质量达标。迷雾是解析距离雾与地表颜色处理，不是体积雾。当前云边已清晰，草叶粗细、草带自然度和衣甲材质仍有后续美术精修空间。

## 元石、仙元石与战斗储备通讯（2026-10-02）

交付见 [经济通讯](Economy.md) 和 [ADR-0002](ADR-0002-Economy-Protocol.md)。使用 UnityCLI 连接 Unity **6000.6.3f1**，通过 Editor API 创建 `Assets/InsectSpace/Demos/LocalEconomy.unity`，实测 Unity 客户端与独立 .NET 服务器的 GF TCP 钱包/房间准入及真实 KCP 资源帧协议。源码与协议归业务模块；没有修改参考 GF 仓库、vendor、shared、平台 Runtime/Editor 或 asmdef。

| 检查 | 最终实际结果 | 证据 |
| --- | --- | --- |
| Foundation 完整回归 | **56/56**，包含 13 项经济测试；服务端启动为 1 场景 / 20 确定性空帧 | `.artifacts/validation/economy/Foundation-Kcp-Final.json`；`tools/Test-Foundation.ps1` 输出 |
| Architecture | SDK 校验、12 个程序集、依赖方向及双引擎配置通过 | `.artifacts/validation/economy/Architecture-Kcp-Final.txt` |
| Unity CLI 编译 | 成功，KCP 客户端增量编译 0 error / 0 warning | `.artifacts/validation/economy/compile-kcp.json` |
| Unity 全量 EditMode | **46/46**，无失败或跳过 | `.artifacts/validation/economy/Kcp-All-EditMode.json` |
| Unity 全量 PlayMode | **37/37**，含 8 个经济测试；无失败或跳过 | `.artifacts/validation/economy/Kcp-All-PlayMode.json` |
| 实际双进程 TCP/KCP 完整验收 | 十步全部通过，脚本退出码 0；Unity 发起托管、入场、施法、使用两种储备、断线恢复、撤离结算与世界消费 | `tools/Test-EconomyDemo.ps1`；`.artifacts/validation/economy/Kcp-Live-Final.log`、`kcp-live-*.json` |
| 储备与钱包实值 | 钱包 (100,10) → 托管 (20,4)、钱包 (80,6) → 施法仙元 100→70 且储备不变 → 使用后储备 (19,3) → 结算钱包 (99,9) | `kcp-live-initial.json`、`reserved`、`cast`、`used-xian`、`settled` 对应文件 |
| KCP 重连与状态校验 | 使用元石后断开 TCP/KCP，重新准入仍为原房间，仙元 80、储备 (19,4)，继续使用仙元石成功 | `kcp-live-reconnected.json`、`kcp-live-recovered.json`、`kcp-live-used-xian.json` |
| TCP 大世界立即消费 | 结算后再消耗 3 元石，最终钱包 (96,9)、储备为零、仙元 100/100 | `kcp-live-world-debit.json` |
| 界面与 Console | 已查看最终 KCP 界面截图，所有标签可辨认，下部操作通过原生滚动区域访问；最终快照 Ready=true、Error=null；记录时 Console 为 0 error / 3 warning | `Assets/Screenshots/Economy-Kcp-Final.png`；`Kcp-Console.json` |
| 容量上限恢复与结算 | 缩小房间历史上限为 3，真实 TCP/KCP 连续重连填满准入记录；超限施法被拒，仍可恢复并撤离，完整返还未消费的 (20,4)，再开新房间仍拒绝 | `economy.kcp-capacity-preserves-recovery-and-settlement`，收录于最终 Foundation 结果 |

测试包含：奖励/充值可信入口与去重、同一支付订单跨角色拒绝、并发防双花、计入托管后的溢出检查、请求序号和身份匹配、旧版本拒绝、超时后同操作号重试、真实 TCP/KCP 断线重连不自动退款、世界价格不可由客户端伪造、零储备开战且施法不扣钱包、重复房间/输入/结算防重入、缺帧等待、帧排序/hash 一致及非法帧整批不生效。容量修复仅修改服务端宿主和 .NET 测试，之后重跑 Foundation、Architecture 与真实 Unity 双进程验收；Unity 全量 46/37 是此前最终客户端源码的测试结果，没有将服务端检查冒充额外一轮 Unity 测试。所有数据只在内存，重启丢失。

逐项完成核验：

| 原始需求 | 实现及实际证据 |
| --- | --- |
| 元石通过活动/打怪获取 | 服务端 `GrantReward` 接受可信活动/怪物事件，幂等入账；客户端不能发奖。资格/概率服务尚未接入，控制台事件是明确的本地演示 |
| 仙元石必须充值 | 活动/怪物入口只能发元石，仙元石由 `ApplyVerifiedRecharge` 入口发放；测试重复订单和跨角色复用拒绝，当前回执为模拟，未接真实支付 |
| 两种石头可带入储备、剩余带出 | 原子托管、服务器房间绑定及按权威剩余量退款；实际 KCP 使用各一颗后钱包为 (99,9) |
| 帧战斗主要消耗空窍仙元 | 普通施法 100→70，不改钱包或储备；零储备开战也已通过。显式消耗石头恢复仙元是本次实现约定 |
| TCP 大世界消费实际扣除 | 服务器查价即时扣款，客户端不能报价；双进程验收从 (99,9) 扣至 (96,9)，Foundation 另验仙元石世界消费 |
| 先数据结构与通讯，暂不接数据库 | 共享编译纯 C# 结构、2001/2002 版本化协议、真实 TCP/KCP；MySQL/Redis 无接入 |
| UnityCLI 完成本地 Unity 6 工程 | CLI 创建原生场景、编译、完整 EditMode/PlayMode，并驱动实际客户端十步网络验收 |

保留失败过程：TCP 首阶段的 .NET **51/52**，畸形包 `InvalidDataException` 未被过滤，补充异常过滤后通过；新增零储备/控制台测试后的首次 **52/53**，连接失败测试按固定步长计时，在实际墙钟超时前未积累够模拟秒数，改用真实经过时间驱动网络测试后达到 **53/53**（历史 `Foundation-Final.json`）。初次直接 Unity PlayMode 被既有 HeroCloudStudy Play 启动场景覆盖而未产生测试结果，已取消；随后全量测试本身通过，但旧临时测试场景恢复失败。恢复有效场景后首阶段顺序重跑为 EditMode 46/46、PlayMode 36/36 且脚本退出码 0；加入实际 KCP 客户端后完整重跑为上表 46/37。KCP 服务端曾因旧进程占用 DLL 构建失败，停止本任务的旧服务后重建成功。没有把未完成、恢复失败或测试失败写为通过。Pipeline 回调记录存在重复条目，计数使用 summary 和唯一 FullName。

Console 历史缓冲仍保留此前人物展示任务的截图参数错误，未删除或掩盖；当前 groundTruth 为 0 error、3 warning，不描述为所有历史运行零警告。

**范围边界：**充值种子和控制台充值均明确为 SIMULATED；生产支付验签、账号认证、MySQL/Redis、持久化账本、正式掉落概率、真实多人 AOI、伤害/胜负与完整战斗玩法未接入。当前是明确的本地单玩家 KCP 资源房间，资源帧由服务器 20 Hz 调度，客户端实际重放并验证；不依赖控制台伪造网络战斗，也不代表生产多人房间已完成。未执行团结/微信/WebGL 发布构建、原生热更回归或设备 QA，现有发布门禁仍适用。平台公共契约提取与发布评审尚未执行。

## 转数与玩家随机资质（2026-10-02）

交付见 [转数与资质](Cultivation.md) 与 [ADR-0003](ADR-0003-Cultivation-Protocol.md)。实际核对用户提供的 Bilibili 蛊修资料及既有新手策划；模型区分未开窍、一至五转四小阶、六至九转仙阶，客户端和服务器 Compile Link 同一纯 C# 协议。普通资质采用项目初测丁/丙/乙/甲 20/50/25/5 权重，由服务端事务内随机一次；概率不是参考文章的原著概率。

通过 UnityCLI 在 Unity **6000.6.3f1** 内创建原生 `LocalCultivation.unity` 场景，使用真实 TCP 7779 的 2003 协议，复用本地角色身份。没有修改参考 GF 仓库、vendor、shared、平台 Runtime/Editor 或 asmdef。既有工作区的美术、剧情和设置差异未作为本次成长功能内容处理。

| 检查 | 实际结果 | 证据（均在 `.artifacts/validation/cultivation/`，截图除外） |
| --- | --- | --- |
| .NET 服务端构建 | 成功，0 错误 / 0 警告 | 本次 `dotnet build` 输出；随后 Foundation 再构建并通过 |
| Foundation | **63/63**，新增 7 项成长测试；既有经济与 KCP 回归保留 | `Foundation.log`、`Foundation.json` |
| Architecture | SDK SHA、12 个程序集、依赖方向、模拟纯净与双引擎清单通过 | `Architecture.log` |
| Unity CLI 编译 | 成功，记录的增量编译 0 error / 0 warning | `compile.json` |
| Unity 全量 EditMode | **46/46**，无失败或跳过 | `EditMode.json` |
| Unity 全量 PlayMode | **41/41**，新增 4 项成长测试，无失败或跳过；脚本正常恢复场景并退出 0 | `PlayMode.json`；`Test-ClientDemo.ps1` 输出 |
| 真 Unity + 独立服务器晋升 | 从未开窍到九转，全部阶段/跨转门槛/九转上限通过；脚本退出 0 | `Live-Client.log`、`Live-Server.log`、`live-rank-*.json` |
| 随机与重连 | 本次验收角色丙等、43% 元海、22 步，从一转到九转及重连保持；重复开窍明确拒绝 | `live-awakened.json`、`live-reroll-rejected.json`、`live-final-reconnect.json` |
| 最终九转事实 | Rank=9、Stage=None、三个灾劫计数各为 3、主修道痕 300000、成尊证明齐备、经验余 64000；钱包仍为 (100,10) | `live-final-reconnect.json` |
| 最终预览 | 正常本地服务另起进程，客户端 Ready、Error=null；新演示角色一转初阶、丙等、59%、22 步 | `Final-Preview.json`；`Assets/Screenshots/Cultivation-Final.png`，已实际查看 |
| Console | 当前 groundTruth 为 0 error / 3 warning；历史缓冲未清除，不声称整个历史零错误 | `Console-Final.json` |

Foundation 覆盖：全部 10000 个权重输入得到精确分档数量、百分比/步数边界、并发 32 次同一开窍只调用随机源三次（档位/步数/占比）、重复创建不重置、所有 24 个修炼状态的展示、丁等完整到九转、经验/证明不足不突破、299999 道痕不可成尊、请求/奖励事件去重、跨角色事件复用拒绝、溢出无部分入账、版本/畸形包拒绝、超时重试不重新抽样、真实 TCP 重连和 KCP 战斗期间拒绝突破。枚举遍历证明概率映射边界正确，不冒充生产随机分布或长期数值平衡的统计验收。

`Test-CultivationDemo.ps1` 启动自己的临时内存服务器，客户端通过 UnityCLI 调用开窍/突破意图，服务器本地控制台发放明确的模拟经验与试炼证明；不在 Editor eval 中改服务器或客户端角色字段。验收结束关闭自己创建的进程并恢复原连接端口。最后重新启动普通本地服务，所以预览角色与临时验收角色的百分比不同；这属于已明确标注的进程重启清空，不是重连重抽。

保留首次双进程脚本失败：一转中阶等待超时。原因是 PowerShell 函数参数 `$Stage` 与循环的 `$stage` 大小写不敏感且动态作用域重名，等待谓词拿到了阶段标签字符串；修正参数名为 `$Checkpoint` 后全流程通过。未改变服务器突破规则、跳过断言或把首次超时当作成功；首次产物保存在 `Initial-Live/`。本次修改后端/客户端的 Unity 和 .NET 回归均已通过，脚本修复后重跑的是完整双进程验收。

**边界：**目前是转数/资质数据、可信判定入口与通讯闭环；真实账号、数据库、跨重启一次开窍、角色槽/删号重建账本、正式经验数值、花海演出、资质改善、实际升仙/灾劫关卡及其概率保底尚未接入。能量类型已识别，但未把新资质绑定到旧 KCP 资源样例的固定 100 点池；正式战斗数值仍需后续版本化接入。未执行团结、WebGL、微信构建或设备 QA，不替代平台发布门禁。


## 2026-10-03：凡蛊组合与流派系统

已在 Unity 6000.6.3f1 + .NET 10 本地项目执行。新增 Player/GuPaths 业务目录，未修改受保护的 vendor/shared/Runtime/Editor、asmdef 或参考 GF 工程。资料原文 SHA-256 验证未变。

- `tools/Build-GuTables.ps1`：使用固定 Luban 4.5.0 生成，客户端/服务端两张 bytes 哈希一致。首批 43 条记录：一转 9、二转 7、三转 14、四转 7、五转 6；配置 42 个流派 ID，当前蛊虫主标签覆盖 12 个流派。来源与设计归类见 `docs/Gu-Catalog-Sources.md`。
- 最终目录指纹：`e18ccf223ec7092f679773e27f8d42d7f79d41e94ebc9dd59f7af93ff00a5ad7`。修正一个原文异常章节标题，并将月霓裳/宝月光王、蓄力/惯力归入各自相同进阶系列后重新导表。
- `tools/Test-Foundation.ps1`：最终 **71/71**，包含 8 项新增流派测试；服务端 bootstrap 输出 `SERVER_FOUNDATION_READY scenes=1 deterministicFrames=20`。覆盖可解释评分、精确并列、同系列重复、所有权/转数、原子拒绝、回执容量、并发幂等、目录版本、畸形包、客户端关联/原操作重试及真实 TCP/KCP 换装锁定。首次畸形包测试发现 `InvalidDataException` 捕获遗漏，已修复并全量重跑通过。
- Unity CLI 编译成功。`tools/Test-ClientDemo.ps1 -Mode All -Filter InsectSpace.`：**EditMode 46/46，PlayMode 45/45**。之后只调整了来源文字、两组 family 配置与竖屏 UI；再执行相关 `InsectSpace.Tests.GuPathTests`：**4/4**。最新 `client-demo/PlayMode.json` 是这次四项定向回归，不冒充全量报告。
- `tools/Test-Architecture.ps1`：最终通过（SDK 校验和、12 个程序集、依赖方向、确定性代码与双引擎配置）。`git diff --check` 通过。
- `tools/Test-GuPathDemo.ps1`：最终目录上 **26 个实际 Unity/TCP 检查点通过**。临时 .NET 服务监听 17777–17780；验证未开窍拒绝、服务器开窍、一转月道成型、越阶拒绝、重连、真实 KCP 房间换装锁、离场后混修、按既有修炼规则突破到三转、同系列拒绝、力道成型和最终重连。钱包始终为 100 元石/10 仙元石。脚本只终止自身服务并恢复面板原端口。
- 验收证据：`.artifacts/validation/gu-paths/live-*.json`、`Live-Server.log` 与 `Live-Server-errors.log`。`Assets/Screenshots/GuPaths-Final.png` 为 1080×2160 竖屏真实 Game 视图，已目视检查目录滚动、组合确认/草稿、来源文字均可读。
- 最后 Unity Console ground truth：**0 errors / 0 warnings**，`compilationFailed=false`；Pipeline 保留缓冲区累计 68 warnings（没有清空历史来掩盖记录）。
- 当前保留 `LocalGuPath` Play 演示与本地 7779 服务，一转固定开发角色已确认“月光蛊 + 小光蛊 → 月道成型”；`local-ready.json` 记录此状态。该进程与验收用临时三转角色互相独立。

本次完成数据、所有权/组合校验、流派识别、客户端目录/草稿 UI 与网络闭环。没有实现各蛊的技能效果、杀招自动生成、境界/道痕成长、完整蛊虫实例背包、交易炼化、正式奖励、数据库、正式账号鉴权或微信真机网络；目录功能描述不能当成已实现战斗逻辑。已有经济/修炼协议回归通过，流派组合不会自动消费钱包或提升修为。正式发布仍需平台协议评审与 WeChat Release Gates。

## 2026-10-03：蛊虫养炼配置与斜俯视移动增量

本轮实际执行：

- `tools/Test-WorkshopTableValidation.ps1`：10/10 通过，覆盖可变长度配方材料、空材料列表、字段错误、概率超限、未知蛊/食物、重复材料、数量上限、转数门槛和非整数价格；只使用 `.artifacts` 临时 fixture，未写入正式表。
- `tools/Test-Foundation.ps1`：**80/80**，包含养炼购买、饥饿/休眠、单炼三分支、合炼三分支、并发/幂等、协议截断、客户端重连和真实 TCP/KCP 战斗锁测试。
- `tools/Test-Architecture.ps1`：通过（SDK 校验和、依赖方向、确定性检查和双引擎配置）。
- `unity recompile`：Unity **6000.6.3f1**，0 error / 0 warning。
- `tools/Test-ClientDemo.ps1 -Mode All -Filter InsectSpace.`：EditMode **46/46**，PlayMode **49/49**；新增四项本地导航测试。
- 通过 Unity CLI 重新创建 `Assets/InsectSpace/Demos/LocalWorldNavigation.unity`：真实 3D 几何、斜俯视相机、可运行 NavMesh、点击地面寻路、键盘/触控摇杆手动移动、任务/NPC 到达后停止；自动跑图与手动接管共用同一个本地移动组件。截图 `Assets/Screenshots/WorldNavigation-3D.png` 已目视检查。

养炼四张正式表仍为空，运行时会明确阻止工作坊启动；待用户确认概率、饱食周期和首批路线后再填表和做正式联调。导航当前是明确标注的 LOCAL 原型，没有冒充在线 AOI、服务器移动权威或生产寻路。

## 2026-10-03：斜俯视镜头与 NPC 养炼入口复核

- Unity CLI 实测场景相机为透视投影、FOV 50°，旋转约 `(37.72, 319.09, 0)`；已查看 540×960 竖屏 Game 画面 `Assets/Screenshots/WorldNavigation-Portrait.png`，房屋侧面和地面纵深可见。之前 1280×720 截图把竖屏画面拉伸，不作为竖屏比例验收依据。
- 地图靠近 NPC 后需手动交互打开养炼页，返回保持位置；面板显示时停止地图输入。新增一项入口测试，验证远距离拒绝、自动到达不创建工作坊、玩家点击才打开、返回保留位置。
- `tools/Test-Foundation.ps1`：**81/81**，服务端 bootstrap 通过；包括空正式配置拒绝启动的回归。
- `tools/Test-Architecture.ps1`：通过。Unity CLI 编译 **0 errors / 0 warnings**。
- `tools/Test-ClientDemo.ps1 -Mode All -Filter InsectSpace.`：**EditMode 46/46，PlayMode 50/50**，脚本退出码 0。
- 实际 CLI 驱动场景从出生点连续寻路到坊市，确认到达时工作坊尚未创建；显式交互打开第 0 页，空配置明确拒绝联网，关闭后位置不变。证据：`.artifacts/validation/world-workshop-entry/`；工作坊入口截图 `Assets/Screenshots/WorldNavigation-WorkshopEntry.png`。
- 同一 Play 会话继续寻路到饲蛊师和炼蛊工坊，显式交互分别选中第 1、2 页；未提交购买、喂养或炼制。最终 Console ground truth 为 **0 errors / 3 warnings**，保留历史缓冲，不描述为全局零警告；见 `live-feeder.json`、`live-forge.json`、`console.json`。

本轮四张正式表仍未启用，不声称已完成正式表下的 Unity/TCP 购买—养炼闭环；当前验证了镜头、导航、NPC 面板入口、空配置防误启用以及既有服务端 fixture 回归。未执行在线 AOI、微信或真机验收。

## 2026-10-03：Unity 养炼 TCP/KCP 独立验收

- `tools/Test-WorkshopTableValidation.ps1`：10/10 通过；`Test-WorkshopTables.ps1 -AllowUnconfigured` 与 `Build-WorkshopTables.ps1 -SchemaOnly` 通过，正式四表仍为空。
- `tools/Test-WorkshopDemo.ps1` 使用 TEST ONLY 临时表和独立 .NET 宿主，通过 Unity CLI 创建测试探针，实测 **45 个检查点**全部通过：连接/开窍、购买三个独立实例、单炼成功/失败保留/失败毁蛊、合炼三结果、组合、饥饿休眠、补喂、重连、KCP 战斗锁和不确定请求重试。
- 不确定炼制实测：服务端钱包从 67 扣至 66，重试返回原回执，随机调用从 10 增至 11；没有重复扣费或重新抽取。客户端/服务端临时 Luban bytes 哈希一致；脚本结束时正式 JSON、正式 bytes 哈希均未变化。证据：`.artifacts/validation/workshop-live/20261003-085441-630/summary.json` 与各检查点 JSON。
- 该验收使用测试概率、10 秒饱食周期和合炼配方，全部标记为 TEST ONLY，不能当成正式游戏数值。

截至本历史条目，当时基础回归为 Foundation **81/81**、Unity EditMode **46/46**、PlayMode **50/50**；后续正式表启用和当前回归见下方 2026-10-04 条目。

## 2026-10-04：世界遭遇与月光蛊首期战斗

本轮完成并复核世界地图遭遇入口：竖屏 3D 斜俯视地图支持 NPC 自动寻路、点击地面移动、键盘/触屏摇杆接管；靠近野怪遭遇点后必须由玩家点击进入独立 `LocalMoonlightBattle` 场景。战斗为 1 名主角对 1 只野怪，自动普攻与配置化野怪普通攻击并行，月光蛊首技“月刃”支持手动和自动释放，胜负或撤退返回世界地图。

规则和技能来自 Luban 配置：`design/luban/Tables/battle_rules.json`、`battle_skills.json`；权威整数帧模拟位于 `shared/com.insectspace.simulation/Runtime/MoonlightBattle.cs`，本地 TCP/KCP 房间宿主位于 `server/InsectSpace.Server/Economy/LocalMoonlightBattleHost.cs`。本地服务器使用内存数据，明确标记为 LOCAL；未实现 PvP 帧同步、在线 AOI、账号鉴权或持久化数据库。

| 检查 | 实际结果 | 证据 |
| --- | --- | --- |
| Foundation | **85/85**，含月光蛊确定性、非法帧/哈希原子校验、TCP/KCP 入场、输入、撤退和养炼回归；服务端启动校验通过 | `tools/Test-Foundation.ps1` 输出 |
| Architecture | 通过，SDK 校验、12 个程序集、依赖方向、确定性模拟和双引擎配置通过 | `tools/Test-Architecture.ps1` 输出 |
| Unity 6 编译 | **0 errors / 0 warnings**；Unity **6000.6.3f1** | `unity recompile` 输出 |
| Unity PlayMode | **50/50**，包含世界导航、NPC 显式交互和遭遇入口测试；脚本退出码 0，测试结果状态为 completed | `.artifacts/validation/client-demo/PlayMode.json`、Unity `test_status` |
| 代码格式检查 | `git diff --check` 通过 | `git diff --check` 输出 |

此前测试器收尾阶段曾引用已删除的临时 `InitTestScene`；`tools/Test-ClientDemo.ps1` 已增加不存在场景过滤和正式世界场景回退，修复后全量 PlayMode 50/50 正常退出并恢复到 `LocalWorldNavigation`。该历史验证发生在正式表启用前；当前正式表状态见下方 2026-10-04 条目。

## 2026-10-04：正式养炼原型表与当前 Unity 回归

本节更新上文“正式养炼表仍为空”的历史状态；相关 2026-10-03 记录反映当时配置，不再代表当前仓库。

- `tools/Test-WorkshopTables.ps1` 通过，当前四表包含 `care=3`、`offers=4`、`recipes=1`；物品表配置月兰花瓣和月华石。
- `tools/Build-WorkshopTables.ps1` 使用 Luban 4.5.0 实际生成客户端与服务端代码/数据，双方养炼表 bytes 哈希一致。
- `tools/Run-LocalServer.ps1 -LocalWorkshop -TcpPort 17877 -EconomyPort 17879 -EconomyBattlePort 17880` 启动通过。工作坊宿主在启动时装载了正式蛊目录和生成的四张养炼表；服务端仍使用内存存档，重启会清空。
- Unity CLI 打开 `LocalWorkshop` 并运行时，客户端报告 `ready=True`、`connected=True`、`服务器已确认`，读到 `items=2, offers=4, care=3, recipes=1`；验证后已停止该 Play 会话与临时服务器。
- `tools/Test-Foundation.ps1`：**85/85**；`tools/Test-Architecture.ps1`：通过。
- `tools/Test-ClientDemo.ps1 -Mode All -Filter InsectSpace.`：Unity 6 EditMode **46/46**、PlayMode **50/50**。
- 以上表内的商店价格、300 秒饱食时长、单炼/合炼成功率和毁蛊率，是内部原型默认值，便于玩法和网络联调，不是用户确认的最终平衡参数。`tools/Test-WorkshopDemo.ps1` 仍使用 TEST ONLY 临时表，其覆盖结果不作为正式数值验收。

本节没有验证微信传输、生产账号/存档、真实 AOI 或 PvP 帧同步。

## 2026-10-05：WSL2 后端基础骨架与手机号白名单

本轮在本机 WSL2 `Ubuntu-24.04` 原生安装并启动 Redis 7 与 MySQL 8，Windows 侧仅运行 .NET 宿主进程；数据库和缓存连接通过 WSL2 地址访问。密码、会话签名密钥和白名单只写入被 `.gitignore` 忽略的 `.artifacts/validation/backend/wsl.env`，仓库没有测试凭据或手机号名单。

| 检查 | 实际结果 | 证据 |
| --- | --- | --- |
| 后端库/六角色宿主编译 | 通过，0 错误 / 0 警告 | `dotnet build server/InsectSpace.BackendHost/InsectSpace.BackendHost.csproj` |
| WSL2 基础设施 | 通过，原生 MySQL/Redis 服务可连接，迁移成功 | `tools/Initialize-WSLBackend.ps1` |
| 六进程健康检查 | 通过，Gateway、Identity、Lobby、World、Battle、Worker | `tools/Test-Backend.ps1` |
| 手机白名单登录 | 通过，白名单号码不需要验证码，空验证码直接创建会话 | `.artifacts/validation/backend/test-result.json` |
| 非白名单手机号 | 通过，测试模式 OTP 生成、校验和一次性消费 | `.artifacts/validation/backend/test-result.json` |
| 世界路由/跨服房间 | 通过，会话主域校验、路由 epoch、房间复用 | `.artifacts/validation/backend/test-result.json` |
| 领域命令 | 通过，MySQL durable inbox 与 Redis 幂等缓存 | `.artifacts/validation/backend/test-result.json` |
| Foundation 回归 | **85/85** | `tools/Test-Foundation.ps1` |
| 架构门禁 | 通过 | `tools/Test-Architecture.ps1` |

当前身份实现仍使用可替换 `IIdentityProvider` 和 `IPhoneCodeSender`；真实短信供应商、微信登录、正式业务结算规则和生产 CDN 未配置，也未据此声称通过生产或真机门禁。白名单由 `INSECTSPACE_PHONE_WHITELIST` 的 E.164 逗号/分号分隔值提供，空配置默认关闭直登。

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

## 2026-10-06：资产清理与双引擎兼容复验

本轮移除了 `client/unity/InsectSpaceClient/Assets/Screenshots/` 中已确认没有被场景、预制体或测试引用的中间截图及对应 `.meta` 文件；保留正式功能验收截图、Unity 6 工程和团结工程。没有修改 `vendor/` 或 `shared/`；Runtime 启动覆盖层只做了团结字体兼容修复，平台归属评审仍适用。

- `tools/Test-Foundation.ps1`：**85/85**，服务端启动检查通过。
- `tools/Test-Architecture.ps1`：通过；`tools/Test-ClientEnvironment.ps1`：通过，Unity **6000.6.3f1** 与团结 **2022.3.62t16** 的工程隔离、SDK 校验和共享链接检查通过。
- 团结 EditMode：**62/62**；团结图形 PlayMode：**42/42**，运行于隔离验证工程和实际图形设备。
- 为兼容团结运行时，启动覆盖层和后端大厅面板统一使用 `LegacyRuntime.ttf`；团结无图形 PlayMode 的 `DrawBuffers`/NavMesh 报错属于引擎无图形限制，不作为业务通过依据。

本轮未改变正式项目入口、服务器路由或资源回退策略。真实微信传输、生产账号、在线 AOI/PvP 和真机发布仍需按 [WeChat-Release-Gates.md](WeChat-Release-Gates.md) 完成平台验收。
