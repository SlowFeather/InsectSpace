using System;
using System.Linq;
using InsectSpace.Client;
using InsectSpace.Contracts;
using InsectSpace.Network;
using InsectSpace.Rendering;
using UnityEngine;

namespace InsectSpace.Gameplay.Demo
{
    public sealed partial class ClientDemoHub
    {
        private sealed class Lesson
        {
            public readonly string Title, Tag, Goal, Source, Exercise;
            public Lesson(string title, string tag, string goal, string source, string exercise)
            { Title = title; Tag = tag; Goal = goal; Source = source; Exercise = exercise; }
        }

        private static readonly Lesson[] Lessons = {
            new Lesson("启动与架构", "真实启动链", "从一个场景走完 GF、YooAsset、Luban 和热更业务入口，认识八个模块的职责。",
                "HotUpdate/HotUpdateEntry.cs", "在 HotUpdateEntry 中找到服务注册与模块依赖；解释为什么 AOT 不引用生成表。"),
            new Lesson("模块生命周期", "真实 ModuleHost", "亲自运行依赖排序、服务冻结、失败回滚与逆序释放，掌握新模块接入方式。",
                "HotUpdate/Modules/Presentation/Demo/DemoModuleLesson.cs", "给教学模块添加一个依赖，再观察 Start / Stop 的顺序变化。"),
            new Lesson("配置与查表", "真实 Luban bytes", "浏览 Core 包实际读出的二进制配置。UI 只消费生成类型，修改源应从 design/luban 开始。",
                "HotUpdate/Config/Generated/Tables.cs", "修改策划源表并运行 tools/Build-Tables.ps1；重新 Play 验证结果。不要手改生成文件。"),
            new Lesson("资源与场景", "真实 YooAsset handles", "依次准备 WorldCommon、加载预制体、附加场景，再按所有权次序释放。",
                "HotUpdate/Modules/Presentation/Demo/ClientDemoHub.cs", "重复加载与释放三次；观察实例先销毁、资源 handle 后释放。"),
            new Lesson("平台与启动门禁", "真实拒绝路径", "用隔离配置触发校验失败。错误必须显式呈现，微信能力不能由桌面 socket 替代。",
                "Runtime/Boot/BootConfiguration.cs", "解释 editorSimulate、localSmokeMode、resourceMode 三个开关的不同职责。"),
            new Lesson("大厅与网络", "本地状态机 + 真实 TCP", "分清传输连通、身份认证和世界准入；可连接本机诊断服务，结果不代表账号登录。",
                "HotUpdate/Modules/Lobby/Demo/LocalNetworkProbe.cs", "停止诊断主机再连接，观察失败；确认不会自动变成成功或本地登录。"),
            new Lesson("角色 · 装备 · 蛊虫", "LOCAL 读模型夹具", "加载角色快照、拒绝旧 Revision、预览装备与蛊虫。这里只演示客户端消费与展示。",
                "HotUpdate/Modules/Player/Demo/LocalPlayerLesson.cs", "在自己的 Player 模块接入服务器快照；预览值不能直接写回权威属性。"),
            new Lesson("大世界 AOI 与移动", "真实消费者 / LOCAL 快照", "观察进入、离开、外观变化、插值和换线。移动意图必须等权威快照确认才改变可见角色。",
                "HotUpdate/Modules/World/Demo/LocalWorldLesson.cs", "先发送移动而不确认，角色应停住；切换 Epoch 后发送旧快照，应被拒绝。"),
            new Lesson("战斗 · 缺帧 · 回放", "真实 GF 确定性世界", "两个教学角色用整数指令交战。演示逐帧推进、乱序缓存、缺帧等待和逐帧 hash 回放核对。",
                "HotUpdate/Modules/Battle/Demo/DemoBattleLab.cs", "先送帧 1 再尝试推进；补帧 0 后推进两帧。重放应与本地命令顺序完全一致。"),
            new Lesson("剧情 · 任务 · 奖励", "LOCAL 内容与回执", "完成原创教学任务，区分条件、提交和奖励确认，观察重复回执不会重复计数。",
                "HotUpdate/Modules/Content/Demo/LocalQuestLesson.cs", "连续点击两次确认回执；解释真实项目的幂等键和持久化应由谁处理。"),
            new Lesson("画质与相机", "真实 URP 切换", "按实际配置切换低、中、高档管线与目标帧率。相机只改变表现，不改变权威状态。",
                "Rendering/QualityController.cs", "切换三档后记录管线与 FPS 配置；不要把 Editor 的帧率当作真机性能结论。"),
            new Lesson("热更与版本边界", "Editor 程序集 / 真实检查", "读取实际热更程序集、AOT 引用方向与目标匹配规则。Editor 不执行原生 HybridCLR 注入。",
                "Runtime/HotUpdate/HotUpdateLoader.cs", "沿清单 → SHA → 母包/目标 → AOT metadata → Gameplay 入口阅读 Loader。")
        };

