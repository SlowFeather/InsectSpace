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
- 未实现实际战斗技能、任务、剧情、奖励、持久化、真实美术或性能指标。
- 未完成签名启动 manifest、母包兼容矩阵、灰度与回滚的生产发布系统。
- 画面只提供灰盒与三档管线基线；无真机 GPU、内存、发热或弱网性能结论。
- CI 文件已经落地并配置 GitHub 远程；本次本地回归不代表 GitHub Actions 已执行或通过。尚未确认真实 CODEOWNERS 团队及受保护分支，不能声称服务端审批已经生效。

微信 app ID、选定平台 SDK、域名/账号后端和目标真机验收不能由 Windows 原生或 MiniGame 托管编译替代。普通 Player 发布门禁保留，不为了生成“成功”结果而关闭。
