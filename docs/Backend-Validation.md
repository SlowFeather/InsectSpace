# 后端与本地客户端验收

日期：2026-10-05。当前状态取代此前 Windows .NET 宿主方案：**MySQL、Redis、六个宿主均在 WSL2 Ubuntu-24.04 原生运行**。Windows 仅负责 Unity 客户端、构建和脚本。范围见 [Backend-Requirements.md](Backend-Requirements.md)。

## 启动

仓库根目录使用 PowerShell 7、.NET 10 SDK、WSL2 Ubuntu-24.04。客户端要求已打开 Unity 6000.6.3f1 工程且 Unity CLI/Pipeline 可连接；先保存场景、停止 Play。

```powershell
./tools/Initialize-WSLBackend.ps1
./tools/Start-BackendServices.ps1 -Build
./tools/Start-BackendClient.ps1
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
# 复用 .artifacts/yoo/WebGL 下现有 Core / WorldCommon foundation-001
./tools/Manage-WSLResources.ps1 -Action Start
./tools/Manage-WSLResources.ps1 -Action Test
./tools/Test-WSLResources.ps1
./tools/Manage-WSLResources.ps1 -Action Stop
```

可指定 `-SourceRoot <目标发布根目录> -Version <版本> -Port 18088`。服务只监听 127.0.0.1，允许发布清单文件 GET/HEAD，拒绝仓库、配置、目录列表及写入访问。复制到 WSL ext4 内容 hash 目录，校验发布内容未改变。

Unity 下载测试使用远程唯一文件系统、禁用 Web 缓存，读取 Core.version、manifest 与两张 RawBundle 表。当前 WebGL 按需下载，预下载数为 0，实际远程读取 2 张表共 76 bytes。WorldCommon 三份 bundle 已远程 SHA-256 验证；UI 世界场景加载使用 Editor simulation。**远程读取与场景链路分别验证，不声称同一原生热更运行已通过。**

## 已执行

| 检查 | 实际结果 | 本机证据 |
| --- | --- | --- |
| Linux 自包含宿主 | 六个 Linux/PID/MySQL/Redis health 通过 | `.artifacts/validation/backend/services/` |
| Test-Backend.ps1 | 白名单 4 形式、非白名单拒绝、OTP 防重放、持久化、30 天/2 小时、Token 重启/固定到期/撤销/过期、路由/房间/inbox 幂等通过 | `.artifacts/validation/backend/test-result.json` |
| Test-BackendClient.ps1 | 9 项：白名单、世界资源、Token 重启、断网缓存/恢复、退出清理、撤销拒绝、test OTP 和后续免短信 | `.artifacts/validation/backend-client/result.json`、`world.png` |
| WSL 资源服务 | 11 文件 hash 一致；越界/目录/未知文件和 POST 拒绝 | `.artifacts/validation/backend-resources/test-result.json` |
| Test-WSLResources.ps1 | foundation-001，2 张表 76 bytes，进度 100% | `.artifacts/validation/backend-resources/unity-download.json` |
| Test-Foundation.ps1 | 85/85；服务端启动通过 | 命令输出 |
| Test-Architecture.ps1 | SDK hash、12 程序集、依赖/模拟纯净/双引擎通过 | 命令输出 |
| Test-ClientDemo.ps1 -Mode All -Filter InsectSpace. | EditMode 46/46、PlayMode 55/55；编译 0 error / 0 warning | `.artifacts/validation/client-demo/` |

后端测试使用独立 test.env，不覆盖用户白名单，结束停止六宿主，继续联调需重新 Start。客户端测试临时备份原加密缓存，结束恢复缓存和编辑器场景；测试会重启本地六服务。

调试期间修复过 Overlay 生命周期、协程编译语法、WSL 路径/运行目录、MySQL 微秒精度和截图路径。远程测试首次误用桌面文件系统于 WebGL 目标而失败，切换该目标的 WebNetworkFileSystem 后通过；不把调试过程记为一次通过。

## 验收边界

经济/背包/任务/活动仅持久化命令入口，尚无正式结算；跨服为身份/路由/房间骨架，无多人 AOI/PvP。真实短信、生产 TLS/限流/运维、原生 HybridCLR、微信安全存储/网络和真机未验收。Runtime 变更待平台/服务端评审，见 [ADR-0006](ADR-0006-Backend-Client-Session.md)。
