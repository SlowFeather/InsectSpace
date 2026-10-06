# InsectSpace 当前工程状态

更新时间：2026-10-06。当前状态对应分支 `codex/wsl-backend-client`。

## 已验证

- WSL2 Ubuntu-24.04 原生运行 MySQL、Redis，以及 Gateway、Identity、Lobby、World、Battle、Worker 六个后端进程。
- 手机号白名单免验证码登录；非白名单使用明确标记的 dev/test OTP。首次登录后使用本机受保护 Token，默认有效 30 天，可由 `INSECTSPACE_SESSION_HOURS` 配置。
- Unity 6 客户端完成登录、大厅、WorldCommon 场景加载、Token 恢复、断网保留与重试、退出撤销，以及 test OTP 后的 Token 登录。
- YooAsset / HybridCLR 资源由 WSL2 只读宿主提供，已验证版本、hash、Core 表、WorldCommon 场景和冷缓存下载。
- 团结 Windows IL2CPP 母包及同母包原生补丁已运行；新代码从 WSL2 下载并加载，母包 `GameAssembly.dll` 保持不变。

## 验证证据

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| Foundation | 85/85 | `.artifacts/validation/backend-foundation-final.log` |
| Architecture | 通过 | `.artifacts/validation/backend-architecture-final.log` |
| Unity 6 EditMode | 46/46 | `.artifacts/validation/unity6/EditMode.xml` |
| Unity 6 图形 PlayMode | 55/55 | `.artifacts/validation/client-demo/PlayMode.json` |
| 后端客户端闭环 | 9 项 | `.artifacts/validation/backend-client/result.json` |
| WSL 资源生命周期 | 通过 | `.artifacts/validation/backend-resources/unity-download.json` |
| WSL 原生补丁 | 28 次下载、3 个 WorldCommon bundle | `.artifacts/validation/backend-resources/native-wsl-result.json` |

详细启动命令、配置边界和复验步骤见 [后端与本地客户端验收](Backend-Validation.md)；需求基线见 [WSL2 后端与客户端需求](Backend-Requirements.md)；完整历史记录见 [验证记录](Validation.md)。

## 当前边界

本阶段交付的是后端骨架、契约和本地闭环。正式经济、背包、任务、活动结算，多人 AOI，跨服迁移，PvP 帧同步，真实短信供应商，微信安全存储、网络和真机发布仍未验收。Editor 远程资源测试使用已编译玩法，不代表 Unity 6 原生 HybridCLR 已完成。

白名单号码、密钥、Token 和本机环境文件只保存在 Git 忽略配置中，不写入代码、文档、日志或 GitHub。

## 本地启动

```powershell
./tools/Initialize-WSLBackend.ps1
./tools/Start-BackendServices.ps1 -Build
./tools/Build-WSLClientResources.ps1 -StartHost
./tools/Start-BackendClient.ps1 -RemoteResources
```

停止客户端并恢复编辑器状态：

```powershell
./tools/Start-BackendClient.ps1 -Stop
./tools/Stop-BackendServices.ps1
```
