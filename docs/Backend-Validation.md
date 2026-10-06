# 后端与本地客户端验收

日期：2026-10-06。当前状态取代此前 Windows .NET 宿主方案：**MySQL、Redis、六个宿主均在 WSL2 Ubuntu-24.04 原生运行**。Windows 仅负责 Unity 客户端、构建和脚本。范围见 [Backend-Requirements.md](Backend-Requirements.md)。当前交付摘要见 [工程状态](Project-Status.md)。

## 启动

仓库根目录使用 PowerShell 7、.NET 10 SDK、WSL2 Ubuntu-24.04。客户端要求已打开 Unity 6000.6.3f1 工程且 Unity CLI/Pipeline 可连接；先保存场景、停止 Play。

```powershell
./tools/Initialize-WSLBackend.ps1
./tools/Start-BackendServices.ps1 -Build
./tools/Build-WSLClientResources.ps1 -StartHost
./tools/Start-BackendClient.ps1 -RemoteResources
# 停客户端并恢复原场景/Play 配置
./tools/Start-BackendClient.ps1 -Stop
# 停六宿主，不删除数据库
./tools/Stop-BackendServices.ps1
```

六服务 Gateway/Identity/Lobby/World/Battle/Worker 依次监听 loopback 8080–8085。MySQL/Redis 仅绑定本机。宿主发布到 WSL ext4，supervisor 核对 PID、启动时间、可执行文件身份和 Linux health。明确 local 模式由 World 进程维护注册心跳，客户端不能注册实例；这不是 AOI 实现。

配置在 Git 忽略的 `.artifacts/validation/backend/wsl.env`。初始化保留白名单、Token 时长和现有密钥；`-RotateLocalSecrets` 显式轮换。本地初始化器设置 `INSECTSPACE_PHONE_AUTH_MODE=test`，不是生产部署工具。

## 登录和缓存

`INSECTSPACE_PHONE_WHITELIST` 配置 E.164 号码，逗号/分号分隔；空值关闭直登。白名单免验证码/密码，code 可省略、null、空或任意值；非白名单必须通过一次性六位 OTP。用户号码只在本机配置，未提交 Git。国内手机号 UI 自动规范到 +86。

真实短信供应商由用户决定暂空。production 无供应商明确失败；当前 UI 自动填入的 OTP 标为 DEV/TEST，没有发送真实短信。身份和短信分别保留 IIdentityProvider、IPhoneCodeSender 适配接口。

默认 `INSECTSPACE_SESSION_HOURS=720` 是签发起固定 30 天，允许 1–8760 整数小时。配置变更重启服务后影响新会话，不修改旧会话到期日。Windows 使用 DPAPI 保护缓存到 `Application.persistentDataPath/SessionCache/<endpointSHA>.bin`。启动向 `/v1/identity/session/login` 校验，恢复不续期。过期、损坏和撤销清缓存；断网保留并可重试；退出先服务端撤销再清本机。其他平台未接 OS protector 时明确不缓存。

## 资源托管

```powershell
# Unity CLI 调用现有 HotUpdateBuild / ContentBuild，发布当前目标的资源
./tools/Build-WSLClientResources.ps1 -StartHost
./tools/Manage-WSLResources.ps1 -Action Test
./tools/Test-WSLResources.ps1
./tools/Test-BackendClient.ps1 -RemoteResources
./tools/Manage-WSLResources.ps1 -Action Stop
```

可指定 `-SourceRoot <目标发布根目录> -Version <版本> -Port 18088`。服务只监听 127.0.0.1，允许发布清单文件 GET/HEAD，拒绝仓库、配置、目录列表及写入访问。复制到 WSL ext4 内容 hash 目录，校验发布内容未改变。

资源宿主另支持 -CoreDirectory / -WorldDirectory，将新版 Core 和其引用的固定 WorldCommon 发布目录组合。非默认端口使用独立状态目录；原生验证默认 18089，可与 Unity 资源宿主 18088 并行。成功下载证据只记录发布路径、字节数、SHA-256 和时间，不记录请求头或查询参数。

-RemoteResources 显式安装 Editor 远程文件系统，禁用 Web 缓存，资源失败不回退模拟资源。Unity 6 当前 WebGL 目标已从同一启动链下载 Core.version、manifest、四张 RawBundle 表，并完成白名单登录、大厅和 WorldSandbox 场景加载；WSL 请求记录由测试断言。Editor 玩法使用已编译程序集，明确不执行原生注入。不带该开关则保持原 Editor simulation 开发路径。

Test-WSLResources.ps1 单独验证两张基础表共 76 bytes，以及 WorldActor 实例化/释放和 WorldSandbox 加载/卸载。WebGL 按需下载时预下载数为 0 属正常行为，验收检查实际读取、对象及句柄状态。

### 原生代码热更新

固定团结 2022.3.62t16、Windows IL2CPP、MSVC 和 Windows SDK：

