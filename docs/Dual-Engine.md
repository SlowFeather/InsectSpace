# 团结微信小游戏与 Unity Web 开发

日期：2026-10-01。平台变更提案与执行记录；**平台评审仍待签署，本文不是发布批准**。

## 两个工程入口

| 引擎 | 工程 | 主要用途 |
| --- | --- | --- |
| 团结 1.10.4 / 2022.3.62t16 | `client/tuanjie/InsectSpaceClient` | 微信 MiniGame、现有 HybridCLR 原生验证 |
| Unity 6.6 / 6000.6.3f1 | `client/unity/InsectSpaceClient` | Editor 玩法开发、CLI/Pipeline、浏览器 Web 开发版 |

不要用另一种编辑器升级同一个目录。`Packages`、`ProjectSettings`、URP 资产、场景、Library、构建缓存和产物分别保存。团结模板来自升级前已提交的工程配置，Unity 入口保留此次 Unity 6 升级配置。

首次克隆或换电脑，在 PowerShell 7 执行：

```powershell
./tools/Initialize-Client.ps1 -Engine Tuanjie
./tools/Initialize-Client.ps1 -Engine Unity
./tools/Initialize-Client.ps1 -Engine Tuanjie -Open
# 或
./tools/Initialize-Client.ps1 -Engine Unity -Open
```

本机自动发现固定版本编辑器。其他安装目录可传 `-Editor`，或设置 `INSECTSPACE_TUANJIE_EDITOR` / `INSECTSPACE_UNITY_EDITOR`；不要把个人路径写进工程配置。

## 共用与隔离

唯一业务源码仍在 `client/unity/InsectSpaceClient/Assets/InsectSpace`。团结通过本地目录链接共用 Editor/Shared、Runtime、HotUpdate、Rendering 脚本、Tests、LubanRuntime、Resources 配置和 Content/Data 数据；Windows 使用无需管理员权限的 junction，其他系统使用目录符号链接。Editor 根目录的 asmdef 各自保留，只有团结引用 WxEditor。初始化遇到已有普通目录或错误链接会报错，绝不覆盖其内容。

从任一工程编辑这些共享目录，修改的都是同一份源码。不要删除团结侧链接所指向的文件来“清理副本”。链接本身不提交，目录的 `.meta` 和引擎独立资产提交；新成员先运行初始化脚本。代码模块继续遵守现有所有权和 AOT 边界。

场景、prefab、材质、URP 14/17 配置是两个引擎各自的资产。对它们的修改需要分别同步或通过同一 Editor 生成工具再生成，不能把 Unity 6 序列化结果无条件写回团结。SDK 均使用完整、固定版本的 vendor 发布包：团结保留原包；Unity 使用官方 YooAsset 3.0.6 与单独编号的 GF Unity 6 API 适配包，移除团结微信转换 SDK。GF 核心/服务端 DLL、确定性模拟和 HybridCLR 包不变。校验和及变更来源见 `vendor/unity6-sdk-release.json`。

## Unity Web 开发版

Unity 侧的场景使用 `.unity` 扩展名，团结侧继续使用 `.scene`。本次通过运行中的 Unity CLI / AssetDatabase 移动场景，保留 GUID；共享生成工具按引擎选择扩展名。不要只复制团结的 `.scene` 文件到 Unity：实测它会被识别为 `DefaultAsset`，无法进入 YooAsset 场景包。

原团结脚本/程序集元数据曾使用 Unity 6 不识别的 GUID 编码。迁移已通过团结 `AssetDatabase.AssetPathToGUID` 导出原有身份，再改写为标准 32 位 GUID；场景引用和身份保持不变，没有随机重新生成 GUID。迁移工具为 `Invoke-Unity -Engine Tuanjie -Action ExportGuids` 与 `Convert-PortableMetadata.ps1`，新克隆不需要重复迁移。架构检查会拒绝再次引入不可移植 GUID。

```powershell
./tools/Invoke-Unity.ps1 -Engine Unity -Action Web -TimeoutSeconds 1800
./tools/Run-Web.ps1
# 一步构建并启动本机服务
./tools/Run-Web.ps1 -Build
```

