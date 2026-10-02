# InsectSpace

支持团结引擎 1.10.4 微信小游戏和 Unity 6.6 Web 开发的项目框架。此阶段交付的是**能启动、可验证、可分组开发的工程底座**，不是已完成的 MMO 或业务原型。

## 快速启动

1. 首次运行 `./tools/Initialize-Client.ps1 -Engine Tuanjie` 创建共享源码链接；用 **Tuanjie 1.10.4 / 2022.3.62t16** 打开 `client/tuanjie/InsectSpaceClient`，或用 **Unity 6.6 / 6000.6.3f1** 打开 `client/unity/InsectSpaceClient`。不要用不同引擎升级同一个目录。
2. 执行菜单 `InsectSpace > Foundation > Open Bootstrap Scene`。
3. 点击 Play。Console 应出现 `FOUNDATION_READY modules=8 tables=2`。
4. 打开 `InsectSpace > Foundation > Runtime Status` 查看启动状态、表数量和高/中/低画质切换。

首次成功编译后会自动生成启动场景和必要设置。启动场景是斜向俯视的灰盒，不代表最终美术品质；原 SampleScene 保留，团结使用 `.scene`，Unity 使用 `.unity`。

默认是明确标识的 **LOCAL SMOKE**：真实初始化 GF / YooAsset、读取真实 Luban 二进制表、通过独立热更程序集入口注册 8 个业务模块、执行 20 个空战斗帧后返回本地世界状态。它不伪装成真实登录、在线玩家或真实战斗。编辑器模式不执行原生 HybridCLR 注入。

## 工具命令

新人教学与现场演示：打开 Unity 6 工程后运行 `./tools/Start-ClientDemo.ps1`，进入含 12 个主题的中文交互实验室。覆盖启动、模块、配置、资源、平台门禁、大厅网络、角色、AOI、确定性战斗、任务、画质和热更边界；未接入后端的部分明确标注 LOCAL。完整讲解步骤、预期结果、练习与测试命令见 [客户端功能 Demo](docs/Client-Demos.md)。

Unity 浏览器开发：`./tools/Run-Web.ps1 -Build`。该命令在隔离工程构建 WebGL Development Player，再启动仅监听本机的 HTTP 服务。浏览器版明确标为 **LOCAL WEB DEVELOPMENT**，使用内置资源与已编译玩法，不代表微信 SDK、在线传输或 Unity 6 原生热更验证。完整操作与边界见 [双引擎开发](docs/Dual-Engine.md)。

草地渲染样板：打开 Unity 6 工程后运行 `./tools/Start-MeadowShowcase.ps1`，预览带骨骼待机动画的 KayKit 法师、程序化草地与风动画。`-Quality 0|1|2` 选择草量，`-TacticalGrid` 显示仅用于构图的六边形网格。启动前先保存场景并停止 Play；这是本地画面研究。来源、重建入口、截图与验证见 [Unity 6 渲染样板](docs/Rendering-Showcase.md)。

在仓库根目录使用 PowerShell 7；服务端和独立测试使用 .NET 10 SDK：

```powershell
./tools/Build-Tables.ps1                  # 真正的 Luban 4.5.0 导表，生成客户端和服务端代码/数据
./tools/Test-Architecture.ps1             # 核心校验和、程序集依赖方向、确定性代码检查
./tools/Test-Foundation.ps1               # .NET 回归 + 真 TCP/KCP 回环 + 服务端启动
./tools/Test-ClientEnvironment.ps1        # 双引擎配置、源码链接与平台隔离
./tools/Invoke-Unity.ps1 -Engine Unity -Action EditMode -TimeoutSeconds 1200
./tools/Invoke-Unity.ps1 -Engine Unity -Action PlayMode -TimeoutSeconds 1200
./tools/Invoke-Unity.ps1 -Engine Unity -Action Web -TimeoutSeconds 1800
./tools/Run-Web.ps1                       # 本机浏览器预览，Ctrl+C 停止
./tools/Invoke-Unity.ps1 -Action Prepare  # 隔离副本：团结真实导入/编译/生成场景
./tools/Invoke-Unity.ps1 -Action EditMode
./tools/Invoke-Unity.ps1 -Action PlayMode
./tools/Invoke-Unity.ps1 -Action Artifacts # 编译热更 DLL，并构建 YooAsset Core 包
./tools/Invoke-Unity.ps1 -Action Native -TimeoutSeconds 1800 # 独立工程：项目内工具链、IL2CPP 母包及真实启动
./tools/Invoke-Unity.ps1 -Action NativePatch # 不重建母包，通过本机 HTTP 发现并加载新代码包
./tools/Invoke-Unity.ps1 -Action MiniGame  # 团结 MiniGame 目标托管编译，不代表微信原生导出
./tools/Invoke-Unity.ps1 -Action MiniGameNative -TimeoutSeconds 1800 # 真正的 MiniGame WASM 导出，不等于微信转换/真机
./tools/Test-MiniGameExport.ps1           # 导出文件、平台、版本与代码 payload 完整性
./tools/Invoke-Unity.ps1 -Action WeChatNative -TimeoutSeconds 1800 # 官方微信 SDK 转换；未配置 AppID/CDN 时只验产物
./tools/Test-WeChatExport.ps1             # 小游戏子包、JSON、资源和 Brotli WASM 一致性
./tools/Run-LocalServer.ps1               # 本机 TCP 7777 / KCP 7778 诊断主机，Ctrl+C 停止
```

