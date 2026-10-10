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

## 2026-10-07：人物、草地与云雾研究及 MuMu 素材对照

本轮更新 Unity 6 的 HeroCloudStudy：人物/探索两种透视机位随宽高比重新构图，草地按自然疏密分布，加入路径、花、石块、灌木和远树林，调整暖白积云、冷青阴影及迷雾层次。默认场景仍使用 Valerya；根据用户授权，从 Root MuMu 的已下载资源另建 `Assets/Temp/AFKStudy/AFKCloudStudy.unity`，使用 Faye 原始骨骼、待机动画、贴图与草花网格作本地对照。

- `tools/Test-Foundation.ps1`：**85/85**，服务端启动检查通过；`tools/Test-Architecture.ps1`：通过。
- Unity EditMode **46/46**、完整 PlayMode **56/56**；修正花朵 alpha 在 Forward/DepthOnly/ShadowCaster 中的一致裁剪后，专项 `InsectSpace.Tests.HeroCloudStudyTests` **5/5**。完整与专项结果分别保存在 `.artifacts/validation/hero-cloud-study/2026-10-07/PlayMode-All.json`、`PlayMode-HeroStudy.json`。
- 首次完整 PlayMode 为 **55/56**：角色 `SkinnedMeshRenderer.bounds` 包含动画预留范围，构图断言误报。改为 BakeMesh 的实际姿态范围后全量通过，没有放宽断言边界来掩盖真实裁剪。
- AFK 实际顶点探针通过：有效 Generic Avatar、循环待机 **1.633333 秒**、7 个蒙皮 Renderer；3 个姿态 × HERO/EXPLORE × 16:9/9:16/3:4 均在视口内，根位置不漂移，蒙皮顶点有变化。证据为日期目录下 `afk-probe.json`，不将探针计入 Unity Test Runner 的 56 个测试。
- 默认场景与 AFK 场景均检查横竖屏截图；实际操作验证了控件展开/收起、MIST 预设和 EXPLORE 切换。三个研究 Shader 编译诊断为空。`Start-HeroCloudStudy.ps1 -AfkReference` 实际启动成功，人物和云动画恢复实时运行。
- 从 MuMu 拉取 **6,133 个 LPak / 10,792,623,333 bytes**；目录切出 **214,972 个 UnityFS**，另保留 **26,149 个非 UnityFS**，这些非 UnityFS 条目的长度全部完整。目录另有 **2,242 个未下载包**，不声称已获取服务器全库。原包、清单、批次日志和可搜索导出索引位于忽略的 `.artifacts/reference/afk-journey/all/`。

旧版 GUI 在集中解析 Shader 包时曾内存不足；批处理改用 AssetStudioModCLI v0.19.0。试跑发现扁平文件名碰撞和 Windows 长路径错误，最终流程保留输入相对路径，输出使用对象 ID，原名称留在 XML/CSV/SQLite 索引。批次转换统计及残留异常以本地 `export-result.json` 为准，不能把“原包已保留”当作“所有 Shader、动画和 FBX 均已转换可用”。

商业素材仅位于忽略目录，本地渲染对照不代表获得发布授权。未修改 GF、vendor/shared、平台 Runtime/Editor 或全项目管线设置；未运行本轮团结、WebGL、微信或设备性能/发布验收。复建与预览入口见 [人物与云雾研究](Hero-Cloud-Study.md)。

## 2026-10-08：AFK 研究场景收尾复验

本轮只复验 Unity 6 本地研究场景和项目门禁：

- Unity `6000.6.3f1` `recompile`：0 errors / 0 warnings。
- `tools/Test-Architecture.ps1`：通过。
- `tools/Test-Foundation.ps1`：**85/85**，`SERVER_FOUNDATION_READY scenes=1 deterministicFrames=20`。
- `tools/Test-ClientDemo.ps1 -Mode PlayMode -Filter InsectSpace.Tests.HeroCloudStudyTests`：**5/5**。
- `AgentScripts/VerifyAfkStudy.cs`：有效 Generic Avatar、待机 1.63333344 秒、7 个蒙皮 Renderer、真实顶点变形、根不漂移；Hero/Explore × 16:9、9:16、3:4 全部通过。
- 截图证据：`client/unity/InsectSpaceClient/Assets/Temp/AFKStudy/AFK-Explore-Adjusted-Desktop-3.png`、`AFK-Explore-Adjusted-Portrait-3.png`。

`git diff --check` 仍只报告 `HeroCloudStudy.unity` 中已有的空对象 `m_Name:` 尾随空格；这些是 Unity 序列化场景的空名称字段，本轮没有改动其语义。AFK 商业素材、原始包和导出索引继续位于忽略目录，仅供本地画面对照；本轮没有做 MuMu 注入、移动设备性能、微信真机或发布门禁验收。

## 2026-10-08：AFK 湖岸渲染复现

