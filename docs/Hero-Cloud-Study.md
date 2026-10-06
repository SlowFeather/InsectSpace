# Unity 6 人物、积云与迷雾渲染研究

日期：2026-10-02。引擎：Unity 6000.6.3f1。范围：本地美术表现研究。

## 打开预览

先用 Unity 6 打开 `client/unity/InsectSpaceClient`，保存当前场景并停止 Play，再在仓库根目录执行：

```powershell
./tools/Start-HeroCloudStudy.ps1 -Preset Mist -Quality 1
./tools/Start-HeroCloudStudy.ps1 -Preset Day -Quality 1
```

场景是 `Assets/InsectSpace/Scenes/HeroCloudStudy.unity`。右下角 CONTROLS 可切换 DAY / SUNSET / NIGHT / MIST、暂停人物和云动画、循环三档画质；草叶风动独立运行。`-OpenOnly` 只打开场景。启动脚本设置本地 Editor 的 1600×900 Game View 和此场景的 Play 起点，不修改项目发布场景列表。

## 参考与画风取舍

参考用户提供的《剑与远征：启程》晴天人物截图及 Igor 迷雾截图：前者用于正常人物比例、银发、蓝天暖白积云和青色云底；后者用于灰绿调、远景消隐、深色前景和横向草地色带。没有提取商业游戏角色、贴图或界面作为本场景资源。`docs/参考/webwxgetvideo.mp4` 当时为零字节，未声称分析了视频内容。

- 人物：替换为银发正常比例的 Valerya；海军蓝与金色护甲通过 Shader 调色，皮肤保留柔和明暗，盔甲使用分段光照。
- 天空：程序生成 4096×2048 积云图集，轮廓距离与明暗分开编码；层叠云冠、暖白受光面、青蓝暗部以及留白构图。根据用户进一步反馈，移除轮廓高频噪声，改用平滑距离场及约一个屏幕像素宽的边缘抗锯齿，保持轮廓清晰。
- 草地：程序化起伏地面、36 个草叶块、三档密度；顶点色提供成片冷暖变化，Shader 提供风动。
- 迷雾：灰蓝天空、低饱和灰绿草地、前景压暗与远景雾化；关闭可见积云和太阳盘，保留人物可读性。

Mist 使用线性距离雾与地表 Shader 的低处雾色混合，不是体积光线步进。它与 Day 共用相同网格、角色、相机和动画，仅改变环境参数及地表 MaterialPropertyBlock。

## 同条件交叉验证

对照捕获固定为 1600×900，机位 `(0, 1.31, -2.62)`，垂直视场角 39°，人物 BreathingIdle 归一化时间 0.18；捕获期间冻结人物、云相位和场景时间，之后恢复实时预览。

![晴天与迷雾同机位对照](../.artifacts/validation/hero-cloud-study/Day-Mist-Comparison.png)

本地对照图位于忽略版本管理的 `.artifacts/validation/hero-cloud-study/`。原始截图保存在 `client/unity/InsectSpaceClient/Assets/Screenshots/HeroCloud_{Day|Mist}_Q{0|1|2}.png`。

| 档位 | 草叶数量 | 草叶顶点 | 草叶三角形 |
| --- | ---: | ---: | ---: |
| 0 | 21,024 | 105,120 | 63,072 |
| 1 | 42,012 | 210,060 | 126,036 |
| 2 | 63,000 | 315,000 | 189,000 |

这些是场景所选草叶网格的总量，不是视锥剔除后的实际提交量，也不是性能测量。三档均检查了真实截图；低档沿用项目的画质行为，人物地面阴影有所减弱。

观察结果：迷雾下银发、脸部和深蓝护甲仍可辨认；远处草色融入天际，前景保留深浅草带。与参考相比，当前草叶仍偏细、草带偏规则，云块构图和衣甲材质仍可继续美术精修。没有宣称与原游戏画面等同。

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

## 实现与验证边界

主要实现位于 `Rendering/HeroStudy/`，场景生成器为 `AgentScripts/BuildHeroStudy.cs`。共享天空控制器只新增可选雾配置及末尾 Mist 枚举值；既有三预设场景继续使用原始配置。天空、雾和地表材质参数具有场景切换/禁用恢复检查，运行时不改写共享材质资产。

本轮最终验证：Foundation 43/43、Architecture 通过、Unity EditMode 46/46、Unity PlayMode 29/29。首次 PlayMode 为 27/28，循环边界断言取样过早；修正等待方式并加入迷雾测试后顺序重跑通过，原失败证据保留。三个专用 Shader 当前编译诊断均为空。详见 [实际验证记录](Validation.md)。

没有修改平台 Runtime/Editor、SDK 或全项目渲染管线设置；未进行团结、WebGL、移动设备帧率/显存、微信真机或发布验收。本研究可用于继续比较画风，不能作为这些平台的性能结论。
