using System;
using System.Collections;
using Framework;
using Framework.Network;
using InsectSpace.Client;
using InsectSpace.Cultivation;
using InsectSpace.Gameplay.Economy;
using UnityEngine;

namespace InsectSpace.Gameplay.Cultivation
{
    // Explicit Editor-only laboratory. The character facts always come from the TCP server.
    public sealed class LocalCultivationPanel : MonoBehaviour
    {
        public int Port = 7779;
        public bool Ready { get; private set; }
        public string Error { get; private set; }
        public LocalEconomyTcpClient Transport { get; private set; }
        public CultivationClient Client => Transport?.Cultivation;
        private InsectSpaceBootstrap boot;
        private bool ownsBoot;
        private Font font;
        private GUIStyle title, heading, body, card, button;
        private Vector2 scroll;
        private IEnumerator Start()
        {
#if !UNITY_EDITOR
            Error = "LOCAL CULTIVATION requires the Unity Editor."; yield break;
#else
            try
            {
                var config = BootConfiguration.Load(); config.Validate();
                if (!config.editorSimulate || !config.localSmokeMode || config.useWeChatSdk) throw new InvalidOperationException("修炼联调需要明确的 Editor LOCAL SMOKE 配置。");
            }
            catch (Exception e) { Error = e.Message; }
            if (Error != null) yield break;
            boot = FindObjectOfType<InsectSpaceBootstrap>();
            if (boot == null) { ownsBoot = true; boot = new GameObject("Cultivation Bootstrap - LOCAL ONLY").AddComponent<InsectSpaceBootstrap>(); }
            float deadline = Time.realtimeSinceStartup + 45;
            while (!boot.Ready && boot.LastError == null && Time.realtimeSinceStartup < deadline) yield return null;
            if (!boot.Ready) { Error = boot.LastError ?? "Bootstrap timeout"; yield break; }
            Transport = new LocalEconomyTcpClient(GameFrameworkEntry.GetModule<INetworkManager>(), enableCultivation: true); Ready = true; Reconnect();
#endif
        }
        private void Update() { if (Ready) Transport.Tick(Time.unscaledDeltaTime); }
        private void OnDestroy()
        { Transport?.Dispose(); Ready = false; if (ownsBoot && boot != null) Destroy(boot.gameObject); if (font != null) Destroy(font); }
        public void Reconnect() => Execute(() => Transport.Connect(Port));
        public void Refresh() => Execute(() => Client.Refresh());
        public void Awaken() => Execute(() => Client.Awaken());
        public void Breakthrough() => Execute(() => Client.Breakthrough());
        private void Execute(Action action) { try { Error = null; action(); } catch (Exception e) { Error = e.Message; } }
        private void OnGUI()
        {
            if (title == null)
            {
                font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Noto Sans CJK SC", "Arial" }, 18);
                body = new GUIStyle(GUI.skin.label) { font = font, fontSize = 17, wordWrap = true };
                body.normal.textColor = new Color(.88f, .92f, .92f);
                title = new GUIStyle(body) { fontSize = 32, fontStyle = FontStyle.Bold }; title.normal.textColor = new Color(.9f, .8f, .49f);
                heading = new GUIStyle(title) { fontSize = 23 };
                card = new GUIStyle(GUI.skin.box) { padding = new RectOffset(18, 18, 12, 12), margin = new RectOffset(0, 0, 7, 7) };
                button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 17, fixedHeight = 40 };
            }
            float scale = Mathf.Max(.45f, Mathf.Min(Screen.width / 1100f, Screen.height / 900f));
            var old = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            GUILayout.BeginArea(new Rect(24, 18, Screen.width / scale - 48, Screen.height / scale - 36));
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("转数 · 资质   /   修炼名册", title);
            GUILayout.Label("LOCAL DEVELOPMENT  ·  真实 TCP  ·  服务端随机一次  ·  内存存储，重启丢失", body);
            GUILayout.Label("本地角色 1 / local-home     127.0.0.1:" + Port + "     规则 v" + CultivationRules.Version, body);
            GUILayout.BeginHorizontal(); GUI.enabled = Ready;
            if (GUILayout.Button("连接 / 重连", button)) Reconnect();
            GUI.enabled = Client != null && Client.Connected && Client.Pending == null;
            if (GUILayout.Button("刷新服务器资料", button)) Refresh();
            GUI.enabled = Client != null && Client.Connected && Client.Pending == null && Client.RetryableRequest != null;
            if (GUILayout.Button("重试未确认操作", button)) Execute(() => Client.Retry());
            GUILayout.EndHorizontal(); GUI.enabled = true;
            GUILayout.Label(Client?.Status ?? "等待客户端启动…", body);
            if (Error != null) GUILayout.Label("错误：" + Error, body);
            var s = Client?.Snapshot;
            GUILayout.BeginVertical(card);
            GUILayout.Label(s == null ? "等待服务器角色快照" : CultivationRules.RankName(s), title);
            if (s != null)
            {
                GUILayout.Label(CultivationRules.ApertureName(s) + "     能量类型：" + CultivationRules.EnergyName(s), body);
                GUILayout.Label("先天资质：" + CultivationRules.GradeName(s.Aptitude.Grade) + "     初始元海：" + s.Aptitude.SeaPercent + "%     仪式停止：" + s.Aptitude.StopStep + " 步", heading);
                GUILayout.Label("元海占比表示先天容量，不是当前真元剩余量；仙元、仙元石是不同数据。", body);
                GUILayout.Label("修炼积累：" + s.Experience + "     本次突破需要：" + CultivationRules.RequiredExperience(s) + "     资料版本：" + s.Revision, body);
                GUILayout.Label(CultivationRules.RequirementText(s), body);
                if (s.Rank >= 6) GUILayout.Label("已验证：天劫 " + s.HeavenlyTribulations + " / 浩劫 " + s.GrandTribulations + " / 万劫 " + s.MyriadTribulations + "     主修道痕 " + s.MainPathDaoMarks, body);
                if (s.Rank == 8) GUILayout.Label("无上大宗师：" + (CultivationRules.Has(s, CultivationProof.SupremeGrandmaster) ? "已确认" : "未确认") + "     天道封锁：" + (CultivationRules.Has(s, CultivationProof.HeavenlySealBroken) ? "已突破" : "未突破"), body);
            }
            GUILayout.BeginHorizontal();
            bool actionable = Client != null && Client.Ready && Client.RetryableRequest == null;
            GUI.enabled = actionable && !s.Awakened;
            if (GUILayout.Button("服务器随机开窍（仅一次）", button)) Awaken();
            GUI.enabled = actionable && s.Awakened && s.Rank < 9;
            if (GUILayout.Button("请求突破下一境界", button)) Breakthrough();
            GUI.enabled = true; GUILayout.EndHorizontal(); GUILayout.EndVertical();
            GUILayout.BeginVertical(card); GUILayout.Label("初始资质规则", heading);
            GUILayout.BeginHorizontal();
            GUILayout.Label("丁等 · 20%\n元海 20–39%\n10–19 步", body);
            GUILayout.Label("丙等 · 50%\n元海 40–59%\n20–29 步", body);
            GUILayout.Label("乙等 · 25%\n元海 60–79%\n30–39 步", body);
            GUILayout.Label("甲等 · 5%\n元海 80–99%\n40–49 步", body);
            GUILayout.EndHorizontal();
            GUILayout.Label("概率为项目初测方案。档内步数与占比分别随机；不按操作速度或充值决定。普通池不含无资质和十绝体。", body);
            GUILayout.Label("同一角色重复请求、掉线、重连不会重抽。资质不加经验或奖励倍率，各档均可晋升九转。", body);
            GUILayout.EndVertical();
            GUILayout.BeginVertical(card); GUILayout.Label("一转至九转", heading);
            GUILayout.Label("一至五转：初阶 → 中阶 → 高阶 → 巅峰；空窍依次为光膜、水膜、石膜、晶膜。", body);
            GUILayout.Label("六转：青提仙元 · 福地     七转：红枣仙元 · 福地\n八转：白荔仙元 · 洞天     九转：黄杏仙元 · 蛊尊", body);
            GUILayout.Label("六转以上不套用凡人四小阶。升仙、灾劫与成尊条件必须有服务端凭据；此面板不提供指定转数或资质的按钮。", body);
            GUILayout.EndVertical();
            GUILayout.Label("本地联调：服务器控制台 cultivation-xp 发放模拟经验；cultivation-proof 记入模拟试炼凭据，再刷新资料并突破。", body);
            GUILayout.Label("经验数值与试炼凭据仅用于联调。尚未制作升仙关卡、灾劫战斗、持久化存档和正式成长数值。", body);
            GUILayout.EndScrollView(); GUILayout.EndArea(); GUI.matrix = old;
        }
    }
}