本轮新增独立的 `Assets/Temp/AFKStudy/Replica/AFKLakeside.unity` 本地研究场景，以及 `tools/Start-AfkRenderStudy.ps1` 启动入口。场景复用了本机解包的 Faye Generic Avatar、待机动画、草花网格和湖岸环境参考，加入专用分层岩壁、湖面、程序化草花、营地远景、横竖屏正交构图和 Day/RainNight 两套天气。

- Unity 6 编译：**0 errors / 0 warnings**；Unity **6000.6.3f1**。
- `BuildAfkReplica.VerifyAndCapture`：Generic Avatar 有效，待机 **1.63333344 秒**，真实蒙皮变形 **0.0393344**，根节点不漂移；横屏和竖屏人物均未裁切。
- 四张截图均无紫色 Shader 错误：`Day-Desktop.png`、`Day-Portrait.png`、`RainNight-Desktop.png`、`RainNight-Portrait.png`；探针为 `render-probe.json`。
- Foundation：**85/85**；Architecture：通过；`git diff --check`：通过（仅保留已有 Unity 场景空名称字段的尾随空格提示）。

证据目录：`.artifacts/validation/afk-render-replica/2026-10-08/`。商业参考包、导出索引和场景均属于忽略目录下的本地研究内容，没有进入正式发布资源；本轮未进行 MuMu 注入、移动设备 GPU/内存、微信真机或生产发布验收。

## 2026-10-08：AFK 原家园数据恢复与湖岸对照

默认入口现为 `Assets/Temp/AFKStudy/Homestead/AFKRecoveredLakeside.unity`，以解包原岩壁、树木、灌木、芦苇、蒲公英、建筑和 Faye 重新组合 MuMu 湖岸构图。`Start-AfkRenderStudy.ps1 -FullMap` 打开独立的原始静态布局 `AFKHomestead.unity`。两者不能混称为准确恢复了 MuMu 中用户的动态家园。

- 完整布局有 **34,182** 个静态实例、基础 **232 meshes / 136 materials / 204 textures**，另有 **3,194** 个原水块。当前预制体库重导 **298** 项；默认场景 **900 renderers / 810 组原草实例**。
- 按解包 GLES 校正公告板左右方向、竖直阴影、阴影接收偏移、芦苇 diffuse/BaseColor 混合、花朵 `_Scale` 及位置随机旋转。全地图复查发现巨大花朵后补齐 `_Scale`，重新截图确认尺寸恢复。修正横屏下粗地面三角形穿出湖面的接缝。
- UnityCLI `BuildLibrary`、`UpdateRecovery`、`FrameFullMap` 和 `Start-AfkRenderStudy.ps1 -Rebuild -Preset Day` 实际执行成功；检查了 `data.result.success` 与内部 diagnostics，不只依据 CLI 退出码。
- Unity `6000.6.3f1` 编译 **0 errors / 0 warnings**；Foundation **85/85**，服务端 `SERVER_FOUNDATION_READY scenes=1 deterministicFrames=20`；Architecture 通过。
- Unity Test Runner：`InsectSpace.Tests.HeroCloudStudyTests` PlayMode **5/5**，无失败或跳过。这是现有渲染研究回归；AFK 新场景另由专项探针检查，没有将其计入 Test Runner 数量。
- 最终 `VerifyAfkRecovered.Run`：有效 Avatar、7 skins、原循环待机 **1.63333344 秒**；蒙皮顶点变形平方量 **0.0393344**、根稳定。日夜 × 1600×900 / 1080×1920 四图均非空、人物未裁切、magentaSamples 为 0；开关实时阴影有 **18,925** 个显著变化像素（320×568）。已目视检查默认场景和完整地图截图。
- Python 提取 helper 已迁至 AgentScripts；6 个脚本语法检查通过，入口导入及真实 catalog 依赖解析通过（家园场景 18 bundles / 30,769,253 bytes）。本轮没有在空档案上重跑全量导出；源档案、补充导出和旧角色场景仍是本机依赖。
- 本次 AFK 源码/脚本/文档范围的空白与冲突标记检查通过。保留现有 HeroStudy 用户改动，没有改 GF、vendor/shared、平台 Runtime/Editor 或全项目管线。

证据位于 `.artifacts/validation/afk-recovered/2026-10-08/`，包括 `verification-command.json`、`render-probe.json`、四张 Day/Night 截图、`FullMap-Portrait.png`、`PlayMode-HeroStudy.json`、`compile.json`、Foundation/Architecture 日志。默认场景最终保持 Day Play。

视觉仍未一比一：缺细级别 VT、原水面反射及完整 RenderFeature，动态家园布局、主角、花种/比例和建筑特效尚有差距。无紫色错误与探针通过不能替代视觉一致性判断。没有完成本轮移动性能、团结/WebGL、微信真机或发布验收。启动与数据边界见 [AFK 渲染研究](AFK-Render-Study.md)。

