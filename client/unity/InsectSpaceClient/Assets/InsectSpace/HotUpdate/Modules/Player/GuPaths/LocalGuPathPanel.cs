using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Framework;
using Framework.Network;
using InsectSpace.Client;
using InsectSpace.Cultivation;
using InsectSpace.Gameplay.Economy;
using InsectSpace.GuPaths;
using UnityEngine;

namespace InsectSpace.Gameplay.GuPaths
{
    public sealed class LocalGuPathPanel : MonoBehaviour
    {
        public int Port = 7779;
        public bool Ready { get; private set; } public string Error { get; private set; }
        public LocalEconomyTcpClient Transport { get; private set; }
        public GuPathClient Client => Transport?.GuPaths;
        public GuCatalog Catalog { get; private set; }
        public int[] Draft => draft.ToArray();
        private readonly List<int> draft = new List<int>();
        private InsectSpaceBootstrap boot; private bool ownsBoot; private Font font;
        private GUIStyle title, heading, body, small, button, card, field;
        private bool compact; private float logicalWidth;
        private Vector2 listScroll, sideScroll; private string search = ""; private int rankFilter, pathFilter, selected = 1001;
        private IEnumerator Start()
        {
#if !UNITY_EDITOR
            Error = "LOCAL GU PATHS requires the Unity Editor."; yield break;
#else
            try
            {
                var config = BootConfiguration.Load(); config.Validate();
                if (!config.editorSimulate || !config.localSmokeMode || config.useWeChatSdk) throw new InvalidOperationException("流派联调需要 Editor LOCAL SMOKE 配置。");
                Catalog = new GuCatalog(name =>
                {
                    var asset = Resources.Load<TextAsset>("LocalGuPaths/" + name);
                    if (asset == null) throw new InvalidOperationException("缺少本地蛊虫表，请运行 Build-GuTables.ps1。");
                    var bytes = asset.bytes; Resources.UnloadAsset(asset); return bytes;
                });
            }
            catch (Exception e) { Error = e.Message; }
            if (Error != null) yield break;
            boot = FindObjectOfType<InsectSpaceBootstrap>();
            if (boot == null) { ownsBoot = true; boot = new GameObject("Gu Paths Bootstrap - LOCAL ONLY").AddComponent<InsectSpaceBootstrap>(); }
            float deadline = Time.realtimeSinceStartup + 45;
            while (!boot.Ready && boot.LastError == null && Time.realtimeSinceStartup < deadline) yield return null;
            if (!boot.Ready) { Error = boot.LastError ?? "Bootstrap timeout"; yield break; }
            Transport = new LocalEconomyTcpClient(GameFrameworkEntry.GetModule<INetworkManager>(), enableCultivation: true, guCatalog: Catalog);
            Ready = true; Reconnect();
#endif
        }
        private void Update() { if (Ready) Transport.Tick(Time.unscaledDeltaTime); }
        private void OnDestroy() { Transport?.Dispose(); if (ownsBoot && boot != null) Destroy(boot.gameObject); if (font != null) Destroy(font); Ready = false; }
        private void Execute(Action action) { try { Error = null; action(); } catch (Exception e) { Error = e.Message; } }
        public void Reconnect() => Execute(() => Transport.Connect(Port));
        public void Refresh() => Execute(() => { Client.Refresh(); if (Transport.Cultivation.Pending == null) Transport.Cultivation.Refresh(); });
        public void Submit() => Execute(() => Client.Equip(draft));
        public void SetDraft(int[] ids) { if (Catalog == null) throw new InvalidOperationException("Catalog not loaded."); Catalog.Evaluate(ids); draft.Clear(); draft.AddRange(ids); }
        public void UseConfirmed() { draft.Clear(); if (Client?.Snapshot != null) draft.AddRange(Client.Snapshot.Loadout); }
        private void Toggle(int id)
        {
            selected = id;
            if (draft.Contains(id)) { draft.Remove(id); return; }
            if (draft.Count >= GuCatalog.MaxSlots) { Error = "组合最多六只，请先移除一只。"; return; }
            if (draft.Any(x => Catalog.Get(x).Family == Catalog.Get(id).Family)) { Error = "同一进阶系列只能选一只。"; return; }
            draft.Add(id); Error = null;
        }
        private void Styles()
        {
            if (title != null) return;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Noto Sans CJK SC", "Arial" }, 18);
            body = new GUIStyle(GUI.skin.label) { font = font, fontSize = 17, wordWrap = true }; body.normal.textColor = new Color(.87f, .92f, .9f);
            small = new GUIStyle(body) { fontSize = 14 }; small.normal.textColor = new Color(.62f, .74f, .72f);
            title = new GUIStyle(body) { fontSize = 30, fontStyle = FontStyle.Bold }; title.normal.textColor = new Color(.91f, .78f, .45f);
            heading = new GUIStyle(title) { fontSize = 22 };
            button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 16, fixedHeight = 34 };
            field = new GUIStyle(GUI.skin.textField) { font = font, fontSize = 17, fixedHeight = 32 };
            card = new GUIStyle(GUI.skin.box) { padding = new RectOffset(14, 14, 10, 10), margin = new RectOffset(0, 10, 0, 8) };
        }
        private void OnGUI()
        {
            Styles(); compact = Screen.width < Screen.height;
            float scale = Mathf.Max(.4f, Mathf.Min(Screen.width / (compact ? 900f : 1320f), Screen.height / (compact ? 1400f : 900f)));
            logicalWidth = Screen.width / scale - 48; var matrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            GUILayout.BeginArea(new Rect(24, 16, Screen.width / scale - 48, Screen.height / scale - 32));
            GUILayout.Label("凡蛊组合   /   流派谱", title);
            GUILayout.Label("LOCAL DEVELOPMENT · 真实 TCP · 内存角色，重启丢失 · 背包为服务端演示赠予", small);
            GUILayout.BeginHorizontal(); GUI.enabled = Ready;
            if (GUILayout.Button("连接 / 重连", button)) Reconnect();
            GUI.enabled = Client != null && Client.Connected && Client.Pending == null;
            if (GUILayout.Button("刷新资料", button)) Refresh();
            GUI.enabled = Client != null && Client.Connected && Client.Pending == null && Client.RetryableRequest != null;
            if (GUILayout.Button("重试未确认组合", button)) Execute(() => Client.Retry());
            GUI.enabled = true; GUILayout.EndHorizontal();
            GUILayout.Label(Client?.Status ?? "正在读取蛊虫目录…", body);
            if (Error != null) GUILayout.Label("提示：" + Error, body);
            if (Catalog != null) DrawWorkspace();
            GUILayout.EndArea(); GUI.matrix = matrix; GUI.enabled = true;
        }
        private void DrawWorkspace()
        {
            var s = Client?.Snapshot;
            if (compact) GUILayout.BeginVertical(); else GUILayout.BeginHorizontal();
            if (compact) GUILayout.BeginVertical(card, GUILayout.Height(570)); else GUILayout.BeginVertical(card, GUILayout.Width(700));
            GUILayout.Label("凡蛊目录  ·  " + Catalog.Entries.Count + " 条", heading);
            GUILayout.BeginHorizontal(); GUILayout.Label("搜索", body, GUILayout.Width(45)); search = GUILayout.TextField(search, field); GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            for (int i = 0; i <= 5; i++) if (GUILayout.Button((rankFilter == i ? "● " : "") + (i == 0 ? "全部" : i + " 转"), button)) rankFilter = i;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◀", button, GUILayout.Width(42))) pathFilter = (pathFilter + Catalog.Paths.Count) % (Catalog.Paths.Count + 1);
            GUILayout.Label(pathFilter == 0 ? "全部流派（含支援标签）" : Catalog.PathName(pathFilter), body);
            if (GUILayout.Button("▶", button, GUILayout.Width(42))) pathFilter = (pathFilter + 1) % (Catalog.Paths.Count + 1);
            GUILayout.EndHorizontal();
            listScroll = GUILayout.BeginScrollView(listScroll);
            foreach (var g in Catalog.Entries.Where(g => (rankFilter == 0 || rankFilter == g.Rank) && (pathFilter == 0 || pathFilter == g.Path || pathFilter == g.SupportPath) &&
                (string.IsNullOrEmpty(search) || g.Name.Contains(search) || Catalog.PathName(g.Path).Contains(search))))
            {
                GUILayout.BeginHorizontal();
                bool owned = s != null && s.Owned.Contains(g.Id); bool usable = owned && s.PlayerRank >= g.Rank;
                GUI.enabled = usable;
                if (GUILayout.Button(draft.Contains(g.Id) ? "移除" : "加入", button, GUILayout.Width(64))) Toggle(g.Id);
                GUI.enabled = true;
                if (GUILayout.Button(g.Name + "  ·  " + g.Rank + "转  ·  " + Catalog.PathName(g.Path), button, GUILayout.Width(compact ? logicalWidth - 240 : 338))) selected = g.Id;
                GUILayout.Label(!owned ? "未拥有" : !usable ? "转数不足" : GuCatalog.RoleNames(g.Roles), small);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView(); GUILayout.EndVertical();
            GUILayout.BeginVertical(); sideScroll = GUILayout.BeginScrollView(sideScroll);
            GUILayout.BeginVertical(card); GUILayout.Label("角色与已确认组合", heading);
            GUILayout.Label(s == null ? "等待服务器资料" : (s.PlayerRank == 0 ? "尚未开窍" : CultivationRules.RankName(s.PlayerRank)) + "  ·  背包 " + s.Owned.Count + "  ·  修订 " + s.Revision, body);
            if (s != null) { GUILayout.Label(Catalog.Describe(s.Profile), heading); GUILayout.Label(Names(s.Loadout), body); }
            var growth = Transport?.Cultivation;
            GUI.enabled = growth != null && growth.Ready && growth.RetryableRequest == null;
            if (growth?.Snapshot != null && !growth.Snapshot.Awakened && GUILayout.Button("请求服务器开窍", button)) Execute(() => growth.Awaken());
            if (growth?.Snapshot != null && growth.Snapshot.Awakened && GUILayout.Button("请求突破下一境界", button)) Execute(() => growth.Breakthrough());
            GUI.enabled = true;
            if (growth != null) GUILayout.Label(growth.Status + "；开窍/突破后刷新资料", small);
            GUILayout.EndVertical();
            GUILayout.BeginVertical(card); GUILayout.Label("组合草稿  " + draft.Count + " / 6", heading);
            GUILayout.Label(Names(draft), body); DrawProfile(Catalog.Evaluate(draft));
            GUILayout.BeginHorizontal(); GUI.enabled = Client != null && Client.Ready && Client.RetryableRequest == null;
            if (GUILayout.Button("提交组合", button)) Submit();
            GUI.enabled = true; if (GUILayout.Button("读取已确认组合", button)) UseConfirmed();
            if (GUILayout.Button("清空", button)) draft.Clear(); GUILayout.EndHorizontal(); GUILayout.EndVertical();
            var detail = Catalog.Get(selected);
            if (detail != null)
            {
                GUILayout.BeginVertical(card); GUILayout.Label(detail.Name + " · " + detail.Rank + " 转", heading);
                GUILayout.Label(detail.Effect, body); GUILayout.Label(detail.PathBasis, small); GUILayout.Label(detail.Source, small); GUILayout.EndVertical();
            }
            GUILayout.Label("组合评分：主标签 +3，支援 +2。独占至少 60% 且两只蛊虫覆盖两种定位，判为成型；其余可为倾向或混修。", small);
            GUILayout.Label("草稿仅供预览，提交后以服务器确认结果为准。成型不增加流派境界或道痕；蛊虫描述尚未接入技能施放。", small);
            GUILayout.Label("联调提升转数：服务器控制台 cultivation-xp / cultivation-proof，客户端按正常门槛突破。", small);
            GUILayout.EndScrollView(); GUILayout.EndVertical();
            if (compact) GUILayout.EndVertical(); else GUILayout.EndHorizontal();
        }
        private string Names(IEnumerable<int> ids) { var names = ids.Select(id => Catalog.Get(id).Name).ToArray(); return names.Length == 0 ? "空组合" : string.Join(" + ", names); }
        private void DrawProfile(PathProfile p)
        {
            GUILayout.Label(Catalog.Describe(p), heading);
            foreach (var score in p.Scores) GUILayout.Label(Catalog.PathName(score.Path) + "  " + score.Score + "分  ·  " + score.GuCount + "只  ·  " + GuCatalog.RoleNames(score.Roles), body);
        }
    }
}