```powershell
# 可选：使用已校验的固定提交源码归档，仍检查锁定 SHA-256
$env:INSECTSPACE_NATIVE_ARCHIVES = Join-Path (Get-Location) '.artifacts/toolchains/downloads'
./tools/Invoke-Unity.ps1 -Engine Tuanjie -Action Native -TimeoutSeconds 1800
./tools/Invoke-Unity.ps1 -Engine Tuanjie -Action NativePatch -TimeoutSeconds 1200
./tools/Test-WSLNativePatch.ps1
```

原生测试备份验证 Player 的内置资源和缓存，放入 YooAsset 3.0.6 空内置清单，再从 WSL 冷缓存下载。检查新 gameplay DLL、Core 版本/manifest、WorldCommon bundles、NATIVE_CODE_LOADED、补丁 revision、资源生命周期与 NATIVE_SESSION_CACHE_PASSED。同一个 GameAssembly.dll 在补丁构建前后和 WSL 测试前后均保持不变；结束恢复原内置资源和缓存。DPAPI 测试使用临时合成凭据，验证原生热更代码中的加密写入、恢复和清理，不输出凭据。

## 已执行

| 检查 | 实际结果 | 本机证据 |
| --- | --- | --- |
| Linux 自包含宿主 | 六个 Linux/PID/MySQL/Redis health 通过 | `.artifacts/validation/backend/services/` |
| Test-Backend.ps1 | 白名单 4 形式、非白名单拒绝、OTP 防重放、持久化、30 天/2 小时、Token 重启/固定到期/撤销/过期、路由/房间/inbox 幂等通过 | `.artifacts/validation/backend/test-result.json` |
| Test-BackendClient.ps1 -RemoteResources | 9 项通过；同一启动链远程下载 4 张表与世界场景；Token 重启/断网/撤销及 test OTP 恢复通过 | `.artifacts/validation/backend-client/result.json`、`world.png` |
| WSL 资源服务 | 当前 Unity 发布包 14 文件 hash 一致；越界/目录/未知文件和 POST 拒绝 | `.artifacts/validation/backend-resources/test-result.json` |
| Test-WSLResources.ps1 | 2 张表 76 bytes；远程 prefab 实例化/释放和 scene 加载/卸载通过 | `.artifacts/validation/backend-resources/unity-download.json` |
| Native / NativePatch | 母包及同母包补丁运行通过；14 份 AOT metadata、8 模块、当前 4 张表；原生 DPAPI 缓存通过 | `.artifacts/validation/native-player.log`、`native-patch-result.json` |
| Test-WSLNativePatch.ps1 | 冷缓存 28 次下载、3 个世界 bundle；代码实际替换，GameAssembly SHA-256 不变 | `.artifacts/validation/backend-resources/native-wsl-result.json` |
| Test-Foundation.ps1 | 85/85；服务端启动通过 | 命令输出 |
| Test-Architecture.ps1 | SDK hash、12 程序集、依赖/模拟纯净/双引擎通过 | 命令输出 |
| Unity 回归 | 隔离 EditMode 46/46；连接图形编辑器的 PlayMode 55/55；Unity CLI 编译通过 | `.artifacts/validation/unity6/EditMode.xml`、`.artifacts/validation/client-demo/PlayMode.json` |

后端测试使用独立 test.env，不覆盖用户白名单，结束停止六宿主，继续联调需重新 Start。客户端测试临时备份原加密缓存，结束恢复缓存和编辑器场景；测试会重启本地六服务。

调试期间修复过 Overlay 生命周期、协程编译语法、WSL 路径/运行目录、MySQL 微秒精度和截图路径。远程测试首次误用桌面文件系统于 WebGL 目标而失败，切换该目标的 WebNetworkFileSystem 后通过；不把调试过程记为一次通过。

本轮原生复验曾因缺少 Windows IL2CPP 模块、导出 Visual Studio solution 设置和旧导出目录冲突失败；用户批准官方模块安装后，锁定可执行 Player 输出并隔离旧产物后通过。冷缓存测试首次移除整个内置目录，缺少 BuiltinCatalog 导致初始化失败，改为空清单后通过。Unity PlayMode 无图形模式出现 RenderTexture.Create 错误（33/55），在真实图形编辑器中复验 55/55；未屏蔽错误。新增共享战斗脚本的团结 GUID 使用既有 ExportGuids / Convert-PortableMetadata 工具处理后，Unity 编译恢复。测试使用当前工作树，包含用户尚未提交的四表/玩法内容；本轮提交不接管那些改动。

## 验收边界

经济/背包/任务/活动仅持久化命令入口，尚无正式结算；跨服为身份/路由/房间骨架，无多人 AOI/PvP。Windows 原生 HybridCLR 和 DPAPI 已在本轮复验；Unity 6 是 Editor 已编译玩法路径。真实短信、生产 TLS/限流/运维、微信安全存储/网络和真机未验收。Runtime/Editor 变更待平台/服务端评审，见 [ADR-0006](ADR-0006-Backend-Client-Session.md)。
