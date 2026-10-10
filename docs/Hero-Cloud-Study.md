# Unity 6 人物、积云与迷雾渲染研究

更新：2026-10-07。引擎：Unity 6000.6.3f1。范围：本地美术表现研究。

## 打开预览

先用 Unity 6 打开 `client/unity/InsectSpaceClient`，保存当前场景并停止 Play，再在仓库根目录执行：

```powershell
./tools/Start-HeroCloudStudy.ps1 -Preset Mist -Quality 1
./tools/Start-HeroCloudStudy.ps1 -Preset Day -Quality 1
./tools/Start-HeroCloudStudy.ps1 -AfkReference -Preset Day -Quality 1
```

默认场景是 `Assets/InsectSpace/Scenes/HeroCloudStudy.unity`；`-AfkReference` 打开本机忽略目录中的 `Assets/Temp/AFKStudy/AFKCloudStudy.unity`，需要已经导入的研究素材。右下角 CONTROLS 可切换 HERO / EXPLORE 机位、DAY / SUNSET / NIGHT / MIST、暂停人物和云动画、循环三档画质；草叶风动独立运行。`-OpenOnly` 只打开场景。启动脚本设置本地 Editor 的 1600×900 Game View 和此场景的 Play 起点，不修改项目发布场景列表。Game View 改为 9:16 或 3:4 时相机会自动重新构图。

## 参考与画风取舍

参考 `docs/参考`、用户提供的《剑与远征：启程》晴天人物截图及 Igor 迷雾截图：前者用于正常人物比例、蓝天暖白积云和青色云底；后者用于灰绿调、远景消隐和深色前景。默认场景保留授权来源的 Valerya。2026-10-07 根据用户授权，从 Root MuMu 中提取已下载商业素材，另建 AFKCloudStudy 作本地渲染对照；素材没有进入正式发布资源。`docs/参考/webwxgetvideo.mp4` 初次研究时为零字节，未据此声称分析了视频内容。

- 人物：替换为银发正常比例的 Valerya；海军蓝与金色护甲通过 Shader 调色，皮肤保留柔和明暗，盔甲使用分段光照。
- 天空：程序生成 4096×2048 积云图集，轮廓距离与明暗分开编码；层叠云冠、暖白受光面、青蓝暗部以及留白构图。根据用户进一步反馈，移除轮廓高频噪声，改用平滑距离场及约一个屏幕像素宽的边缘抗锯齿，保持轮廓清晰。
- 草地：程序化起伏地面、36 个草叶块、三档密度；用噪声控制疏密和冷暖草块，加入蜿蜒路径、花丛、石块、灌木及远树林。云影和接触阴影使用冷青色，草叶保留风动。
- 迷雾：灰蓝天空、低饱和灰绿草地、前景压暗与远景雾化；移除规则横向雾带，关闭可见积云和太阳盘，保留人物可读性。

Mist 使用线性距离雾与地表 Shader 的低处雾色混合，不是体积光线步进。它与 Day 共用相同网格、角色、相机和动画，仅改变环境参数及地表 MaterialPropertyBlock。

## 同条件交叉验证

当前 HERO 使用 39° 透视，目标点在人物根节点上方 1.12 米，横屏距离 4.45 米，竖屏按宽高比增加距离，保持完整人物。EXPLORE 使用 44° 斜俯视镜头。正式角色的三姿态以及 16:9、9:16、3:4 两种机位通过自动检查；AFK 角色另用实际蒙皮顶点探针验证。最新截图在 `.artifacts/validation/hero-cloud-study/2026-10-07/`。

![当前晴天人物机位](../.artifacts/validation/hero-cloud-study/2026-10-07/Hero-Day-Desktop.png)

以下同条件晴天/迷雾和云边图是 2026-10-02 的历史对照，保留用于追踪修复，不能作为当前机位截图。

![晴天与迷雾同机位对照](../.artifacts/validation/hero-cloud-study/Day-Mist-Comparison.png)

本地截图位于忽略版本管理的 `.artifacts/validation/hero-cloud-study/`，商业研究的捕获中间文件在 `Assets/Temp/`。

| 档位 | 草叶数量 | 草叶顶点 | 草叶三角形 |
| --- | ---: | ---: | ---: |
| 0 | 4,531 | 22,655 | 13,593 |
| 1 | 18,200 | 91,000 | 54,600 |
| 2 | 27,212 | 136,060 | 81,636 |

这些是默认场景草叶网格的总量，不含花、石块、灌木和商业研究新增草花，不是视锥剔除后的实际提交量，也不是性能测量。低档沿用项目的画质行为，人物地面阴影有所减弱。