浏览器访问终端打印的 loopback 地址，默认 `http://127.0.0.1:8086`。服务仅监听本机，不向局域网或公网发布。需要 Node.js；浏览器通过 HTTP 加载 WASM，不要双击 HTML。

构建在 `.artifacts/unity6/ValidationClient` 的普通文件副本中执行，输出到 `.artifacts/unity6/WebDevelopment`。它使用固定的 WebGL target、Development Build 和 `INSECTSPACE_WEB_DEVELOPMENT` 编译符号。玩法程序集正常编译进入 Player，经已有反射入口返回 AOT 接口；AOT 程序集仍不引用热更程序集或生成表类型。隔离工程生成专用 `link.xml`，保留玩法、GF 反射工厂与 YooAsset 文件系统构造函数；每次资源构建使用独立输出目录，避免覆盖固定版本包。

此模式明确标识 **LOCAL WEB DEVELOPMENT**：使用随构建附带的 YooAsset Core 表和 WorldCommon 资源，运行同一套本地玩法；不请求微信身份，不启用在线服务器，不执行 HybridCLR 原生注入，也不提供浏览器代码热更新。改变脚本后重新构建，资产可继续使用 YooAsset 接口。开发构建保留引擎组件，避免仅存在于 AssetBundle 的组件被裁剪；浏览器显示按刷新节奏运行，表中的移动端 FPS 预算仍由团结使用。生产包的裁剪、性能和热更新需单独评审。

Web 开发设置不允许用于普通 Player/生产发布，专用放行只允许固定隔离目录、固定输出目录和 Development WebGL 构建。网络失败不会选择此模式。浏览器在线模式仍需独立的 WSS/WebTransport 等平台适配与服务端验证，桌面 TCP/KCP 不能直接当作浏览器传输。

微信和 Unity Web 的 WASM、AssetBundle、代码 payload、母包身份与 CDN 发布不能混用。团结 MiniGame/微信转换仍用原平台路径和发布门禁。

## CLI 环境

Unity 工程固定 `com.unity.pipeline` 版本，等待包导入后使用：

```powershell
unity status --json
unity list --project-path ./client/unity/InsectSpaceClient --json
unity command editor_status --project-path ./client/unity/InsectSpaceClient --json
```

Unity CLI 技能可用 `unity skill install codex` 安装。它不等于 Pipeline 服务已连接，也不等于团结支持该包。不要在团结 2022 工程安装 Unity 6 Pipeline。代理、账号、凭据保持在机器环境中，项目不存储代理地址或令牌。

若 `status` 显示 ready 但命令超时，先查看编辑器是否被弹窗阻塞。本机曾被 URP 材质升级提示阻塞；完成升级后，`editor_status`、`eval`、Play/Stop 均恢复正常。Safe Mode 则应先解决编译错误再退出，不要把 CLI 安装成功等同于当前工程可运行。

## 回归与平台评审

```powershell
./tools/Test-Architecture.ps1
./tools/Test-Foundation.ps1
./tools/Test-ClientEnvironment.ps1
./tools/Invoke-Unity.ps1 -Engine Tuanjie -Action EditMode
./tools/Invoke-Unity.ps1 -Engine Tuanjie -Action PlayMode
./tools/Invoke-Unity.ps1 -Engine Unity -Action EditMode -TimeoutSeconds 1200
./tools/Invoke-Unity.ps1 -Engine Unity -Action PlayMode -TimeoutSeconds 1200
```

默认 `Invoke-Unity` 仍是团结，旧 MiniGame/Native 命令和隔离路径不变。Unity 结果单独写到 `.artifacts/validation/unity6`。隔离副本会把共享链接展开成普通文件，构建修改配置不会回写正在打开的工程。

平台评审范围：双引擎包清单、源码链接、Web 配置校验、反射保留、构建门禁与独立资源输出。合并/发布前由平台负责人审核；不生成或伪造 `InsectSpacePlatformApproval.json`。本次实际执行结果只记入 `Validation.md`，编辑器/浏览器通过不能代替微信真机验证。