        private Font uiFont;
        private GUIStyle bodyStyle, titleStyle, smallStyle, sectionStyle, buttonStyle, navStyle, codeStyle, inputStyle;
        private static readonly Color Ink = new Color(0.84f, 0.90f, 0.94f);
        private static readonly Color Muted = new Color(0.52f, 0.65f, 0.71f);
        private static readonly Color Accent = new Color(0.30f, 0.92f, 0.73f);

        private void InitializeStyles()
        {
            if (bodyStyle != null) return;
            uiFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Noto Sans CJK SC", "Arial" }, 18);
            bodyStyle = new GUIStyle(GUI.skin.label) { font = uiFont, fontSize = 18, wordWrap = true,
                padding = new RectOffset(0, 0, 4, 4), normal = { textColor = Ink } };
            titleStyle = new GUIStyle(bodyStyle) { fontSize = 29, fontStyle = FontStyle.Bold };
            smallStyle = new GUIStyle(bodyStyle) { fontSize = 15, normal = { textColor = Muted } };
            sectionStyle = new GUIStyle(bodyStyle) { fontSize = 20, fontStyle = FontStyle.Bold, normal = { textColor = Accent } };
            codeStyle = new GUIStyle(bodyStyle) { fontSize = 16, padding = new RectOffset(12, 12, 10, 10),
                normal = { textColor = Ink, background = Texture2D.whiteTexture } };
            buttonStyle = new GUIStyle(GUI.skin.button) { font = uiFont, fontSize = 17, alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(12, 12, 8, 8), margin = new RectOffset(0, 6, 4, 4), wordWrap = true };
            navStyle = new GUIStyle(buttonStyle) { fontSize = 15, margin = new RectOffset(0, 0, 2, 2),
                padding = new RectOffset(8, 8, 6, 6) };
            inputStyle = new GUIStyle(GUI.skin.textField) { font = uiFont, fontSize = 18 };
        }

        private static void Panel(Rect rect, Color color)
        {
            var old = GUI.color; GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = old;
        }

