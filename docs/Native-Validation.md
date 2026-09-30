# 原生与远程补丁验证

日期：2026-09-30。此工作流验证 **Windows IL2CPP**，不是微信原生导出或真机认证。

## 复现

```powershell
./tools/Invoke-Unity.ps1 -Action Native -TimeoutSeconds 1800
./tools/Invoke-Unity.ps1 -Action NativePatch
```

第一步在 `.artifacts/unity/ValidationClient` 中安装项目本地工具链，生成裁剪 AOT 元数据、桥接及泛型引用，编译 Gameplay，构建 Core/WorldCommon，生成 Development Player，并启动该 Player 验证。Player 中 `editorSimulate`、`localSmokeMode` 均关闭；会话保持 SignedOut，不伪装成登录。

第二步只编译带 `INSECTSPACE_NATIVE_PATCH` 验证标记的 Gameplay 并构建新 Core 包，不调用母包构建。临时 HTTP 服务仅监听 `127.0.0.1`。同一个 Player 冷启动后发现新版本、下载变更资源、注入元数据、执行新 DLL，并再次验证预制体/场景生命周期。服务和 Player 都会在脚本结束或失败时退出。

本机 HTTP 配置只接受 Windows Development IL2CPP Player 的显式验证参数；普通编辑器/生产配置仍要求 HTTPS，fallback 地址也执行同样校验。

## 固定来源

`Install-NativeToolchain.ps1` 使用 HybridCLR 8.5.0 包自身的 2022-tuanjie 对应版本，并核对 Git 提交：

| 源 | 版本 | 提交 |
| --- | --- | --- |
| focus-creative-games/hybridclr | v8.5.0 | f67c0de1b5f1a8cc844807fab4c92a03b3a3cb63 |
| focus-creative-games/il2cpp_plus | v2022-tuanjie-8.3.0 | ea1bec1fbd5e7585e59fc98486821cd0d398751c |

源 checkout 保持 pristine，在独立 overlay 中组装，再由 SDK Installer 复制到验证工程的 `HybridCLRData/LocalIl2CppData-WindowsEditor`。不修改安装目录、不替换参考项目工具链。

## 防误用

- `Native` 只允许固定隔离工程、团结 2022.3.62t16、Windows64 和 Development 输出。
- 原生母包记录框架 AOT C#、asmdef、link.xml 和 SDK lock 的源码指纹；`NativePatch` 检测到变化时要求先重建母包。
- 补丁构建前后、运行前后核对原生 `GameAssembly.dll` SHA；不以重新构建母包假装代码热更成功。
- Loader 在注入前确认 Gameplay 未预装入 Player，并校验母包标识、目标、协议及每份 payload SHA。
- AOT 元数据先于 Gameplay 注入；一旦开始注入，失败或版本切换必须重启进程。
- `playerBuildId` 是兼容标识，不是密码、签名或权限证明。生产 CI 仍需要签名、审批、防回滚和兼容矩阵。

## 证据

`.artifacts/validation/unity-Native.log`、`native-player.log`、`unity-NativePatch.log`、`native-patch-player.log`、`native-patch-result.json`。

成功标记包括 `NATIVE_CODE_LOADED`、`FOUNDATION_READY modules=8 tables=2`、`CONTENT_LIFECYCLE_PASSED`、`NATIVE_VALIDATION_PASSED`；补丁日志还须包含 `GAMEPLAY_REVISION native-patch-002` 和实际发现的新版本。

验证使用 `-nographics`。Null 图形设备下的 shader 不支持日志不能作为画面质量或 GPU 兼容结论；本脚本验证的是运行时与内容链路。真实渲染和微信真机仍需独立验收。