观察结果：迷雾下人物轮廓、脸部和服装仍可辨认，远处草色融入天际；晴天的草地不再呈规则平行草带。当前实现使用本项目光照和 Shader，不是对原游戏材质或渲染管线的完整还原。

云边修复前后使用同机位、同姿态和零云相位捕获。`Cloud-Edge-Comparison.png` 为截图原像素的 200% 最近邻裁切，不进行图像平滑修饰。修复移除了高频噪声对云外轮廓的扰动；在 Shader 中用屏幕导数重建边缘覆盖率。图集改为无压缩与三线性采样，避免块压缩改变轮廓；当前 Editor 返回 RGB24，Standalone 配置覆盖为 RGBA32。取消压缩会增加内存成本，本轮未测量显存占用。移动端仍保留 2048 ASTC 6×6 覆盖，尚未验证其边缘和性能。

![云边修复前后，200% 原像素对照](../.artifacts/validation/hero-cloud-study/Cloud-Edge-Comparison.png)

## 人物来源与骨骼

人物为 [Valerya – Fantasy Queen / Stylized 3D Character](https://sketchfab.com/3d-models/valerya-fantasy-queen-stylized-3d-character-271fa806eeac44d5a0c03101d7ce2096)，作者 agra_aoe / AoG.01，原始模型与贴图为 CC BY 4.0。原作者披露使用 AI 辅助制作。原始 GLB 共 54,074 三角形，没有骨骼和动画。来源、哈希与修改说明见 `Assets/InsectSpace/Rendering/HeroStudy/Valerya/ATTRIBUTION.json`。

按用户建议，已实际在 Mixamo 上传源 T-pose，完成自动绑定，再下载 Breathing Idle（FBX Binary、With Skin、30 fps）。Unity 使用有效 Humanoid、7 个 SkinnedMeshRenderer、9.933333 秒循环动画；`globalScale=100` 修正导出单位，不启用根运动。Mixamo 绑定与动画按其适用条款使用，不把它们标成原模型的 CC BY 内容。早期静态 relaxed OBJ 保留为中间研究资产，最终场景使用 `Valerya_BreathingIdle.fbx`。

验证覆盖了实际 BakeMesh 顶点变化、根位置不漂移和跨循环边界播放。头发及盔甲自动权重目前只检查了待机，未验收战斗、夸张动作、布料、面部绑定或量产适用性。

## 复建入口

保留已下载的 FBX 和七张 albedo 即可重建场景。在编辑器停止 Play、场景已保存时执行：

```powershell
$p = [IO.Path]::GetFullPath('client/unity/InsectSpaceClient')
unity command run_script --file D:/Project/InsectSpace/AgentScripts/BuildHeroStudy.cs --entry BuildHeroStudy.Run --timeout_ms 180000 --timeout 180 --project-path $p --format json
```

这会重新生成该研究的场景、材质、云图集和草地网格，覆盖此研究内的手工调参。先保存需要保留的版本；既有 MeadowShowcase / SkyShowcase 不属于此脚本目标。只刷新云图集时，入口改为 `BuildHeroStudy.RebuildCloudAtlas`，无需重建场景。

如需重新准备自动绑定输入，先把原始 GLB 放在 `.artifacts/reference/valerya/source/valerya.glb`，再执行：

```powershell
python tools/art/Convert-Valerya.py --mixamo
```

输出 `.artifacts/reference/valerya/Valerya_Mixamo.zip`。重复执行安全，不再用重命名覆盖现有文件；ZIP 明确只包含一个 T-pose OBJ、MTL 与七张贴图。默认静态研究输出同样位于 `.artifacts`，`--unity-albedos` 可显式复制源 albedo 到研究资产目录。转换器不覆盖最终 FBX 的来源记录，也不会自动调用 Mixamo。

## MuMu 商业素材本地对照

Faye 使用原始 FBX 骨骼与动画，有效 Generic Avatar、7 个 SkinnedMeshRenderer；待机为 1.633333 秒循环，导出的 walk_loop 为 1.1 秒，当前控制器只播放待机。导入尺度为 100，不应用根运动。身体与武器使用原始 albedo，关闭正式角色专用的银发/海军蓝调色；玻璃和原始阴影片单独处理。草和花采用原始网格，散布在本项目草地中；花朵在颜色、深度和阴影 pass 中一致裁剪 alpha。

![AFK 素材晴天研究](../.artifacts/validation/hero-cloud-study/2026-10-07/AFK-Hero-Day-Desktop.png)

复建入口为 `AgentScripts/BuildAfkStudy.cs` 的 `BuildAfkStudy.Run`，调用方式与默认场景生成器相同。验证入口为 `AgentScripts/VerifyAfkStudy.cs` 的 `VerifyAfkStudy.Run`，需要在 AFKCloudStudy Play 模式运行。探针检查骨骼、贴图、Shader、真实蒙皮变形、根不漂移、循环及六种构图组合；结果保存为日期目录下 `afk-probe.json`。

全部提取内容在忽略目录 `.artifacts/reference/afk-journey/all/`。已拉取 6,133 个 LPak、10,792,623,333 bytes；切出 214,972 个 UnityFS 及 26,149 个非 UnityFS 条目，后者完整保留在 `non-unity/`，没有被误称为损坏。目录中 2,242 个包尚未下载，不声称已取得服务器完整资源库。原包、切片清单和未下载清单均保留。

AssetStudio GUI 位于 `D:/APP/AssetStudio-v0.16.47/`；批处理使用 [AssetStudioMod v0.19.0](https://github.com/aelurum/AssetStudioMod/releases/tag/v0.19.0)，安装在 `D:/APP/AssetStudioModCLI-v0.19.0/`。ZIP 的 SHA-256 为 `CAD5B9E2084DDA85007012F94C6D3AD1944BA153796C4E80027792D0BA7BE285`。UnityFS 版本头为 0.0.0，解析显式指定 2021.3.48f1。每批最多 2,048 个文件或 96 MiB，保留相对路径；用对象 ID 命名以避开 Windows 路径长度限制。

完整原始对象保存在 `packs/` 和 `bundles/` 中；`raw/batch-v3-*/assets.xml` 索引全部对象，早期批次还包含单独的原始对象副本。`converted/` 提供可转换的 PNG、OBJ、JSON 等，`assets.csv` / `assets.sqlite` 保存原名称、类型、路径 ID 和源文件。完整批次结果以本地 `export-result.json` 和 `status/` 为准；FBX 必须连同依赖导出，普通 OBJ 不保留骨骼。保留原包并不等同于已生成全部可用 FBX。商业素材署名为 AFK Journey / Lilith Games，本地对照不授予项目发布授权。

## 实现与验证边界

主要实现位于 `Rendering/HeroStudy/`，场景生成器为 `AgentScripts/BuildHeroStudy.cs`。共享天空控制器只新增可选雾配置及末尾 Mist 枚举值；既有三预设场景继续使用原始配置。天空、雾和地表材质参数具有场景切换/禁用恢复检查，运行时不改写共享材质资产。

2026-10-07 验证：Foundation 85/85、Architecture 通过、Unity EditMode 46/46、完整 PlayMode 56/56；alpha pass 修正后专项 HeroCloudStudy 5/5。首次完整 PlayMode 为 55/56，角色 Renderer.bounds 包含动画预留空间，改用 BakeMesh 的实际姿态范围后通过。三个专用 Shader 编译诊断均为空。AFK 探针与横竖屏截图单独记录。详见 [实际验证记录](Validation.md)。

没有修改平台 Runtime/Editor、SDK 或全项目渲染管线设置；未进行团结、WebGL、移动设备帧率/显存、微信真机或发布验收。本研究可用于继续比较画风，不能作为这些平台的性能结论。

## 2026-10-08：AFK 研究场景收尾复验

- `unity recompile` 使用 Unity **6000.6.3f1** 通过，0 error / 0 warning；`tools/Test-Architecture.ps1` 通过，`tools/Test-Foundation.ps1` 为 **85/85**。
- `tools/Test-ClientDemo.ps1 -Mode PlayMode -Filter InsectSpace.Tests.HeroCloudStudyTests` 为 **5/5**。
- 重新运行 `VerifyAfkStudy.Run`：Generic Avatar 有效，待机 **1.63333344 秒**，7 个蒙皮 Renderer，`maxDeformationSquared=0.04877426`，根节点不漂移；Hero/Explore × 16:9、9:16、3:4 六种构图全部在视口内。
- 最新 AFK Explore 截图：`Assets/Temp/AFKStudy/AFK-Explore-Adjusted-Desktop-3.png`（1600×900）和 `Assets/Temp/AFKStudy/AFK-Explore-Adjusted-Portrait-3.png`（900×1600）。这些文件属于本地忽略目录，作为研究证据，不进入正式发布资源。

本次确认的是 Unity 编辑器中的本地表现研究，不包含 MuMu 注入、移动端帧率/显存、完整地图资源恢复、商业授权或微信真机发布验收。