        private void OnGUI()
        {
            InitializeStyles();
            float scale = Mathf.Min(Screen.width / 1440f, Screen.height / 900f);
            var oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1440 * scale) / 2,
                (Screen.height - 900 * scale) / 2, 0), Quaternion.identity, Vector3.one * scale);
            Panel(new Rect(0, 0, 1440, 900), new Color(0.035f, 0.055f, 0.08f));
            GUI.Label(new Rect(28, 15, 900, 46), "INSECTSPACE   /   客户端功能实验室", titleStyle);
            GUI.Label(new Rect(30, 58, 1010, 27), "UNITY 6  ·  新成员上手与现场演示  ·  真实框架 + 明确标识的本地教学数据", smallStyle);
            GUI.Label(new Rect(1080, 28, 336, 30), "LOCAL · 已体验 " + CompletedCount + " / 12", sectionStyle);
            Panel(new Rect(24, 99, 220, 720), new Color(0.060f, 0.09f, 0.12f));
            GUILayout.BeginArea(new Rect(36, 111, 196, 694));
            GUILayout.Label("LEARNING PATH", smallStyle);
            for (int i = 0; i < Lessons.Length; i++)
            {
                var before = GUI.backgroundColor;
                GUI.backgroundColor = i == SelectedLesson ? new Color(0.18f, 0.66f, 0.54f) : Color.white;
                if (GUILayout.Button((i + 1).ToString("00") + (completed[i] ? " ✓ " : "  ") + Lessons[i].Title, navStyle, GUILayout.Height(43))) SelectLesson(i);
                GUI.backgroundColor = before;
            }
            GUILayout.Space(8);
            GUILayout.Label("约 30 分钟 · 可重复操作", smallStyle);
            ActionButton("重置全部教学状态", () => RunOperation(ResetLessons()), markLesson: false);
            GUILayout.EndArea();

            Panel(new Rect(260, 99, 674, 720), new Color(0.063f, 0.09f, 0.12f));
            GUILayout.BeginArea(new Rect(282, 114, 630, 686));
            pageScroll = GUILayout.BeginScrollView(pageScroll, false, false);
            var lesson = Lessons[SelectedLesson];
            GUILayout.Label(lesson.Tag.ToUpperInvariant(), sectionStyle);
            GUILayout.Label(lesson.Title, titleStyle);
            GUILayout.Label(lesson.Goal, bodyStyle);
            GUILayout.Space(12);
            if (!Ready)
                Box(Stopped ? LastResult : Error == null ? "正在启动：" + (boot == null ? "检查配置" : boot.Stage) : "DEMO STOPPED\n" + Error);
            else DrawLesson();
            GUILayout.Space(18);
            GUILayout.Label("动手练习", sectionStyle);
            GUILayout.Label(lesson.Exercise, smallStyle);
            GUILayout.Label("源码 · Assets/InsectSpace/" + lesson.Source, smallStyle);
#if UNITY_EDITOR
            if (GUILayout.Button("在编辑器中打开本页源码", buttonStyle))
            {
                var source = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/InsectSpace/" + lesson.Source);
                if (source != null) UnityEditor.AssetDatabase.OpenAsset(source);
            }
