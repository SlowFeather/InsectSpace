# 代码、资源与配置链路

## 版本基线

| 组件 | 锁定基线 |
| --- | --- |
| 引擎 | Tuanjie 1.10.4 / 2022.3.62t16 |
| GF / 确定性 / KCP 适配 / Unity 组件 | 1.0.0-alpha.7 |
| HybridCLR UPM | 8.5.0 |
| YooAsset | 3.0.5 |
| Luban | 4.5.0，cs-bin + bin |
| Luban ByteBuf | 参考项目中 MIT 运行时快照，保留 LICENSE.txt |
| URP | 原工程 14.2.0-t1，不降级参考工程的 12.x |

这些是实际锁定的项目基线，不表示最新版本或团结微信目标已获得兼容认证。UPM 包可导入/可编译不等于 native IL2CPP 运行时适配完成。

三个确定性包的实际 UPM 版本为 `1.0.0-alpha.7.insectspace.1`：只补齐原包缺失的 `.meta`，避免团结刷新时的 immutable package 错误；核心 DLL 校验和不变。原始档案仍在 vendor，修复过程与来源记录在 `Repair-SdkPackaging.ps1` 和 `packaging-overrides.json`，不改参考工程。

## 导表

输入为 `design/luban/Defines` 与 `Tables`，目前只有场景和画质两张基础表，不提前设计玩法数值。Luban 本身支持其他策划输入格式；后续切换 Excel 时仍以相同 schema 为约束。

`Build-Tables.ps1` 用真正的 Luban 执行客户端和服务端生成。生成类型只进入热更程序集；`ByteBuf` 留在独立 AOT 程序集。服务端也使用对应生成代码，脚本比较二进制表 SHA，防止客户端和服务端读到不同数据。

不得手改生成文件。新增表后同步更新 bootstrap 要预加载的基础表列表；区域/章节表不应全部放入首包，而应按包异步加载。

## YooAsset

已建立 `Core` RawFile 包：`Content/Data` 收集表；`Content/Code` 收集热更 DLL、代码 manifest 与 AOT metadata。运行时通过 YooAsset 3 的 `LoadAssetAsync<RawFileObject>` 读取，句柄在 finally 释放。

编辑器使用真实的 EditorSimulate 构建与 manifest。Offline、Host、Web 分支使用对应文件系统；微信生产缓存文件系统需要结合选定 SDK 另行适配，不能将通用 Web 分支直接认定为已验证微信实现。

Host/Web 启动在 `discoverRemoteVersion=true` 时通过 YooAsset 请求一次 Core 版本，然后锁定同一 manifest 的代码和表。关闭该开关时仍可显式使用 `packageVersion`。已经注入代码后不能在当前进程替换版本，必须完整重启。桌面 Host 的发现、下载、缓存复用及新 DLL 执行已有真实验证；Web/微信不能据此推定通过。

`code_manifest.json` 同时绑定 `playerBuildId`、目标、协议和 `contentPackages`。常规资源包不独立追踪 latest，而是按当前 Core 的清单按需准备。当前 `WorldCommon` 用 AssetBundle/LZ4 收集 `WorldActor.prefab` 与 `WorldSandbox.scene`。资源所有权和接入示例见 [Resource-Ownership.md](Resource-Ownership.md)。

`Build Core Resource Package` 可独立验证 RawFile 打包。只有通过代码 manifest、目标平台、裁剪 AOT 元数据和平台审核门禁的完整包才是可发布版本。示例版本名 `foundation-001` 仅用于本地验证；生产版本必须不可变，禁止覆盖 CDN 同名已发内容。

`Compile Hot Update DLL` 会撤销旧 `code_manifest.json`，防止保留旧 SHA 后混装新 DLL。因此 `Artifacts` 只验证托管编译和资源打包，不产生获准发布的 Player 包；需要执行目标匹配的 Stage，再构建 Core。`Native`/`NativePatch` 已按正确顺序完成这些步骤。

批量 `Artifacts` 验证每次使用独立 `validation-*` 输出根目录，支持重复执行且不覆盖以前的包。编辑器菜单正常打包保留同版本目录已存在就拒绝的行为；生产发包必须显式升级版本。

## HybridCLR

编辑器直接获取已编译的 `InsectSpace.Gameplay.HotUpdate`，通过反射 `HotUpdateEntry.Create` 进入业务。`Compile Hot Update DLL` 使用 HybridCLR 编译工具生成目标 DLL，复制成 `.dll.bytes`。

Player 路径准备所有代码 payload，逐个做 SHA 校验，先补充 AOT metadata，再加载热更 DLL。部分注入失败不允许重试。`Stage Matching Player Metadata` 必须使用同一目标、同一次母包构建的裁剪 AOT 程序集；它不会自动安装或篡改引擎原生文件。

新增 `InsectSpace > Build > Prepare Native AOT Metadata` 调用 SDK 的 `GenerateAll`，要求已经安装项目本地工具链并选择 IL2CPP，不会自动修改全局引擎。该流程先构建裁剪 AOT DLL，再生成桥接和泛型引用，然后才能执行 Stage。

`ReleaseGate` 区分普通 Player 与 SDK 中间构建：只有框架准备作用域、scripts-only 标志和与目标匹配的 `HybridCLRData/StrippedAOTDllsTempProj/<target>` 输出路径同时成立，才不要求尚未生成的 code manifest。作用域仅由上述 InsectSpace 菜单/对应 CLI 方法开启，并在 finally 中关闭；直接使用 SDK 的 Generate 菜单会提示改用此入口。路径前缀伪装、上级目录跳转、其他平台和普通 Player 输出均不能走这个分支。Windows IL2CPP 原生运行、MiniGame 原生生成及微信 SDK 转换均已执行；微信环境中的冷启和远程更新仍需单独验证。

`NativeValidationBuild` 另有严格限定到隔离工程、固定 Windows64 输出路径、Development 构建及内部作用域的验证入口。它不制造平台审批文件，不放行普通发布。原生工具链来源、复现和边界见 [Native-Validation.md](Native-Validation.md)。

当前构建工具对 Windows/Android/iOS/WebGL 提供 runtime-platform 映射，团结专用小游戏枚举必须经适配组确认后显式加入。未知目标主动失败。不要直接运行社区 installer 替换团结发行版引擎的 libil2cpp。

## 生产发布待办

当前运行时 SHA 校验只能发现内容损坏；同源 manifest 不是可信签名。生产必须实现可信启动 manifest 的签名校验/防回滚，绑定母包 build ID、平台、ABI、协议、configHash、mapHash、热更 DLL 与资源版本。

发布顺序：母包工具链确认 → 原生桥接与 AOT 生成 → 热更 DLL → 客服端/服务端同版 Luban → 资源构建 → CI 契约检查 → 签名发布描述 → 上传不可变对象 → 灰度渠道指针 → 真机热启/冷启/弱网/回滚验收。

回滚用上一整套匹配版本，不在已注入的进程内卸载 DLL。AOT 变动走母包发布；业务补丁只能使用母包已支持的 API。代码和表不能分别追踪任意 latest。