## 2026-10-09：AFK 夜景、角色材质与水面增量复验

本条更新上一条的画面和恢复状态。按用户要求以渲染为重点，湖岸仍使用近似布局；从已恢复的原材质、编译 GLES、环境曲线、LUT 和 MuMu 保存照片继续校准。默认场景现有 **974 renderers / 810 组原草实例**，花朵更换为原 `pbsc_bio_ep05_dandelion_01_hd`。

- 水 Shader 部分恢复原 `skybox_02` 反射、流动噪声与光斑；Faye 三份不透明 `iGame/Char` 材质使用原贴图/参数与专用适配 Shader，恢复主光环境权重、强度上限和附加光 ramp。角色皮肤 SDF、深度轮廓光、妆容、溶解、闪光及 IBL 尚未完整恢复。
- 岩壁顶面、植被、草高、水色及角色暖光继续按截图人工校准；`_StudyNight*` 与局部填充/遮蔽不是原游戏参数。补齐两个适配 Shader 的点光投影变体；默认场景关闭点光投影，以主光阴影的 0.7 遮蔽权重近似暖光填充，避免校准光产生第二组蓝影。
- `Start-AfkRenderStudy.ps1 -Rebuild -Preset Night` 与最终 `VerifyAfkRecovered.Run` 成功，内部 diagnostics 为空。Unity **6000.6.3f1** 编译检查：**0 errors / 0 warnings**。
- Foundation：**85/85**，`SERVER_FOUNDATION_READY scenes=1 deterministicFrames=20`；Architecture：通过，SDK hashes、12 assemblies、依赖方向及双引擎配置检查通过。
- Unity Test Runner：`InsectSpace.Tests.HeroCloudStudyTests` PlayMode **5/5**。此为现有渲染回归；AFK 专项探针不混入 Test Runner 数量。
- 最终探针：有效 Generic Avatar、7 skins、原循环待机 **1.63333344 秒**，变形平方量 **0.0393344**、根稳定；三份不透明角色材质使用专用 Shader，角色光白天关闭/夜晚开启。角色光影响 **1,391** 像素、环境光池影响 **135,110** 像素（540×960）；方向光阴影影响 **11,513** 像素（320×568），临时点光投影影响 **7,437** 像素（540×960），探针结束恢复原点光设置。
- Day/Night × **1600×900 / 1080×1920** 四张图均非空、人物未裁切、magentaSamples 为 0，已目视检查。证据目录 `.artifacts/validation/afk-recovered/2026-10-09/` 包含 `render-probe.json`、`verification-command.json`、四图、`rebuild.log`、`compile.json`、`PlayMode-HeroStudy.json`、Foundation/Architecture 日志。角色提取模块导入检查通过，本轮收尾没有重新导出全量参考档案。
- MuMu ADB 连接已确认 `device`。本轮参考图 `.artifacts/reference/afk-journey/mumu-live-oct09.png` 是游戏内保存的夜景照片，未把照片边框与 UI 当成渲染区域。VT 只有 38 个可用页面、13 个缺失 bundle，所属 `LDRes/prgroup_LDRes_VT_5130716192642926209.lpak` 本机与 MuMu 均未找到，细级地表画笔细节仍缺失。
- AFK 源码、脚本与文档范围空白/冲突标记检查通过；保留已有 HeroStudy 改动，没有修改 GF、vendor/shared、平台 Runtime/Editor 或全项目管线。最终保留 Night Play 预览。

仍未达到原游戏视觉一致：地表色块层次、完整水面/角色着色、云影体积雾、建筑特效和主角存在差距。探针与回归通过只证明本地研究场景可运行及相关行为，不作为视觉一致、移动性能、团结/WebGL、微信真机或发布验收。

## 2026-10-09：AFK 流式岩壁恢复与最终渲染验证

本条替代上一条的最终场景数量、花朵、像素计数和收尾状态；上一条保留为历史记录。按用户要求以渲染效果为重点，整体场景仍是近似布局。默认 `AFKRecoveredLakeside.unity` 现有 **957 renderers / 810 组原草实例**，花朵使用原 `pbsc_bio_chapter03_dandelion_01_hd`。

