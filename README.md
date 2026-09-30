# InsectSpace

面向团结引擎 1.10.4 的微信小游戏项目框架。此阶段交付的是**能启动、可验证、可分组开发的工程底座**，不是已完成的 MMO 或业务原型。

## 快速启动

1. 使用 **Tuanjie Editor 1.10.4 / 2022.3.62t16** 打开 `client/unity/InsectSpaceClient`，等待包导入和编译。
2. 执行菜单 `InsectSpace > Foundation > Open Bootstrap Scene`。
3. 点击 Play。Console 应出现 `FOUNDATION_READY modules=8 tables=2`。
4. 打开 `InsectSpace > Foundation > Runtime Status` 查看启动状态、表数量和高/中/低画质切换。

首次成功编译后会自动生成启动场景和必要设置。启动场景是斜向俯视的灰盒，不代表最终美术品质；原 `SampleScene.scene` 保留。

默认是明确标识的 **LOCAL SMOKE**：真实初始化 GF / YooAsset、读取真实 Luban 二进制表、通过独立热更程序集入口注册 8 个业务模块、执行 20 个空战斗帧后返回本地世界状态。它不伪装成真实登录、在线玩家或真实战斗。编辑器模式不执行原生 HybridCLR 注入。

## 工具命令

在仓库根目录使用 PowerShell 7；服务端和独立测试使用 .NET 10 SDK：

```powershell
./tools/Build-Tables.ps1                  # 真正的 Luban 4.5.0 导表，生成客户端和服务端代码/数据
./tools/Test-Architecture.ps1             # 核心校验和、程序集依赖方向、确定性代码检查
./tools/Test-Foundation.ps1               # .NET 回归 + 真 TCP/KCP 回环 + 服务端启动
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

新机器导表前运行 `./tools/Build-Tables.ps1 -Install`，或传入 `-LubanPath`。团结安装路径不同，给 `Invoke-Unity.ps1` 传入 `-Editor`。验证工具使用 `.artifacts/unity/ValidationClient`，不关闭、不操作正在打开的原工程。

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
| `client/unity/InsectSpaceClient/Assets/InsectSpace/Runtime` | AOT 启动、资源、代码注入、平台边界 |
| `client/unity/InsectSpaceClient/Assets/InsectSpace/HotUpdate` | 热更业务，各组在 Modules 子目录工作 |
| `client/unity/InsectSpaceClient/Assets/InsectSpace/Rendering` | URP 适配、相机与画质 |
| `design/luban` | 唯一配置源，策划维护定义与表 |
| `server` | 本地验证主机、服务端生成表、后续服务拆分边界 |
| `docs` | 架构、团队边界、发布门禁、验收记录 |

GF 核心沿用参考项目 `GameFramework` 的 **1.0.0-alpha.7 预编译 SDK**，不复制或修改其核心源码。当前工程没有对参考工程的运行时绝对路径依赖。

## 先读这些

- [整体架构](docs/Architecture.md)
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
