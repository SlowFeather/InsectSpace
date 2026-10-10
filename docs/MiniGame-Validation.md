# MiniGame 原生验证

日期：2026-09-30。引擎：Tuanjie 1.10.4 / 2022.3.62t16。

## 已证实的范围

- 使用真正的 `BuildTarget.MiniGame`，不是把 Windows 或普通 WebGL 编译重命名为小游戏。
- 项目内 HybridCLR 8.5.0 工具链完成 AOT 裁剪、桥接和原生 WASM 导出。
- `Use Slim Format For global-metadata.dat` 关闭；不修改全局编辑器。
- Core 中包含 14 份目标匹配的 AOT 元数据、独立 Gameplay DLL 和两张 Luban 表。
- WorldCommon 包含 MiniGame 目标的预制体/场景 bundle。
- 原始导出在本机 Chrome 中实际运行，日志确认 `NATIVE_CODE_LOADED aot=14` 和
  `FOUNDATION_READY modules=8 tables=4`；斜俯视灰盒正常显示。

**浏览器运行原始 MiniGame WASM 不等同于微信小游戏运行。** 官方微信 SDK 转换现已单独通过；
开发者工具、真机网络、平台缓存、前后台恢复和微信同母包远程补丁仍须单独验收。
本次浏览器检查只证明启动与代码/表加载，未外推为该目标的完整内容生命周期或热更验证。

## 复现与产物

```powershell
./tools/Invoke-Unity.ps1 -Action MiniGameNative -TimeoutSeconds 1800
./tools/Test-MiniGameExport.ps1
```

输出：`.artifacts/unity/ValidationClient/HybridCLRData/MiniGameValidation/Raw`。

`Test-MiniGameExport` 检查 WASM 头、data/loader/framework 文件、两包版本与内置 catalog、
所有 catalog 文件的存在性、代码清单目标/母包标识、逐 DLL SHA-256 以及内容版本绑定。
它是导出完整性检查，不模拟引擎运行，也不是安全签名验证。

证据：

- `.artifacts/validation/unity-MiniGameNative.log`
- `.artifacts/validation/minigame-export-result.json`
- `.artifacts/validation/minigame-browser-console.log`
- `output/playwright/minigame-foundation.png`

## 资源适配

MiniGame 的实际编译宏包含 `UNITY_WEBGL`。因此不能使用 YooAsset 的桌面
`BuiltinFileSystem` 或 `SandboxFileSystem`；原始运行检查曾捕获此错误，现已修正。
Web Player 的离线模式使用 `WebServerFileSystem`，只请求随导出部署的 StreamingAssets；
Host/Web 模式还会配置 `WebNetworkFileSystem`，继续使用绑定的 Core/内容版本。
“离线”在这里指资源随包部署，不表示浏览器可以通过 `file://` 读取任意文件。

微信 SDK 应关闭 Unity 自身的 Web Cache，并明确使用 SDK 的网络和缓存适配；
不能把浏览器 IndexedDB 的验证记录当作微信文件系统验证记录。

## 微信 SDK 转换

```powershell
./tools/Invoke-Unity.ps1 -Action WeChatNative -TimeoutSeconds 1800
./tools/Test-WeChatExport.ps1
```

已验证官方 SDK changelog 0.1.34、固定提交 `a09d4b29daa1dd8358b09b5b5639554ab08cfdc2`。
UPM 清单自身标记 0.1.1，因此以提交和归档 SHA 识别版本。SDK 0.1.32 曾因缺少
`WX_SyncFunction_tnnt` 原生 JS 桥符号失败；保留失败记录，没有修改 SDK DLL 或关闭链接检查。

微信构建显式启用 WebGL 2，保留 URP 线性色彩空间，关闭 slim metadata，并按 SDK 的
绝对/相对路径字段配置导出。输出在 `MiniGameValidation/WeChat`：

- `minigame`：转换后的微信开发者工具工程、WASM/首资源子包。
- `webgl/StreamingAssets`：需要按项目 CDN 根目录部署的资源。
- `export-receipt.json`：SDK/引擎与 AppID/CDN 是否已配置的记录，不包含密钥。

校验确认 JSON 可解析、两个子包存在、代码和资源版本匹配，并将 Brotli WASM 解压后的 SHA-256
与原生 WASM 比较。结果见 `unity-WeChatNative.log`、`wechat-player-export-result.json`、
`wechat-export-result.json`。本次 `appIdConfigured=false`、`resourceCdnConfigured=false`，
所以它是**通过转换与文件校验的未配置工程**，没有声称微信 SDK 已在开发者工具/真机成功启动。

## 门禁

原生验证仅允许固定隔离工程、Development、MiniGame 目标、指定输出路径和调用作用域。
普通 Player 仍要求平台审批；本工作流没有生成或伪造 `InsectSpacePlatformApproval.json`。
编辑器默认 LOCAL SMOKE 保持不变，只有隔离验证副本关闭 smoke。