- broadleaf 岩壁从常驻 LOD2 升为解包流式 LOD0；六份网格分别有 118 / 126 / 146 / 156 / 128 / 134 个顶点。场景恢复两段原轮廓的 **7 个岩壁模块**及对应高台地表，保留原尺寸与相对接缝。`BuildLibrary` 每次最后调用 `ImportCliffRecovery`，防止重建回退到 LOD2。裁出的高台地表是开放片，关闭其投影，岩壁仍提供真实阴影。
- 可逆昼夜 A/B 比较涵盖原太阳方向、原 zone-light、顶面曝光/饱和度/tint 与暖光颜色。当前缺少完整区域数据的构图中，原 zone-light 直用偏亮偏青；最终保留 yaw −40°、ambient contribution 0.88。岩壁夜景曝光 0.8、顶面曝光 0.4、饱和度 0.6、tint `(0.9, 1.05, 0.9)`；高台地表曝光 0.46，前景草地保留白色顶面 tint。暖光强度 22、范围 10.5、颜色 `(1, 0.82, 0.52)`。这些是人工匹配值，不是原游戏配置。比较截图位于 `source-comparison/`；该比较发生在最终开放地表投影调整前。
- 比较脚本恢复空 `MaterialPropertyBlock` 曾导致植被缩小而阴影尺寸正常，改为 `SetPropertyBlock(block.isEmpty ? null : block)` 后复验尺寸正常。`DisableBatching=True` 用于对象空间变形，不将其写成该回归的修复依据。
- 最终 `BuildAfkHomestead.BuildStudy` 和 `VerifyAfkRecovered.Run` 均成功，内部 diagnostics 为空。Unity **6000.6.3f1** 编译检查：**0 errors / 0 warnings**。证据为 `rebuild-final.json`、`verification-final.json`、`compile-final.json`。
- `tools/Test-Foundation.ps1`：**85/85**，`SERVER_FOUNDATION_READY scenes=1 deterministicFrames=20`；`tools/Test-Architecture.ps1`：通过，SDK hashes、12 assemblies、依赖方向、确定性代码及双引擎配置检查通过。最终日志为 `Foundation-final.log`、`Architecture-final.log`。
- `tools/Test-ClientDemo.ps1 -Mode PlayMode -Filter InsectSpace.Tests.HeroCloudStudyTests`：**5/5**，completed、0 failed / skipped / inconclusive，脚本退出码 0。证据为 `PlayMode-HeroStudy-final.json`。这是现有渲染研究回归，AFK 专项探针不计入 Test Runner 数量。
- 最终探针：有效 Generic Avatar、7 skins、原循环待机 **1.63333344 秒**、变形平方量 **0.0393344**、根稳定；三份不透明角色材质使用专用 Shader，角色光白天关闭/夜晚开启。方向光阴影影响 **10,679** 像素（320×568）；角色光影响 **1,375**、地面光池影响 **126,434**、临时点光投影影响 **6,329** 像素（540×960）。探针结束恢复原点光设置。
- Day/Night × **1600×900 / 1080×1920** 四张最终图均非空、人物未裁切、magentaSamples 为 0，已目视检查。最新截图、`render-probe.json` 和以上证据均位于 `.artifacts/validation/afk-recovered/2026-10-09/`。最终 MuMu 参考为 `.artifacts/reference/afk-journey/mumu-live-oct09-latest.png`，显示游戏内保存的夜景照片，比较时排除照片框与 UI。
- 最终场景与材质已通过 UnityCLI / Editor API 保存为 **Night**，`TimeOfDay=0.05`、角色光开启、scene dirty=false，Play 已停止，证据为 `saved-final.json`。随后正常退出对应项目的 Unity Editor，并通过 MuMuManager 关闭 Android 15 实例 0；进程及实例状态复查均已退出，没有关闭其他编辑器或模拟器实例。

VT 仍只有 **38 个可用页面 / 13 个缺失 bundle**，缺失 `LDRes/prgroup_LDRes_VT_5130716192642926209.lpak` 在本机与 MuMu 中均未找到，细级地表画笔色块未恢复。水面反射、角色高级着色、云影体积雾、建筑特效和截图主角仍有差距；本轮没有将探针通过视作视觉一致，也没有进行移动性能、团结/WebGL、微信真机或发布验收。商业参考继续保留在忽略目录，未修改 GF、vendor/shared、平台 Runtime/Editor 或全项目管线。启动、重建及参数边界见 [AFK 渲染研究](AFK-Render-Study.md)。

## 2026-10-09：MuMu 重开后的实时昼夜校准

本条更新前述最终画面、数量及参考来源。MuMu 可用后改用实时游戏截图 `mumu-oct09-day-live.png` 和 `mumu-oct09-continuation.png`，默认场景为 **953 renderers / 810 组原草实例**，花朵为原 `pbsc_bio_chapter03_dandelion_02_hd`；流式 LOD0 岩壁继续保留。优先匹配渲染，场景仍为近似布局。

