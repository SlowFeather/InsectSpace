# AFK 原素材与家园渲染研究

Unity `6000.6.3f1`，项目 `client/unity/InsectSpaceClient`。通过 UnityCLI 控制本地 Editor，使用 Root MuMu 截图和本机解包档案，恢复模型、贴图、材质参数、地图实例、环境曲线及 LUT，适配到当前 URP。这是明确标记 LOCAL 的画面研究，尚未一比一复现原游戏。

## 预览

先用 Unity `6000.6.3f1` 打开本机项目 `D:/Project/InsectSpace/client/unity/InsectSpaceClient`。保存当前场景、停止 Play，保持 UnityCLI Pipeline 已连接，在仓库根目录运行：

```powershell
./tools/Start-AfkRenderStudy.ps1 -Preset Day
./tools/Start-AfkRenderStudy.ps1 -Preset Night
./tools/Start-AfkRenderStudy.ps1 -Preset Dawn
./tools/Start-AfkRenderStudy.ps1 -Preset Dusk
./tools/Start-AfkRenderStudy.ps1 -Preset Day -Cycle -CycleSeconds 240
./tools/Start-AfkRenderStudy.ps1 -Preset Night -HideFairy
./tools/Start-AfkRenderStudy.ps1 -FullMap
./tools/Start-AfkRenderStudy.ps1 -OpenOnly
./tools/Start-AfkRenderStudy.ps1 -Rebuild -Preset Day
```

| 入口 | 场景及用途 |
| --- | --- |
| 默认 | `Assets/Temp/AFKStudy/Homestead/AFKRecoveredLakeside.unity`；使用原素材，按 MuMu 湖岸画面重新组合 |
| `-FullMap` | `Assets/Temp/AFKStudy/Homestead/AFKHomestead.unity`；恢复原始 homestead_01 静态实例布局 |

默认场景左上角支持 Day / Night、时间滑块、Cycle、动画暂停、缩放和 **Fairy / light**。取消勾选 Fairy / light 会立即隐藏小精灵及其暖光，重新勾选即可恢复；日夜切换、拖动时间及循环跨午夜都会保留该选择。启动器 `-HideFairy` 可直接以隐藏状态进入 Play。启动器默认固定在指定时刻；加 `-Cycle` 连续循环，默认一昼夜 240 秒，可用 `-CycleSeconds 10..3600` 修改。Day / Night 按钮会停止循环，重新勾选 Cycle 可继续。FullMap 可在 Scene View 检查地图，当前没有角色移动玩法；下面的高级水面和体积雾验证针对默认湖岸场景。`RainNight` 仅保留为 Night 的旧参数别名；没有恢复降雨。

## 原数据与适配

