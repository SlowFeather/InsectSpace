# 资源包与句柄所有权

## 分层

`Core` 是 RawFile 包，包含同版代码、AOT 元数据、代码清单和 Luban 表。`WorldCommon` 是普通 AssetBundle 包，包含可替换的灰盒预制体和附加场景。后续区域/章节按同样规则增加独立包，不把全部地图塞进 Core。

编辑器的包版本来自 `InsectSpaceBoot.json`。Player 的内容包版本来自已校验的 Core `code_manifest.json`，只绑定一次；不允许运行中任意改版本。新增包应同时更新配置、collector、构建产物和 Core 清单。

CDN 必须提供 Core 引用的全部内容包清单与文件，即使部分 bundle 已在 StreamingAssets 中。当前 Host 路径会向远端校验内容包 hash/manifest；只上传 Core 会使按需内容包准备失败。本机补丁服务复用母包的 WorldCommon 发布文件，验证的不是新地图内容发布。

## 模块接口

业务模块从 `BootContext.Resources` 获取服务：

```csharp
yield return context.Resources.PrepareContentPackage("WorldCommon");
var handle = context.Resources.LoadAsset<UnityEngine.GameObject>("WorldCommon", "WorldActor");
yield return handle;
if (handle.Status != YooAsset.EOperationStatus.Succeeded)
    throw new System.InvalidOperationException(handle.Error);
var instance = UnityEngine.Object.Instantiate(handle.GetAssetObject<UnityEngine.GameObject>());
// Keep the handle while the instance or pool still uses its assets.
UnityEngine.Object.Destroy(instance);
yield return null;
handle.Release();
```

实际业务应在异常/取消路径清理实例和句柄。服务返回原生 YooAsset handle，不额外仿造一套异步或资源计数系统。不要在实例尚存时提前 Release；对象池保留期间也必须持有资源句柄。

场景使用 `LoadScene(package, address)`，当前仅支持 Additive，以保留 Bootstrap。等待返回的 `SceneHandle` 完成，按需要调用 `ActivateScene`；离开后等待 `UnloadSceneAsync()`，成功后 YooAsset 自动释放场景 handle。不要用普通 AssetHandle 的 Release 代替场景卸载。

同一个包的并发 Prepare 会等待同一次准备结果。失败/中断会保留失败状态，不以半准备状态放行加载。所有操作都在 Unity 宿主线程使用。关闭框架前，模块必须先完成场景卸载、销毁表现和释放句柄；`YooResourceService.Dispose` 是最终全局回收，不是正常换图 API。

## 平台接口

平台 AOT 程序集在 Bootstrap 之前调用 `PlatformServices.Install(IClientPlatformAdapter)`，提供网络 factory、地址解析器和 YooAsset 初始化文件系统。只能安装一次，启动后不可替换。微信组在这里注入选定 SDK 的缓存/下载适配，不需要修改 Loader 或 YooAsset vendor 包。

当前未提供实际微信 SDK 实现。默认小游戏网络 factory 仍 fail-closed；通用 Web 文件系统不代表已通过微信存储配额、弱网和前后台恢复测试。