- 按实时白天画面校准太阳 yaw 偏移 +20°，夜晚仍为 −40°，并调整地表、岩壁与芦苇昼间 tint。原模板地表改为无压缩双线性采样，扩大纹理色块尺度；缺细 VT 的范围未改变。
- 水岸 sawtooth 阈值使用 `fwidth` / `smoothstep` 过滤，水体 tint 不再染色岸线；StudyWater 局部岸线宽度 0.04、合成强度 0.55。草高采用原值的 0.8 倍，暖光权重 1.5，与地面一致。参数均为本地校准，原水面 MRT 合成未恢复。可逆对照见 `comparison-shore-grass.json`、`comparison-grass-light.json`。
- `BuildAfkHomestead.BuildStudy` 重建成功，内部 diagnostics 为空；Unity **6000.6.3f1** `recompile` 为 up_to_date，0 errors / 0 warnings。证据为 `rebuild-live-calibration.json`、`compile-live-calibration.json`；Shader 另经最终实际渲染检查。
- 此次续作已执行 Foundation **85/85**、服务端 `SERVER_FOUNDATION_READY scenes=1 deterministicFrames=20`、Architecture 通过、HeroCloudStudy PlayMode **5/5**，无失败/跳过/inconclusive。证据为 `Foundation-day-calibration.log`、`Architecture-day-calibration.log`、`PlayMode-HeroStudy-day-calibration.json`；这些回归在最后的水岸和草叶暖光修正前执行，最终修正后重跑了 AFK 专项探针，没有重复全量业务回归。
- 最新 `VerifyAfkRecovered.Run` 成功、diagnostics 为空：7 skins、有效 Generic Avatar、循环待机 **1.63333344 秒**、变形平方量 **0.0393344**、根稳定；三份不透明角色材质使用专用 Shader，角色光白天关闭/夜晚开启。方向光阴影影响 **9,807** 像素（320×568），角色光 **1,385**、地面光池 **131,442**、临时点光投影 **8,591** 像素（540×960）；探针结束恢复原设置。
- 最新 Day/Night × **1600×900 / 1080×1920** 四图非空、人物未裁切、magentaSamples 均为 0，已逐张目视检查。证据为 `.artifacts/validation/afk-recovered/2026-10-09/verification-live-calibration.json`、`render-probe.json` 与同目录四张截图。早期 `*-final.json` 和上节像素计数属于历史。
- 收尾 AFK 源码、脚本与文档的空白/冲突标记检查通过。通过 UnityCLI / Editor API 保存默认场景和材质，最终为 **Night**、`TimeOfDay=0.05`、角色光开启、scene dirty=false、Play 已停止，证据为同目录 `saved-live-calibration.json`。随后 `unity close` 正常退出对应项目 Editor（PID 34140，graceful），MuMuManager 正常关闭 Android 15 实例 0（PID 43004）；复查两进程均已退出、实例 Android/process 均为 false，证据为 `closed-live-calibration.json`。

仍未达到原游戏完全一致：细 VT 地表画笔色块、完整水面合成、角色高级着色、云影体积雾、建筑特效和参考主角未完整恢复。日夜渲染与探针通过不等于视觉一致，也不是移动性能、团结/WebGL 或微信发布验收。

## 2026-10-09：AFK 高级水面、云雾、角色与连续昼夜

本条更新上面的功能完成状态。通过 UnityCLI 在 Unity **6000.6.3f1** 中重建并验证默认湖岸；采用 MuMu 实时昼夜参考和本机解包的原模板地表、云/雾纹理、环境曲线、角色 SDF 与环境立方体贴图。默认场景现有 **1,044 renderers / 810 组原草实例 / 18 个原岩壁模块**。