- 完整地图恢复 **34,182** 个静态实例：Foreground 3,606、Grass 17,119、Renderer 2,039、Terrain 11,418；基础导出含 **232 meshes / 136 materials / 204 textures**。水域另含 **3,194** 个原水块。该布局不等同于 MuMu 中用户保存的动态家园布置。
- broadleaf 岩壁、树木、灌木、芦苇、石块、蒲公英及家园建筑经 Editor API 导入，本次生成 **298** 个预制体；Generated 中还可能留有早期研究预制体。
- 默认场景含 **1,045 renderers**（含新增小精灵光点）、810 组原草实例；使用原 Faye Generic 骨骼、7 个蒙皮 Renderer 和 `pbsc_char_hero_faye@idle` 循环动画。Faye 并非截图里的玩家主角。
- broadleaf 岩壁已恢复流式 **LOD0**，替换最初常驻包中的 LOD2；六份网格分别有 118 / 126 / 146 / 156 / 128 / 134 个顶点。默认场景保留原地图两段轮廓的 **18 个岩壁模块**及对应高台地表，按组平移、保留原尺寸与接缝。右侧恢复范围延伸至原坐标 x=255，解决默认横屏露出的悬空六边形边缘。高台地表关闭投影，由岩壁提供真实阴影；顶面材质与岩壁 VT 区域共用 AO、明暗阈值和校准响应，消除两种材质之间的六边形色差。
- 恢复 EnvironmentInfo 的太阳旋转、光照颜色/强度曲线、环境色 Gradient 与原 **1024×32 LUT**。对照构图另加太阳 yaw 偏移：白天 +20°、夜晚 −40°（基础 −40°，白天另加 60°），ambient contribution 0.88、LUT contribution 0.85。
- 原岩壁 diffuse alpha / VT 混合、植被对象空间 normal、明暗阈值、公告板方向、草叶高度、花朵 `_Scale` 分支已按 GLES 程序适配。植被阴影使用原竖直方向、`_ShadowLength`、光向偏移与 `_ShadowDepthOffset`。
- 原水网格、半浮点数据、噪声贴图和参数保存在 Source/Water。水 Shader 接入不透明场景颜色/深度，完成折射、RGB 吸收、深浅水过渡、焦散、接触岸线、原 `skybox_02`、流动噪声与光斑合成，并增加实时平面反射。这是 Unity 6 URP 中重建的合成链；原游戏的专有 MRT 管线没有原样移植。
- Faye 的三份不透明 `iGame/Char` 材质已从原包提取，包括贴图色彩空间和属性。专用 Shader 在原明暗阈值、环境权重、主光强度上限和附加光 ramp 上加入 UV2 面部 SDF、皮肤透射、原六面环境贴图 IBL 与基于场景深度的轮廓光。`hdr_10` / `hdr_34` 的格式 34 实际为 ETC_RGB4（ETC1 LDR），按该格式解码，不能因名称含 hdr 就当作 BC6H。妆容、溶解和战斗闪光等分支未纳入本轮静态待机复现。玻璃和地面假影未计入这三份材质。
- 云影采用原云纹理、平铺/移动参数和环境时间权重；体积雾采用原噪声、颜色/密度/散射曲线，通过相机专用 RenderGraph pass 在场景深度边界内做 24 步积分。太阳、环境、水色、雾和角色暖光连续随时间变化，午夜颜色首尾连续。`AfkStudyCameraEffects` 只为研究相机请求颜色/深度并加入效果，没有修改全项目 Renderer 配置。

2026-10-09 续作改用 MuMu 游戏实时昼夜画面校准。花朵使用原 `pbsc_bio_chapter03_dandelion_02_hd`，13 组、每组 3 个公告板。白天地表 tint 的线性乘数为 `(0.46, 0.5, 0.8)`，岩壁为 `(0.44, 0.5, 0.63)`、岩壁 VT 顶面另乘 `(0.964, 0.952, 1.795)`；材质 Color 保存为 gamma 值，上传 Shader 后才是这些线性乘数。高台地表采用岩壁顶面的合成乘数。细地表通过原模板纹理的四组随机偏移/旋转采样与平滑权重叠加粗 VT，抑制明显重复；模板为无压缩双线性过滤，tiling 0.045、contrast 4、粗 VT mip bias 3。它是缺失细 VT 时的重建层，不是补回原始页面。

夜景采用冷蓝环境与小精灵暖光。岩壁 `_StudyNightExposure=0.8`、顶面曝光 0.4、顶面饱和度 0.6、顶面 tint `(0.9, 1.05, 0.9)`；高台地表曝光为合成后的 0.32，使用相同饱和度与 tint，前景草地保留白色顶面 tint。暖光来自角色身旁的小精灵位置，颜色 `(1, 0.82, 0.52)`、夜间强度 **8**、范围 **8.5**，在晨昏平滑淡入淡出。精灵用本地发光核心、光晕和双翼轮廓表示，不冒充解包恢复的精灵模型；隐藏时光点关闭，Point Light disabled 且 intensity=0。保留 `ActorLight` 字段名兼容已有场景/探针，通过 `SetFairyVisible(bool)` 控制。普通草叶高度为原材质的 0.8 倍，局部暖光权重为 1.5，与地面一致。暖光配置、`_StudyNight*`、`_StudyLocalLightWeight`、`_StudyLocalShadowWeight` 都是人工匹配参数，不是原游戏配置；阴影区局部填充遮蔽为 0.7，点光投影在默认对照场景关闭。Shader 的方向光与点光投影通道均已验证。校准只作用于 Study 材质副本，原导入材质库仍可用于数据对照。