新机器导表前运行 `./tools/Build-Tables.ps1 -Install`，或传入 `-LubanPath`。编辑器路径可通过 `-Editor` 或对应 `INSECTSPACE_*_EDITOR` 环境变量指定。`Invoke-Unity` 默认仍选团结，使用 `.artifacts/unity/ValidationClient`；Unity 使用 `.artifacts/unity6/ValidationClient`，不会把构建配置写回正在打开的原工程。

`Native` 需要 Windows IL2CPP 支持、MSVC 与 Windows SDK，并首次下载固定提交的原生源代码；只安装到验证工程的 `HybridCLRData`，不替换全局编辑器。普通业务开发不需要每次运行原生验证。`Artifacts` 同时构建 `WorldCommon` 常规资源包。细节见 [原生验证](docs/Native-Validation.md)。

微信导出位于 `.artifacts/unity/ValidationClient/HybridCLRData/MiniGameValidation/WeChat/minigame`。
可用环境变量 `INSECTSPACE_WECHAT_APPID`、`INSECTSPACE_WECHAT_CDN` 提供项目自己的 AppID 和 HTTPS 资源根地址；
未提供时保持空值，不使用示例身份。`webgl/StreamingAssets` 需要部署到所配置 CDN 的 `StreamingAssets` 路径。
小游戏导出/转换已经通过；微信开发者工具和真机运行需要真实项目配置与设备，不属于当前的“已验证”记录。

## 工程边界

| 目录 | 用途 |
| --- | --- |
| `vendor/upm` | 固定版本 GF、HybridCLR、YooAsset 包 |
| `vendor/dotnet` | 同一 GF 发布版的服务端/测试 DLL |
| `shared` | 契约、模块生命周期、会话、确定性战斗、传输适配 |
| `client/engine-projects.json` | 两种引擎的版本、工程路径、共享源码目录与允许动作 |
| `client/tuanjie/InsectSpaceClient` | 团结微信工程；独立设置/场景/URP 14，共享业务源码 |
| `client/unity/InsectSpaceClient/Assets/InsectSpace/Runtime` | AOT 启动、资源、代码注入、平台边界 |
| `client/unity/InsectSpaceClient/Assets/InsectSpace/HotUpdate` | 热更业务，各组在 Modules 子目录工作 |
| `client/unity/InsectSpaceClient/Assets/InsectSpace/Rendering` | URP 适配、相机与画质 |
| `design/luban` | 唯一配置源，策划维护定义与表 |
| `server` | 本地验证主机、服务端生成表、后续服务拆分边界 |
| `docs` | 架构、团队边界、发布门禁、验收记录 |

GF 核心沿用参考项目 `GameFramework` 的 **1.0.0-alpha.7 预编译 SDK**，不复制或修改其核心源码。当前工程没有对参考工程的运行时绝对路径依赖。

## 先读这些

- [整体架构](docs/Architecture.md)
- [玩家剧情与长期世界设计](design/narrative/README.md)（内部策划：九幕二十四章、主支线、服务器活动、修炼与经济）
- [团结微信与 Unity Web 双引擎开发](docs/Dual-Engine.md)
- [分组开发与核心维护](docs/Team-Ownership.md)
- [热更与资源发布](docs/Hot-Update.md)
- [资源句柄与分包使用](docs/Resource-Ownership.md)
- [世界表现与移动接口](docs/World-Client.md)
- [大厅与战斗连接生命周期](docs/Session-Lifecycle.md)
- [微信发布门禁](docs/WeChat-Release-Gates.md)
- [微信平台适配](docs/WeChat-Platform.md)
- [渲染方向与预算](docs/Rendering.md)
- [实际验证记录](docs/Validation.md)
- [MiniGame 原生验证](docs/MiniGame-Validation.md)
- [原始需求完成度核验](docs/Completion-Audit.md)

**范围声明：**已验证编辑器与 Windows IL2CPP 的真实启动、AOT 注入、同母包远程代码更新和常规资源生命周期；另已完成 MiniGame 原生 WASM 导出及浏览器启动、官方微信 SDK 原生转换和产物一致性检查。微信 SDK 传输适配已落地，但真机传输/缓存仍待验证；账号鉴权、生产集群、真实 AOI 广播、技能/奖励逻辑和生产发布系统没有冒充完成。发布构建保留 smoke/平台审核门禁；开发验证不是微信上线验收。