- 地表：原模板纹理的四组随机旋转/偏移采样平滑混合至粗 VT，补充局部绘制细节并抑制重复。扩展原右侧岩壁轮廓及配套高台，修复横屏悬空切口；高台材质匹配岩壁顶面的 AO、明暗响应、线性 tint 和夜间合成曝光 0.32，去除六边形色差接缝。可逆昼夜比较为 `advanced/comparison-plateau.json` 和 `advanced/plateau/`。
- 水面：主相机不透明颜色/深度折射、RGB 吸收、深浅水过渡、焦散/接触岸线、原天空立方体/流动噪声/光斑与实时平面反射已接通。修复平面反射垂直倒置；Surface/Character 新增 DepthNormals，解决现有 SSAO 深度预通道遗漏这些表面的问题。使用 URP 本地合成，不声称完整移植原专有 MRT。
- 氛围：原移动云纹理影响光照，原雾噪声及密度/颜色/散射曲线驱动相机专用 RenderGraph pass，24 步深度限定体积积分。无需修改 Packages、ProjectSettings 或全项目 Renderer 配置。
- 角色：UV2 面部 SDF、皮肤透射、六面环境 IBL 和深度轮廓光均参与实际像素。原 `hdr_10` / `hdr_34` 为格式 34 **ETC_RGB4 / ETC1 LDR**，已按正确格式解码；没有误当 BC6H。导入按 Shader 属性类型区分 scalar/vector/cubemap，修正 `_NoiseSizeSpeed` 与环境立方体绑定。
- 时间：太阳、环境、水色、雾和角色暖光连续变化，默认 240 秒一周期。`VerifyAfkAdvanced` 验证 `.995 + .01 → .005`，角色光强度在时间 .18/.25/.32 为 **22/11/0**；午夜太阳颜色差 **2.98e−8**、强度差 **0**。启动器支持 Day/Night/Dawn/Dusk、`-Cycle` 与 `-CycleSeconds`。
- 最终 `BuildAfkHomestead.BuildStudy`、`VerifyAfkAdvanced.Run`、`VerifyAfkRecovered.Run` 全部成功，内部 diagnostics 为空。重复帧噪声 **0**；独立效果 A/B 变化像素为：体积雾 **33,386**、平面反射 **39,229**、水深合成 **43,724**、云影/云相位各 **31,995**（540×960）；角色 SDF **561**、透射 **8,293**、IBL **10,591**、深度轮廓 **34,388**（1080×1920 近景）。计数阈值以探针源码为准，像素变化只证明功能生效，不代表与原画面一致。
- 额外数值检查：云影扫描 **16** 个相位；反射贴图有 **602** 个量化颜色，正确镜像位置有 **812** 个红标记像素；水底三个 GPU 样点的实际水深为 **0.6801 / 0.6883 / 0.6863 m**，反射开启/关闭完全一致。有效 Generic Avatar、7 skins、3 份不透明角色材质、原循环待机 **1.63333344 秒**、蒙皮变形平方量 **0.0393344**、根稳定。最终方向光阴影变化 **9,952**、角色暖光 **1,311**、地面光池 **132,846**、临时点光阴影 **8,680** 像素。
- 最终 Day/Night/Dawn/Dusk × **1600×900 / 1080×1920** 八图非空，已目视检查；Day/Night 专项探针另确认人物未裁切、magentaSamples 为 **0**。最新合图为 `advanced/Final-Overview.jpg`，角色近景为 `advanced/Character-On.png`。
- 高级渲染实现完成后执行 `tools/Test-Foundation.ps1`：**85/85**，`SERVER_FOUNDATION_READY scenes=1 deterministicFrames=20`；`tools/Test-Architecture.ps1`：SDK hashes、12 assemblies、依赖方向、确定性代码和双引擎配置通过。Unity Test Runner `InsectSpace.Tests.HeroCloudStudyTests` PlayMode **5/5**，failed/skipped/inconclusive 均为 0。最后的高台范围与材质接缝调整只涉及研究构建器，调整后重新执行两个 AFK 专项探针；没有把专项探针计入 Test Runner 数量。
- 最终 `unity recompile` 为 up_to_date，**0 errors / 0 warnings**，四个研究 Shader 诊断为空；AFK 源码、脚本、文档的空白和冲突标记检查通过。场景与材质已用 Editor API 保存为 **Day、Cycle=true、CycleSeconds=240、scene dirty=false**，启动器实测成功，保留 Editor Play 预览。

最新证据在 `.artifacts/validation/afk-recovered/2026-10-09/advanced/`：`rebuild.json`、`verification-advanced.json`、`advanced-probe.json`、`probe-measurements.json`、`verification-recovered.json`、`compile.json`、`saved-scene.json`、`launcher.log`、`live-status.json`、`Foundation.log`、`Architecture.log`、`PlayMode-HeroStudy.json` 及截图。父目录 `render-probe.json` 和 Day/Night 四图由最终专项探针更新；之前的关闭 Editor 记录和旧像素计数保留为历史。

**资源与复现边界：**原细 VT 仍有 **13 个缺失 bundle**，所属 `LDRes/prgroup_LDRes_VT_5130716192642926209.lpak` 本机与 MuMu 均不存在；现有 38 页面、1536×2048 粗 VT 无法还原原始细画笔布局。因此完成的是模板细节重建，不宣称找回缺失源资源。水面、云影/体积雾、角色高级着色与连续昼夜已实际实现；动态家园布局、参考主角、建筑特效、地表绘制布局及部分角色效果分支仍与原游戏不同。未进行移动 GPU/内存、团结/WebGL、微信真机或发布验收。商业参考继续留在忽略目录；保留已有 HeroStudy 改动，未修改 GF、vendor/shared、平台 Runtime/Editor 或全项目管线。入口与参数见 [AFK 渲染研究](AFK-Render-Study.md)。

## 2026-10-09：水岸细节与可隐藏的小精灵光源

按用户补充，角色身旁的暖光改为小精灵发光：增加本地光点/光晕/双翼轮廓，Point Light 移到角色身旁；夜间强度从旧高位补光的 22 调为 **8**、范围 **8.5**，避免降低光源后地面过曝。场景现有 **1,045 renderers**。小精灵形状为本地绘制，没有声称恢复原游戏精灵模型。

