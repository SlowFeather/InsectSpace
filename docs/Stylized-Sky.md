# Unity 6 风格化天空盒

实现日期：2026-10-02。工程：

    D:\Project\InsectSpace\client\unity\InsectSpaceClient

使用 Unity CLI 1.0.0-beta.11 驱动已打开的 Unity 6000.6.3f1 / URP Editor 创建和保存材质、预设、预制体及场景。视觉方向取自用户提供的《剑与远征：启程》截图：蓝色纵向渐变、暖白云顶、青色云底、清晰的天空留白及草地地平线。这里是原创近似方案，未提取原作资产，也不声称知道原作内部实现。

## 启动与操作

在仓库根目录运行（要求 Unity 6 工程已打开、Play 已停止、当前场景已保存）：

    ./tools/Start-SkyShowcase.ps1
    ./tools/Start-SkyShowcase.ps1 -Preset Sunset -Quality 0
    ./tools/Start-SkyShowcase.ps1 -Preset Night -Quality 1
    ./tools/Start-SkyShowcase.ps1 -OpenOnly

启动器打开 Assets/InsectSpace/Scenes/SkyShowcase.unity，并将 Editor Game View 设置为 900×1600。场景内的按钮支持：日间 / 黄昏 / 月夜、云层运动暂停、纯天空 / 场景、低 / 中 / 高画质。展示面板是本地美术调试 UI；正式客户端可移除 Sky art review controls 对象。

原 MeadowShowcase 场景继续保留；新增场景复用其持久化网格、CC0 KayKit 角色与动画，并采用能看见天空的透视相机。天空展示场景相机位置为 (0.35, 2.65, -9.4)，俯仰角 -4°，FOV 45°。显示面板可能覆盖角色下部；查看纯画面可禁用面板组件。

## 功能与文件

所有核心资产位于 Assets/InsectSpace/Rendering/Sky/：

| 文件 | 用途 |
| --- | --- |
| MobilePainterlySky.shader | 单 Pass 天空：渐变、云层、太阳、月牙与星点 |
| StylizedSkyProfile.cs、Profiles/ | 三套可编辑的 ScriptableObject 色彩、光照及风速配置 |
| StylizedSkyController.cs | 运行时材质实例、平滑预设切换、画质、云层运动及场景环境恢复 |
| Prefabs/StylizedSky.prefab | 可在其他 URP 场景使用的天空组件 |
| Materials/ | Daylight、GoldenHour、Moonrise 静态天空材质 |
| Textures/PainterlyCloudAtlas.png | 2048×1024 原创程序绘制无缝云图；R 轮廓、G 明暗、B 高云 |
| SkyLitMeadow.shader、SceneMaterials/ | 本展示场景专用的受光草地材质，使夜晚和黄昏同时影响地面与角色 |
| SkyShowcasePanel.cs | 独立展示 UI，支持点击和触摸 |

颜色通过 Material 的 Color 属性传入；本机渲染像素检查确认不可再次手动执行 .linear，否则会重复转换。云层两个速度分别累计相位，避免主云层绕回时高云突然跳动。

## 接入已有场景

1. 将 Prefabs/StylizedSky.prefab 放入目标场景。每个活动场景使用一个天空控制器。
2. 在 Inspector 将 Target Camera 指向场景主相机，将 Directional Light 指向场景太阳灯。预制体已配置三套 Profile 和 Daylight 材质；跨场景引用由接入方指定。
3. 相机使用 Skybox 清屏。控制器在运行时会为指定相机设置该模式，禁用时恢复原值。编辑态预览可将 Materials/Daylight.mat 赋给 Lighting > Environment > Skybox Material。
4. Additive 加载后，应通过 SceneManager.SetActiveScene 指定实际表现的场景。控制器只在自己所属场景为活动场景时拥有 RenderSettings。
5. 调用 sky.SetPreset(StylizedSkyController.Preset.Sunset, 1.5f) 平滑切换；0 秒为立即切换。sky.AnimateClouds 控制运动，sky.SetQuality(0) 固定低档，sky.ResumeQualityTracking() 恢复跟随项目画质。

切换会同步天空材质、环境光、雾颜色及指定方向光的颜色和强度。方向光朝向由场景独立控制；太阳/月亮的视觉位置由 Profile.celestialDirection 控制，不是天文时间系统。反射探针不在每帧更新；需要动态反射的正式场景应另外安排低频更新。

运行时实例不修改材质模板。禁用、卸载及活动场景切换时释放材质并恢复环境；非活动场景的恢复推迟到它再次成为活动场景时执行。表现层不参与确定性模拟。

## 移动端预算

- 天空使用一个 Shader Pass；低档每像素 1 次云图采样，中 / 高档 2 次采样。中 / 高天空细节相同，展示场景的草量仍随三档画质变化。
- 无体积云、光线步进、实时云阴影或逐云 GameObject；云图在 Editor 生成，运行时不生成纹理。
- 纹理开启 mipmap、U Repeat / V Clamp、关闭 CPU Read/Write；Android 与 iPhone 导入覆盖为 ASTC 6×6，Standalone 为 BC7，当前 WebGL 为 DXT5。
- 本机 Direct3D11 / WebGL 当前目标下，Profiler 报告此 Texture 对象运行时内存 5,593,408 字节；这包含 Editor / 对象统计，不能当成手机 GPU 内存或帧耗时。
- 当前完成本地 Unity 6 功能及渲染验证。没有 Android / iOS 真机帧时间、温升、耗电或微信设备验收结论；项目发布门禁仍适用。

## 重建与验证

重建会覆盖 SkyShowcase 和生成的天空资产内容；手工修改前可先备份。生成过程通过 Unity Editor API 保存序列化资产，不手工编辑 YAML。已有天空资产 GUID 保留。

    unity command run_script --project-path "D:/Project/InsectSpace/client/unity/InsectSpaceClient" --file "D:/Project/InsectSpace/AgentScripts/BuildStylizedSky.cs" --entry BuildStylizedSky.Run --format json
    ./tools/Test-SkyShowcase.ps1
    ./tools/Test-SkyShowcase.ps1 -CaptureOnly

专项命令执行 6 项 PlayMode 测试，并捕获三套预设 × 三档画质的 9 张纯画面、3 张 UI 画面和 1 张横屏纯天空；结束时停止 Play 并恢复调用前画质。截图位于 Assets/Screenshots/Sky_*.png，结果位于 .artifacts/validation/stylized-sky/。

2026-10-02 最终结果：Foundation 43/43；Architecture 通过；Unity EditMode 46/46；PlayMode 25/25（包含天空 6 项）。专项检查包括重新加载后的材质和纹理、相机持久化、切换中再次切换、模板不被修改、暂停、画质变体、材质释放、非活动场景恢复及双云层跨周期连续性。九组截图的 shaderMessages 均为空；最终捕获区间 Console 无新增 warning/error。已查看日间、黄昏、月夜 UI 与横屏纯天空。

历史诊断保留：一次捕获期间 Pipeline 请求超时；一次全量测试被并发截图命令打断，随后出现内容测试日志错误及世界启动超时，记录于 Interrupted-All-PlayMode.json。顺序重跑全量 PlayMode 得到 25/25。早期颜色断言使用精确浮点相等导致失败，现按 1e-5 容差验证；没有隐藏失败日志来声称通过。