默认湖岸采用沿岸距离流（网格颜色 G）驱动的柔边合成：不规则轮廓与地面共享采样点，浅水横截面从 11 点增至 21 点，水色/反射向浅滩逐渐过渡。窄接触泡沫与移动内侧波带有噪声断续和导数抗锯齿，避免连续硬白描边。两组不同方向/波长的细波纹共同扰动折射、反射和柔和高光；焦散使用连续波场，减少方块状重复。`_StudySoftShore=1`、`_StudyRippleStrength=0.28`、`_StudyShoreStrength=0.55`。FullMap 未提供该距离流，仍采用原 sawtooth 岸线分支。吸收和折射使用真实场景深度，原输出 alpha 不直接当作 URP 透明混合权重。Surface 和 Character 保留 DepthNormals pass，保证当前 SSAO 深度预通道包含水底/人物；反射相机的垂直采样方向已验证。

按用户补充的岸线局部截图，边缘波纹已改为连续向湖内扩散的多道波带。G 通道存储到最近岸线线段的距离，不再只取横向距离，使波峰跟随曲岸形状。默认速度 **0.4 m/s**、间距 **0.8 m**（每 2 秒产生一道）、宽度 **0.065 m**、淡出范围 **2 m**；分别由 `_StudyShoreWaveSpeed/Spacing/Width/Range` 控制，强度为 `_StudyShoreWaveStrength=0.75`。`_StudyShoreWaveTime=-1` 使用实时渲染时间，非负值仅用于可重复录帧；诊断结束恢复实时值。近岸渐入与远处淡出避免波带在周期边界突然出现。

`CompareAfkRecovered.Run` 保存可逆的昼夜 A/B 截图，结束后恢复材质、光照和 Renderer 属性，不保存实验场景。原方向光、原 zone-light 路径、白色/绿色顶面及更黄的暖光均已对照；在当前缺少完整区域数据的构图中，原 zone-light 直用偏亮偏青，最终保留校准方向和绿色顶面，未采用更黄的暖光。植被曾在比较后缩小而阴影尺寸正常，已定位为空 `MaterialPropertyBlock` 的恢复问题；空块使用 `SetPropertyBlock(null)` 清除。Shader 的 `DisableBatching=True` 保证对象空间变形不被动态批处理改变，但不是该回归修复的证据。

`CompareAfkRecovered.CompareShoreGrass` 可逆比较旧岸线与草叶校准；`ComparePlateau` 比较高台与岩壁顶面参数，证据位于 `advanced/plateau/`。历史比较使用当时基线，最终值以上述参数和 `water-fairy/` 最新验证图为准。

原编译程序保存在 `Assets/Temp/AFKStudy/RenderSource/Shaders/`，不能直接作为 Windows / Unity 6 的 URP Shader 使用。适配源码位于 `Assets/InsectSpace/Rendering/AfkStudy/`，没有替换全项目 URP 管线。

## 本机重建

`AgentScripts/BuildAfkHomestead.cs` 提供以下 UnityCLI run_script 入口，场景、网格、材质和预制体均由 Unity API 保存：

| 入口 | 作用 |
| --- | --- |
| BuildAfkHomestead.BuildLibrary | 导入 Source/Prefabs、Supplement、Buildings、Monument；每次最后用 CliffRecovery 覆盖常驻 LOD2 |
| BuildAfkHomestead.ImportCliffRecovery | 从本机 CliffRecovery 导入六份流式 LOD0 岩壁 |
| BuildAfkHomestead.Run | 从已提取布局生成完整地图；对应 `-FullMap -Rebuild` |
| BuildAfkHomestead.BuildStudy | 重组默认湖岸；对应默认 `-Rebuild` |
| BuildAfkHomestead.UpdateRecovery | 重导基础材质、环境及草花 bounds |
| BuildAfkHomestead.FrameFullMap | 更新完整地图近景机位并截图 |

例如：

```powershell
unity command run_script --file "D:/Project/InsectSpace/AgentScripts/BuildAfkHomestead.cs" --entry BuildAfkHomestead.BuildLibrary --timeout_ms 180000 --timeout 180 --project-path "D:/Project/InsectSpace/client/unity/InsectSpaceClient" --format json --no-pager
```

默认湖岸重建还依赖已有完整地图环境及 `Assets/Temp/AFKStudy/Replica/AFKLakeside.unity` 中的 Faye 与营地。早期角色和 Replica 代码保留在 BuildAfkStudy.cs、BuildAfkReplica.cs。这些入口不会自动下载整个解包档案。