- **Fairy / light** 控件与 `SetFairyVisible(bool)` 同时控制可见光点和照明；关闭时 visual inactive、Light disabled、intensity=0。日夜预设、时间变化和午夜循环不会覆盖隐藏选择；开启时恢复。启动器增加 `-HideFairy`。
- 水面加入双方向细波纹、连续焦散波场及相应折射/反射扰动。水陆共享轻微不规则边界，浅滩横截面由 11 点增加至 21 点；沿岸距离流驱动柔和覆盖、细接触泡沫和断续移动波带，反射在浅水衰减。修正折射偏移退回主采样点时诊断 raw depth 未同步的问题。原 FullMap 不使用新增距离流，保留旧岸线分支。
- `BuildAfkHomestead.BuildStudy`、`VerifyAfkWaterFairy.Run` 和 `VerifyAfkAdvanced.RunWaterFairy` 均实际执行成功，内部 diagnostics 为空。水岸软边 A/B **30,190** 像素、细波纹 A/B **11,702** 像素；精灵显示/隐藏在白天变化 **323**、夜间变化 **121,479** 像素（1600×900，同一调用内冻结角色和时间）。隐藏在 .05/.25/.5/.75/.999 五个时刻及跨午夜都保持有效，并验证能够重新显示。
- 深度与反射回归：三个水底样点深度 **0.6846 / 0.6820 / 0.6835 m**，反射开关前后相同；正确镜像位置 **800** 个红标记像素。高级效果开关、16 相位云影扫描继续通过，重复帧噪声 **0**；晨昏精灵光强为 **8/4/0**，午夜颜色/强度连续。
- 实际检查水岸近景、昼夜精灵开关和横竖屏截图；保存 Day/Night/Dawn/Dusk 八图。Unity **6000.6.3f1** 编译 **0 errors / 0 warnings**，五个研究 Shader 诊断为空。
- `tools/Test-Foundation.ps1`：**85/85**、`SERVER_FOUNDATION_READY scenes=1 deterministicFrames=20`；`tools/Test-Architecture.ps1`：通过。最终调整后 `tools/Test-ClientDemo.ps1 -Mode PlayMode -Filter InsectSpace.Tests.HeroCloudStudyTests`：**5/5**，failed/skipped/inconclusive 均为 0。新增专项探针不混入 Test Runner 用例数。

证据：`.artifacts/validation/afk-recovered/2026-10-09/water-fairy/` 中 `rebuild.json`、`compile.json`、`verification.json`、`water-fairy-probe.json`、`verification-advanced.json`、`advanced-probe.json`、`saved-scene.json`、`PlayMode-HeroStudy.json`、Foundation/Architecture 日志、水岸近景和精灵开关对照。`launcher-hidden.log` / `launcher-hidden-status.json` 验证 `-Preset Night -HideFairy` 实际启动后精灵和光关闭；最终留在从 Day 开始的连续循环 Play，精灵隐藏，控制面板可随时恢复，见 `live-status.json` / `Controls.png`。此前 `advanced/` 数据保持为上一轮基线。未修改 GF、vendor/shared、平台 Runtime/Editor 或全项目管线；本轮没有执行移动性能、团结/WebGL 或微信真机验收。

## 2026-10-09：沿岸持续扩散波纹

根据用户提供的岸线近景图，将原来衰减过快的单层内侧波带改为从岸边不断向湖内推进的多道波峰。水网格 G 通道使用到最近岸线线段的距离；弯曲岸线的推进方向不再依赖横向坐标。默认速度 **0.4 m/s**、间距 **0.8 m**、周期 **2 s**、宽度 **0.065 m**、范围 **2 m**，具有近岸渐入、远处淡出、轻微纹理扰动及导数抗锯齿。

- `BuildAfkHomestead.BuildStudy` 与 `VerifyAfkShoreWaves.Run` 实际执行成功，内部 diagnostics 为空。场景仍为 **1,045 renderers**。
- 在浮点 GPU 诊断帧中，0.6 / 1.2 / 2.6 秒的首道波峰距离为 **0.2182 / 0.4537 / 0.2182 m**，0.6 秒内向外推进 **0.2355 m**，与目标 0.24 m 一致；2.05 米外 mask 为 **0**，两秒周期的最大 mask 差为 **1.55e−6**。这验证的是实际像素中的推进方向、距离衰减和周期连续性。
- 波纹开关 A/B 在 **960×540** 近景变化 **13,087** 像素。已保存并目视检查昼夜近景、六个相位合图，导出 **40 帧 / 4 秒**循环动图。录帧只固定岸波相位以便复查，结束恢复 `_StudyShoreWaveTime=-1` 使用实时播放时间。
- Foundation **85/85**、`SERVER_FOUNDATION_READY scenes=1 deterministicFrames=20`；Architecture 通过。Unity HeroCloudStudy PlayMode **5/5**，无失败/跳过；水 Shader 编译诊断为空。新专项探针不计作 Test Runner 用例。