#endif
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            Panel(new Rect(950, 99, 466, 370), new Color(0.07f, 0.11f, 0.15f));
            GUI.Label(new Rect(969, 109, 426, 31), "LIVE WORLD  /  本地可见集", sectionStyle);
            if (preview != null) GUI.DrawTexture(new Rect(957, 151, 452, 286), preview, ScaleMode.ScaleToFit, false);
            GUI.Label(new Rect(970, 436, 426, 26), "AOI 角色 " + VisibleActors + "  ·  预制体 " + (ResourceLoaded ? "已加载" : "未加载") +
                "  ·  场景 " + (AdditiveSceneLoaded ? "已附加" : "未附加"), smallStyle);
            Panel(new Rect(950, 485, 466, 334), new Color(0.06f, 0.09f, 0.12f));
            GUILayout.BeginArea(new Rect(970, 499, 426, 301));
            GUILayout.Label("操作记录", sectionStyle);
            foreach (string message in journal.Reverse().Take(5))
            { GUILayout.Label("› " + message, smallStyle); GUILayout.Space(4); }
            GUILayout.EndArea();
            Panel(new Rect(24, 835, 1392, 46), new Color(0.08f, 0.17f, 0.18f));
            GUI.Label(new Rect(40, 844, 1356, 30), Busy ? "资源操作进行中，请稍候…" : LastResult, bodyStyle);
            GUI.matrix = oldMatrix;
        }

        private void Box(string value)
        {
            var before = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.08f, 0.15f, 0.19f);
            GUILayout.Label(value, codeStyle);
            GUI.backgroundColor = before;
        }
        private void Hint(string value) => GUILayout.Label(value, smallStyle);
        private void Heading(string value) { GUILayout.Space(10); GUILayout.Label(value, sectionStyle); }

        private void DrawLesson()
        {
            switch (SelectedLesson)
            {
                case 0: DrawOverview(); break;
                case 1: DrawLifecycle(); break;
                case 2: DrawConfig(); break;
                case 3: DrawResources(); break;
                case 4: DrawPlatform(); break;
                case 5: DrawLobby(); break;
                case 6: DrawPlayer(); break;
                case 7: DrawWorld(); break;
                case 8: DrawBattle(); break;
                case 9: DrawQuest(); break;
                case 10: DrawQuality(); break;
                case 11: DrawVersions(); break;
            }
        }

        private void DrawOverview()
        {
            Box("BOOT " + boot.Stage + "    MODULES " + boot.ModuleCount + "    TABLES " + boot.TableCount +
                "\n" + boot.Status + "\nTCP 登录认证：" + boot.Context.Connections.LobbyAuthenticated);
            Heading("依赖与职责");
            GUILayout.Label("Config → Platform → Lobby → Player → World\nWorld + Config → Battle / Content → Presentation", bodyStyle);
            Hint("启动负责服务；World 负责 AOI 消费；Battle 负责确定性帧；Presentation 只负责显示。演示模型与 Bootstrap 会话分离。");
            Heading("三条边界");
            GUILayout.Label("① AOT → 稳定接口 → 热更入口\n② 世界快照 / AOI ≠ 战斗帧同步\n③ 客户端预览 ≠ 服务器奖励事实", bodyStyle);
            ActionButton("开始第一课：模块生命周期 →", () => SelectLesson(1), markLesson: false);
            ActionButton("结束演示并释放资源", () => RunOperation(Shutdown()), markLesson: false);
        }

        private void DrawLifecycle()
        {
            ActionButton("运行：乱序注册 → 拓扑启动 → 逆序释放", () => RunLifecycle(false));
            ActionButton("注入 view.Start 失败，观察完整回滚", () => RunLifecycle(true));
            Box(LifecycleTrace.Length == 0 ? "等待运行。注册顺序：view / data / config。" : string.Join("\n", LifecycleTrace));
            Hint("使用独立 ModuleHost，因此不会重启或破坏正在运行的 8 个业务模块。");
        }

        private void DrawConfig()
        {
            Hint("按场景 key 或名称筛选（实际数据源：Core / tbworldscene）");
            tableFilter = GUILayout.TextField(tableFilter, inputStyle, GUILayout.Height(32));
            Heading("世界场景表");
            int count = 0;
            foreach (var scene in tables.TbWorldScene.DataList)
            {
                if (tableFilter.Length > 0 && (scene.Key + scene.DisplayName).IndexOf(tableFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                Box("#" + scene.Id + "  " + scene.DisplayName + " / " + scene.Key +
                    "\n包 " + scene.PackageName + "  ·  可见预算 " + scene.MaxVisiblePlayers);
                count++;
            }
            if (count == 0) Hint("没有匹配行；清空筛选查看全部。");
            Heading("画质配置表");
            foreach (var profile in tables.TbQualityProfile.DataList)
                Hint(profile.Id + "  " + profile.Name + "    " + profile.TargetFps + " FPS    可见预算 " + profile.VisiblePlayers);
            ActionButton("重新解析内存中的真实 bytes", () =>
            { tables = new Config.Tables(name => new Luban.ByteBuf(boot.Context.TableData[name])); Record("2 张 Luban 表重新反序列化完成。"); });
        }

        private void DrawResources()
        {
            Box("Core：代码与配置 RawFile\nWorldCommon：常规 prefab / additive scene\nAsset handle：" + (asset != null && asset.IsValid ? "有效 / 持有" : "未持有") +
                "\nScene handle：" + (AdditiveSceneLoaded ? "有效 / 已加载" : "未持有"));
            ActionButton("1. 准备包并加载 WorldActor", () => RunResourceOperation(LoadResource()), !ResourceLoaded, false);
            ActionButton("2. 销毁实例并释放 handle", () => RunResourceOperation(ReleaseResource()), ResourceLoaded, false);
            ActionButton("3. 附加加载 WorldSandbox", () => RunResourceOperation(LoadAdditiveScene()), !AdditiveSceneLoaded, false);
            ActionButton("4. 卸载场景并验证 handle 释放", () => RunResourceOperation(UnloadAdditiveScene()), AdditiveSceneLoaded, false);
            Hint("右侧绿色/橙色角色属于 AOI 表现；资源课加载的 WorldActor 是独立实例。点击换页不会偷释放资源。");
        }

        private void DrawPlatform()
        {
            Box("平台 " + Application.platform + "\nEditorSimulate=" + boot.Context.Configuration.editorSimulate +
                "  LocalSmoke=" + boot.Context.Configuration.localSmokeMode + "\nWeChatSdk=" + boot.Context.Configuration.useWeChatSdk);
            ActionButton("拒绝：缺少包名的启动配置", () =>
            {
                try { new BootConfiguration { packageName = "" }.Validate(); gateResult = "异常：未拒绝"; }
                catch (InvalidOperationException ex) { gateResult = "已拒绝：" + ex.Message; }
                Record(gateResult);
            });
            ActionButton("拒绝：未安装的小游戏网络工厂", () =>
            {
                try { new UnconfiguredMiniGameChannelFactory().Create("demo", new ServiceEndpoint(), new FoundationPacketCodec()); }
                catch (PlatformNotSupportedException) { gateResult = "已拒绝：小游戏桥未配置；没有使用桌面 socket 或本地成功回退。"; }
                Record(gateResult);
            });
            Box(gateResult);
            Hint("微信身份交换、SDK TCP/UDP、缓存、前后台切换和 HybridCLR 设备验证仍需真实平台门禁。");
        }

        private void DrawLobby()
        {
            Box("独立 LOCAL 教学会话：" + Lobby.Phase + "\n玩家 ID：" + Lobby.PlayerId + "\n世界实例：" + (Lobby.Route?.InstanceId ?? "—"));
            GUILayout.BeginHorizontal();
            ActionButton("模拟身份确认", () => { Lobby.Authenticated(1001); Record("LOCAL 夹具：SignedOut → Lobby"); }, Lobby.Phase == SessionPhase.SignedOut);
            ActionButton("模拟世界准入", () =>
            { Lobby.EnterWorld(World.Session.Route); Record("LOCAL 夹具：Lobby → World；路由来自教学夹具。"); }, Lobby.Phase == SessionPhase.Lobby && Lobby.Route == null);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            ActionButton("模拟断线", () => { Lobby.ConnectionLost(); Record("Recovering；停止旧会话推进。"); }, Lobby.Phase == SessionPhase.World);
            ActionButton("重新认证 + 新 Epoch", () =>
            {
                var route = Lobby.Route; route.Epoch++;
                Lobby.Reauthenticated(1001); Lobby.EnterWorld(route); Record("夹具恢复成功；新 Epoch=" + route.Epoch);
            }, Lobby.Phase == SessionPhase.Recovering);
            ActionButton("退出 / 重置", () => { Lobby.SignOut(); Record("教学会话已退出。"); });
            GUILayout.EndHorizontal();
            Heading("真实 TCP loopback 诊断");
            Hint("先在终端运行 tools/Run-LocalServer.ps1。仅访问 127.0.0.1:7777，连接与消息都是真的。");
            ActionButton("连接本机并发送 foundation.hello", () => Network.Connect(), !Network.Busy);
            Box(Network.Status + "\n诊断会话仍为 " + Network.Session.Phase);
        }

        private void DrawPlayer()
        {
            var player = Player.Player;
            Box(player == null ? "尚无快照" : player.DisplayName + "    ID " + player.PlayerId +
                "\nHomeRealm " + player.HomeRealmId + "    Revision " + player.Revision + "\n境界示例 " + player.RealmLevel);
            ActionButton("应用下一版 LOCAL 快照", () =>
            { Record("快照应用=" + Player.Apply(LocalPlayerLesson.Fixture((Player.Player?.Revision ?? 0) + 1))); });
            ActionButton("重送 Revision 1（应拒绝）", () => Record("旧快照应用=" + Player.Apply(LocalPlayerLesson.Fixture())));
            Heading("只读数据 / 本地装配预览");
            ActionButton("预览木剑 + 青叶蛊", () => { Player.PreviewLoadout(101, 201); Record("本地预览；未提交装备或经济变更。"); });
            ActionButton("预览竹杖 + 萤光蛊", () => { Player.PreviewLoadout(102, 202); Record("本地预览；没有服务器属性加成。"); });
            Box("装备示例 ID " + Player.PreviewEquipment + "    蛊虫示例 ID " + Player.PreviewGu + "\n预览状态不改变角色 Revision。");
            Hint("这些 ID 属于教学夹具，不冒充现有 Luban 表中的正式装备/蛊虫配置。");
        }

        private void DrawWorld()
        {
            var route = World.Session.Route;
            Box("HomeRealm " + route.HomeRealmId + "\nInstance " + route.InstanceId + " / Epoch " + route.Epoch +
                "\nWorldTick " + World.Tick + " / 可见角色 " + VisibleActors + " / Room —");
            GUILayout.BeginHorizontal();
            ActionButton("进入两个角色", () => { World.Spawn(); Record("真实 AOI 消费者已应用本地全量快照。"); });
            ActionButton("移除同伴", () => { World.RemovePeer(); Record("可见集移除 → 对象回收。"); });
            ActionButton("切换外观", () => { World.ChangeAppearance(); Record("AppearanceId 改变 → 表现实例替换。"); });
            GUILayout.EndHorizontal();
            ActionButton("发送移动意图（角色暂不移动）", () => Record("移动 Submit=" + World.RequestMove()));
            ActionButton("LOCAL 权威夹具确认移动", () => Record("快照确认=" + World.ConfirmMove()), World.Pending != null);
            if (World.Pending != null) Hint("待确认 seq=" + World.Pending.Sequence + "  x=" + World.Pending.XMillimeters + "mm  z=" + World.Pending.ZMillimeters + "mm");
            GUILayout.BeginHorizontal();
            ActionButton("夹具批准换线", () => { World.SwitchRoute(); Record("新 Epoch 清空可见集，HomeRealm 保持。可再次点击进入角色。"); });
            ActionButton("投递旧 Epoch", () => Record("旧快照接受=" + World.TryStaleSnapshot()));
            GUILayout.EndHorizontal();
            Hint("没有真实在线玩家或好友路由器；这里只有服务器权威输入的本地替身。正式路由必须由服务器批准。");
        }

        private void DrawBattle()
        {
            Box("模式 " + (Battle.OrderedMode ? "LOCAL 有序收件箱" : "LOCAL 帧输入") + " / 当前帧 " + Battle.Frame +
                "\n状态 hash " + Battle.Hash.ToString("X16") + "\n学员 HP " + Battle.PlayerHp + " / 100    练习靶 HP " + Battle.OpponentHp + " / 100");
            Bar("学员", Battle.PlayerHp, Accent);
            Bar("练习靶", Battle.OpponentHp, new Color(1, 0.65f, 0.28f));
            GUILayout.BeginHorizontal();
            ActionButton("重置本地对战", () => ResetBattle(false));
            ActionButton("重置缺帧示例", () => ResetBattle(true));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            ActionButton("推进一帧", () => Record(Battle.Step() ? "已推进帧 " + Battle.Frame : "未推进：缺帧或对战结束。"));
            ActionButton(autoBattle ? "暂停慢速演示" : "慢速连续演示", () => { autoBattle = !autoBattle; }, !Battle.OrderedMode && !Battle.Finished);
            GUILayout.EndHorizontal();
            if (Battle.OrderedMode)
            {
                GUILayout.BeginHorizontal();
                ActionButton("先送下一帧 +1", () => Record("未来帧缓存=" + Battle.Push(Battle.Frame + 2)));
                ActionButton("补齐当前所需帧", () => Record("所需帧缓存=" + Battle.Push(Battle.Frame + 1)));
                GUILayout.EndHorizontal();
            }
            ActionButton("重新执行输入并逐帧比较 hash", () => VerifyBattleReplay(), Battle.RecordedFrames > 0);
            Hint("演示慢放只控制何时调用 TryAdvance；权威计算只接收整数帧号。不是生产技能或网络房间。");
        }

        private void Bar(string label, int value, Color color)
        {
            var rect = GUILayoutUtility.GetRect(200, 27);
            Panel(rect, new Color(0.10f, 0.15f, 0.19f));
            Panel(new Rect(rect.x, rect.y, rect.width * value / 100f, rect.height), color * 0.65f);
            GUI.Label(rect, label + "  " + value, smallStyle);
            GUILayout.Space(4);
        }

        private void DrawQuest()
        {
            Box(Quest.Story + "\n当前状态：" + Quest.Stage + "\n教学回执计数：" + Quest.DemonstrationReceipts);
            ActionButton("1. 与向导交谈", () => Record("交谈条件=" + Quest.Advance(DemoQuestStage.MeetGuide)), Quest.Stage == DemoQuestStage.MeetGuide);
            ActionButton("2. 探访药圃（LOCAL 条件）", () => Record("探访条件=" + Quest.Advance(DemoQuestStage.VisitGarden)), Quest.Stage == DemoQuestStage.VisitGarden);
            ActionButton("3. 完成练习（LOCAL 条件）", () => Record("练习条件=" + Quest.Advance(DemoQuestStage.FinishPractice)), Quest.Stage == DemoQuestStage.FinishPractice);
            ActionButton("4. 提交结算请求（不会直接发奖）", () => Record("提交=" + Quest.Submit() + "；仍等待权威回执。"), Quest.Stage == DemoQuestStage.ReadyToSubmit);
            ActionButton("5. LOCAL 回执确认 / 重复确认", () => Record("回执应用=" + Quest.ApplyFixtureReceipt() + " / 计数=" + Quest.DemonstrationReceipts), Quest.Stage >= DemoQuestStage.PendingReceipt);
            ActionButton("重置教学章节", () => { Quest.Reset(); Record("本地章节状态重置。"); });
        }

        private void DrawQuality()
        {
            Box("当前 " + QualitySettings.names[QualitySettings.GetQualityLevel()] +
                "\n管线 " + (QualitySettings.renderPipeline == null ? "未绑定" : QualitySettings.renderPipeline.name) +
                "\n目标帧率 " + Application.targetFrameRate + "  ·  VSync " + QualitySettings.vSyncCount);
            foreach (var profile in tables.TbQualityProfile.DataList)
            {
                int id = profile.Id;
                ActionButton("切换 " + profile.Name + " / " + profile.TargetFps + " FPS", () => ApplyQuality(id));
            }
            Heading("正交相机视野");
            demoCamera.orthographicSize = GUILayout.HorizontalSlider(demoCamera.orthographicSize, 4, 12);
            Hint("Orthographic Size = " + demoCamera.orthographicSize.ToString("F1"));
            ActionButton("恢复相机视野", () => { demoCamera.orthographicSize = 7; Record("相机视野已恢复。"); });
        }

        private void DrawVersions()
        {
            Box("模式：Editor 已编译 Gameplay\nCore " + boot.Context.Configuration.packageVersion +
                "\n母包 " + boot.Context.Configuration.playerBuildId + "\n协议 " + ProtocolVersion.Current + " / 模拟 " + ProtocolVersion.BattleFramesPerSecond + " Hz");
            ActionButton("检查实际程序集 / 目标匹配 / AOT 引用", () =>
            {
                var gameplay = typeof(ClientDemoHub).Assembly;
                bool badReference = typeof(InsectSpaceBootstrap).Assembly.GetReferencedAssemblies().Any(a => a.Name == gameplay.GetName().Name);
                bool matching = HotUpdateLoader.MatchesRuntime(Application.platform.ToString(), Application.platform);
                bool wrongTarget = HotUpdateLoader.MatchesRuntime("Android", RuntimePlatform.WindowsEditor);
                versionResult = gameplay.GetName().Name + "\nAOT → Gameplay 直接引用=" + badReference +
                    "\n当前目标匹配=" + matching + " / Android 对 WindowsEditor=" + wrongTarget;
                Record("已读取真实程序集引用和 Loader 目标匹配结果。");
            });
            Box(versionResult);
            Hint("本页没有执行原生注入或替换运行中 DLL。完整代码包构建、原生补丁和微信设备验收见 docs/Hot-Update.md、Native-Validation.md 与 WeChat-Release-Gates.md。");
        }
    }
}