Python 依赖版本见 `AgentScripts/requirements-afk.txt`。catalog 解析、依赖定位和预制体导出 helper 已纳入 AgentScripts，不再从忽略目录导入 Python 源码：

```powershell
python -B AgentScripts/ExtractAfkHomestead.py layout
python -B AgentScripts/ExtractAfkHomestead.py prefabs
python -B AgentScripts/ExtractAfkHomestead.py vt
python -B AgentScripts/ExtractAfkHomestead.py environment
python -B AgentScripts/ExtractAfkWater.py
python -B AgentScripts/ExtractAfkRenderReference.py
python -B -m AgentScripts.ExtractAfkCharacterMaterials
python -B -m AgentScripts.ExtractAfkAdvancedRendering
```

仍需本机 `.artifacts/reference/afk-journey/` 中的 catalog、bundle-manifest、bundles、map-definitions、地图 buffer，以及 Supplement/Buildings/Monument/CliffRecovery 导出。新机器仅 checkout Git 不能独立重建商业参考场景。缺失依赖会报错。

## 验证与差距

最新扩散波纹证据为 `.artifacts/validation/afk-recovered/2026-10-09/shore-waves/`：`Shore-Waves.gif`、40 个连续帧、昼夜近景、`shore-waves-probe.json`、命令结果及回归日志。`VerifyAfkShoreWaves.Run` 从浮点 GPU 诊断图测得波峰在 0.6 秒内向外推进 **0.2355 m**（目标 0.24 m），两米外 mask=0，2 秒周期误差 **1.55e−6**。开启/关闭扩散波纹变化 **13,087** 像素（960×540）。上一轮 `water-fairy/` 保留水深/反射、精灵开关、Day/Night/Dawn/Dusk 八图与回归证据；`advanced/`、父目录及 2026-10-08 目录保留更早的基线。

Play 下运行 `VerifyAfkAdvanced.Run`：冻结角色与时间后逐项开关水深合成、平面反射、体积雾、云影、SDF、皮肤透射、IBL 和轮廓光，重复帧噪声为 0，各效果都有可测像素变化；扫描 16 个云相位，检查反射标记镜像位置、数值水深及午夜循环。三个湖内样点深度约 0.68 米，反射开关前后相同，证明深度来自主相机的水底；不能仅凭开关后图像变化认定深度正确。`VerifyAfkRecovered.Run` 另检查材质、真实蒙皮变形、根稳定、人物裁切和光影。这些专项探针不计作 Unity Test Runner 用例。

水岸与小精灵更新后，`VerifyAfkWaterFairy.Run` 验证白天/夜间隐藏与恢复、五个时间点及跨午夜保持隐藏；`VerifyAfkAdvanced.RunWaterFairy` 复验深度、反射、云雾和角色高级着色，保存独立证据。Foundation **85/85**、Architecture 通过、HeroCloudStudy PlayMode **5/5**。默认场景保存为 Day、240 秒连续循环，Editor 保持 Play 供预览。

当前差距：动态家园布局未恢复；整体布局、花朵尺度、参考主角和建筑特效仍未完全匹配。水面、云影、体积雾和角色高级着色已在 URP 中接通并验证，但不是原专有渲染器的逐条移植，也不代表像素一致。地表使用原 VT 配合原模板重建，湖岸网格按参考构图生成。

homestead_01 VT 索引仅有 38 个可用页面，另有 13 个缺失 bundle，所属 `LDRes/prgroup_LDRes_VT_5130716192642926209.lpak` 在本机档案与当前 MuMu 中均未找到。现有 1536×2048 的 `diffuse-world.png` 是粗级拼图，无法提供参考中的细级地表画笔色块；模板草地图块和水域噪声遮罩只能近似，不能称为补回原始 VT。MuMu 本轮最终参考图为 `.artifacts/reference/afk-journey/mumu-oct09-day-live.png`（白天）及 `mumu-oct09-continuation.png`（夜晚），均为实时游戏画面；早期保存照片不再作为最新对照，比较时排除 UI。

参考商业资源和截图保留在忽略的 Assets/Temp/AFKStudy、.artifacts，遵循项目本地研究边界。未进行移动 GPU/内存基准、团结/WebGL 构建、微信真机或发布验收。实际回归见 `docs/Validation.md`。