证据目录 `.artifacts/validation/afk-recovered/2026-10-09/shore-waves/`：`rebuild.json`、`verification.json`、`shore-waves-probe.json`、`Day-Waves.png`、`Night-Waves.png`、`Phases.jpg`、`Shore-Waves.gif`、Foundation/Architecture/Unity 日志与 PlayMode 报告。小精灵隐藏功能保留；未改平台目录、项目管线或正式发布资产，未执行移动/微信发布验收。

## 2026-10-10：向岸波纹与浅岸轻微起伏

根据用户纠正与 MuMu 实时游戏录屏，本条修正上一条的传播方向：细波带由湖内向岸边移动，到岸逐渐消隐。通过 UnityCLI 在 Unity **6000.6.3f1** 中重建默认湖岸，仍为 **1,045 renderers**。原游戏参考为 `.artifacts/reference/afk-journey/mumu-oct10-water-live.png`、`mumu-oct10-water-live.mp4` 和相位合图；截图为白天，录屏为夜间。

- 水 Shader 相位由减去时间项改为加上时间项，同一道波峰的岸线距离随时间减小。速度 **0.4 m/s**、间距 **0.8 m**、周期 **2 s**；波带宽度由 0.065 调为 **0.045 m**，范围由 2 调为 **1.8 m**，强度由 0.75 调为 **0.6**，匹配较细、较弱的参考水纹。
- 浅岸顶点真实起伏与接触线进退共用波场，默认高度幅度 **0.009 m**、水线幅度 **0.035 m**，沿岸错相并在距岸 1.2 m 内衰减。法线、高光和反射随之变化；FullMap 未启用局部柔岸的分支保持位移关闭。
- `VerifyAfkShoreWaves.Run` 成功、内部 diagnostics 为空。浮点 GPU 样点在 0.8 / 1.4 / 2.8 秒测得波峰距岸 **0.453011 / 0.218399 / 0.453011 m**，0.6 秒内向岸移动 **0.234613 m**，目标为 0.24 m；远处 mask 为 **0**，2 秒周期误差 **2.71e−6**。高度峰峰值 **0.015527 m**、水线偏移峰峰值 **0.060651 m**、深水位移 **0**，起伏周期误差 **1.20e−7**。
- 960×540 近景的波带 A/B 变化 **8,511** 像素，起伏 A/B **3,257** 像素。保存并目视检查昼夜近景、相位图和 **40 帧 / 4 秒**动图；录帧结束恢复 `_StudyShoreWaveTime=-1`。Day/Night/Dawn/Dusk × 1600×900 / 1080×1920 八图通过横竖屏合图目视检查。
- `VerifyAfkAdvanced.RunShoreWaves` 与 `VerifyAfkWaterFairy.RunShoreWaves` 均成功、内部 diagnostics 为空。水底深度 **0.681758 / 0.677005 / 0.688754 m**，反射开关前后相同；镜像位置 **810** 个标记像素。云影、体积雾和角色 SDF/透射/IBL/深度轮廓保持实际像素变化，重复帧噪声 **0**；午夜连续、晨昏精灵光强 **8/4/0**。精灵关闭在五个时间点及跨午夜保持 visual/light disabled，重新显示成功。
- 实际执行 `tools/Test-Foundation.ps1`：**85/85**，`SERVER_FOUNDATION_READY scenes=1 deterministicFrames=20`；`tools/Test-Architecture.ps1`：通过。`tools/Test-ClientDemo.ps1 -Mode PlayMode -Filter InsectSpace.Tests.HeroCloudStudyTests`：**5/5**，failed/skipped/inconclusive 均为 0。专项 GPU 探针不计入 Test Runner 数量。
- 最终 `unity recompile` 为 up_to_date、**0 errors / 0 warnings**；水 Shader supported=true、hasError=false、diagnostics 为空。用 Editor API 保存默认场景为 **Day、Cycle=true、240 秒、小精灵隐藏、scene dirty=false**，水诊断视图关闭，使用实时波纹时间。启动器 `-Preset Day -Cycle -HideFairy` 恢复可操作 Play 预览，面板可随时显示精灵。

最新证据目录 `.artifacts/validation/afk-recovered/2026-10-10/shore-waves/` 包含 `verification.json`、`shore-waves-probe.json`、`verification-advanced.json`、`verification-fairy.json`、`compile.json`、`shader-diagnostics.json`、`saved-scene.json`、`launcher.log`、`live-status.json`、`PlayMode-HeroStudy.json`、Foundation/Architecture/Unity 日志及截图动图。本轮没有修改 GF、vendor/shared、平台 Runtime/Editor、Packages 或 ProjectSettings；细 VT 的 13 个缺失 bundle 和既有复现差距仍保留，不把该优化声称为原游戏完全一致或移动/微信发布验收。
