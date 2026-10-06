using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Framework;
using Framework.Network;
using InsectSpace.Client;
using InsectSpace.Gameplay.Economy;
using InsectSpace.GuPaths;
using InsectSpace.GuWorkshop;
using UnityEngine;

namespace InsectSpace.Gameplay.GuWorkshop
{
    public sealed class LocalWorkshopPanel : MonoBehaviour
    {
        public int Port = 7779;
        public bool CanReturnToWorld;
        public bool Visible { get; private set; } = true;
        public bool Ready { get; private set; }
        public string Error { get; private set; }
        public LocalEconomyTcpClient Transport { get; private set; }
        public WorkshopClient Client => Transport?.Workshop;
        public WorkshopCatalog Catalog { get; private set; }
        public int Tab { get; private set; }
        public long Selected { get; private set; }
        private readonly List<long> ingredients = new List<long>();
        private InsectSpaceBootstrap boot; private bool ownsBoot;
        private Font font; private GUIStyle title, heading, text, small, button, box;
        private Vector2 scroll; private float refreshTimer;
        private Action confirm; private string confirmation;
        private IEnumerator Start()
        {
#if !UNITY_EDITOR
            Error = "LOCAL GU WORKSHOP requires Unity Editor."; yield break;
#else
            try
            {
                var config = BootConfiguration.Load(); config.Validate();
                if (!config.editorSimulate || !config.localSmokeMode || config.useWeChatSdk) throw new InvalidOperationException("养炼联调需显式 LOCAL SMOKE 配置。");
                byte[] Load(string folder, string name)
                {
                    var asset = Resources.Load<TextAsset>(folder + "/" + name);
                    if (asset == null) throw new InvalidOperationException("缺少养炼数据，请先生成 Luban 表。");
                    var bytes = asset.bytes; Resources.UnloadAsset(asset); return bytes;
                }
                Catalog = new WorkshopCatalog(new GuCatalog(name => Load("LocalGuPaths", name)), name => Load("LocalGuWorkshop", name));
            }
            catch (Exception e)
            {
                Error = e.Message;
                if (Error.IndexOf("尚未启用", StringComparison.Ordinal) >= 0 || Error.IndexOf("Missing workshop table", StringComparison.Ordinal) >= 0)
                    Error += "\n请先确认玩法参数，填写 design/luban/GuWorkshop/Tables，再运行 Build-WorkshopTables.ps1。";
            }
            if (Error != null) yield break;
            boot = FindObjectOfType<InsectSpaceBootstrap>();
            if (boot == null) { ownsBoot = true; boot = new GameObject("Workshop Bootstrap - LOCAL").AddComponent<InsectSpaceBootstrap>(); }
            float deadline = Time.realtimeSinceStartup + 45;
            while (!boot.Ready && boot.LastError == null && Time.realtimeSinceStartup < deadline) yield return null;
            if (!boot.Ready) { Error = boot.LastError ?? "Bootstrap timeout"; yield break; }
            Transport = new LocalEconomyTcpClient(GameFrameworkEntry.GetModule<INetworkManager>(), enableCultivation: true, workshopCatalog: Catalog);
            Ready = true; Reconnect();
#endif
        }
        private void Update()
        {
            if (!Ready) return; Transport.Tick(Time.unscaledDeltaTime); refreshTimer += Time.unscaledDeltaTime;
            if (refreshTimer >= 5 && Client.Ready && confirm == null && Client.RetryableRequest == null) { refreshTimer = 0; Execute(() => Client.Refresh()); }
        }
        private void OnDestroy() { Transport?.Dispose(); if (ownsBoot && boot != null) Destroy(boot.gameObject); if (font != null) Destroy(font); Ready = false; }
        private void Execute(Action action) { try { Error = null; action(); } catch (Exception e) { Error = e.Message; } }
        public void Reconnect() => Execute(() => Transport.Connect(Port));
        public void OpenTab(int tab) { Tab = Mathf.Clamp(tab, 0, 3); scroll = Vector2.zero; confirm = null; }
        public void ShowAt(int tab) { OpenTab(tab); Visible = true; }
        public void ReturnToWorld() { confirm = null; Visible = false; }
        public void Buy(int offer) => Execute(() => Client.Request(WorkshopCommand.Buy, offer));
        public void Feed(long instance, int food) => Execute(() => Client.Request(WorkshopCommand.Feed, food, new[] { instance }));
        public void Refine(long instance) => Execute(() => Client.Request(WorkshopCommand.Refine, instances: new[] { instance }));
        public void Fuse(int recipe, long[] ids) => Execute(() => Client.Request(WorkshopCommand.Fuse, recipe, ids));
        public void Equip(long[] ids) => Execute(() => Client.Request(WorkshopCommand.Equip, instances: ids));
        private void Ask(string message, Action action) { confirmation = message; confirm = action; }
        private bool CanWrite => Client != null && Client.Ready && Client.RetryableRequest == null && !Client.Snapshot.BattleActive;
        private void Styles()
        {
            if (title != null) return;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Noto Sans CJK SC", "Arial" }, 20);
            text = new GUIStyle(GUI.skin.label) { font = font, fontSize = 20, wordWrap = true }; text.normal.textColor = new Color(.87f,.92f,.85f);
            small = new GUIStyle(text) { fontSize = 16 }; small.normal.textColor = new Color(.63f,.75f,.70f);
            title = new GUIStyle(text) { fontSize = 34, fontStyle = FontStyle.Bold }; title.normal.textColor = new Color(.95f,.81f,.49f);
            heading = new GUIStyle(title) { fontSize = 24 };
            button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 19, fixedHeight = 48, wordWrap = true };
            box = new GUIStyle(GUI.skin.box) { padding = new RectOffset(16,16,12,12), margin = new RectOffset(0,0,6,10) };
        }
        private void OnGUI()
        {
            if (!Visible) return;
            Styles(); float scale = Mathf.Min(Screen.width / 900f, Screen.height / 1600f);
            var previous = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            var oldColor = GUI.color; GUI.color = new Color(.025f, .045f, .04f, .98f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width / scale, Screen.height / scale), Texture2D.whiteTexture); GUI.color = oldColor;
            float left = (Screen.width / scale - 900) / 2;
            GUILayout.BeginArea(new Rect(left + 24, 20, 852, 1560));
            if (CanReturnToWorld && GUILayout.Button("返回集市地图", button)) ReturnToWorld();
            GUILayout.Label("养  ·  用  ·  炼", title);
            GUILayout.Label("LOCAL 本地联网 / 内存存档，服务端重启清空", small);
            var s = Client?.Snapshot;
            GUILayout.BeginHorizontal();
            GUILayout.Label(s == null ? "等待服务器角色资料" : "元石 " + s.Wallet.YuanShi + "    仙元石 " + s.Wallet.XianYuanShi + "    " + (s.Rank == 0 ? "未开窍" : s.Rank + "转"), heading);
            GUI.enabled = Ready; if (GUILayout.Button("重连", button, GUILayout.Width(92))) Reconnect(); GUI.enabled = true; GUILayout.EndHorizontal();
            if (Transport?.Cultivation?.Snapshot != null && !Transport.Cultivation.Snapshot.Awakened)
            {
                GUI.enabled = Transport.Cultivation.Ready;
                if (GUILayout.Button("请求开窍 · 获得一转修为后可购蛊", button)) Execute(() => Transport.Cultivation.Awaken()); GUI.enabled = true;
            }
            GUILayout.Label(Client?.Status ?? "加载业务目录…", text);
            if (Error != null) GUILayout.Label(Error, text);
            if (Client?.RetryableRequest != null)
            {
                GUI.enabled = Client.Connected && Client.Pending == null;
                if (GUILayout.Button("查询 / 重试原操作（不重新抽取）", button)) Execute(() => Client.Retry()); GUI.enabled = true;
            }
            GUILayout.BeginHorizontal(); var tabs = new[] { "坊市", "蛊囊 / 喂养", "炼蛊", "组合" };
            for (int i = 0; i < tabs.Length; i++) if (GUILayout.Button((Tab == i ? "● " : "") + tabs[i], button)) OpenTab(i);
            GUILayout.EndHorizontal();
            if (Catalog != null && s != null)
            {
                scroll = GUILayout.BeginScrollView(scroll);
                if (Tab == 0) Shop(); else if (Tab == 1) Inventory(); else if (Tab == 2) Refining(); else Loadout();
                GUILayout.EndScrollView();
            }
            if (confirm != null)
            {
                GUILayout.BeginVertical(box); GUILayout.Label("确认本次操作", heading); GUILayout.Label(confirmation, text);
                GUILayout.BeginHorizontal(); GUI.enabled = CanWrite;
                if (GUILayout.Button("确认执行", button)) { var action = confirm; confirm = null; action(); }
                GUI.enabled = true; if (GUILayout.Button("取消", button)) confirm = null;
                GUILayout.EndHorizontal(); GUILayout.EndVertical();
            }
            GUILayout.Label("商店价格与概率为联调配置；操作等待服务端确认。自动跑图不代替购买、喂养或炼制确认。", small);
            GUILayout.EndArea(); GUI.enabled = true; GUI.matrix = previous;
        }
        private void Shop()
        {
            GUILayout.Label("坊市掌柜", heading); GUILayout.Label("蛊虫购入后仍是野生状态；炼化后才可装备。", text);
            foreach (var o in Catalog.Offers.Values.OrderBy(x => x.Id))
            {
                GUILayout.BeginVertical(box);
                string name = o.Gu != 0 ? Catalog.Gu.Get(o.Gu).Name + " · 野生" : Catalog.Items[o.Item].Name + " × " + o.Quantity;
                GUILayout.Label(name, heading); GUILayout.Label("价格 " + o.YuanShi + " 元石", text);
                GUI.enabled = CanWrite;
                if (GUILayout.Button("购买", button)) { int id = o.Id; Ask("购买 " + name + "，消耗 " + o.YuanShi + " 元石。", () => Buy(id)); }
                GUI.enabled = true; GUILayout.EndVertical();
            }
        }
        private string Name(GuInstance g) => Catalog.Gu.Get(g.DefinitionId).Name + "  #" + g.Id;
        private string State(GuInstance g)
        {
            long remaining = Math.Max(0, g.FedUntil - Client.Snapshot.ServerSeconds);
            return (g.Refined ? "已炼化" : "野生") + " · " + (remaining == 0 ? "休眠，需补喂" : "饱食剩余 " + (remaining / 3600) + "小时" + (remaining % 3600 / 60) + "分");
        }
        private void Inventory()
        {
            GUILayout.Label("蛊囊  " + Client.Snapshot.Inventory.Count + " / " + WorkshopSnapshot.MaxInstances, heading);
            if (Client.Snapshot.Inventory.Count == 0) GUILayout.Label("蛊囊为空，先到坊市购入凡蛊。", text);
            foreach (var g in Client.Snapshot.Inventory)
            {
                var rule = Catalog.Care[g.DefinitionId]; GUILayout.BeginVertical(box); GUILayout.Label(Name(g), heading); GUILayout.Label(State(g), text);
                GUILayout.Label("食物：" + Catalog.Items[rule.Food].Name + " × " + rule.FoodCount, small);
                GUI.enabled = CanWrite;
                if (GUILayout.Button("补喂", button)) { long id = g.Id; int food = rule.Food; Ask("给 " + Name(g) + " 喂食 " + Catalog.Items[food].Name + " × " + rule.FoodCount + "，补满饱食时长。", () => Feed(id, food)); }
                GUI.enabled = true; GUILayout.EndVertical();
            }
            GUILayout.Label("食料与炼材", heading);
            foreach (var m in Client.Snapshot.Materials) GUILayout.Label(Catalog.Items[m.Id].Name + " × " + m.Count, text);
        }
        private static string Odds(int success, int destroy) => "成功 " + (success / 100f).ToString("0.##") + "% · 失败保留 " + ((10000-success-destroy)/100f).ToString("0.##") + "% · 毁蛊 " + (destroy/100f).ToString("0.##") + "%";
        private void Refining()
        {
            GUILayout.Label("单炼 · 炼为己用", heading);
            foreach (var g in Client.Snapshot.Inventory.Where(x => !x.Refined))
            {
                var rule = Catalog.Care[g.DefinitionId]; GUILayout.BeginVertical(box); GUILayout.Label(Name(g), heading); GUILayout.Label(State(g), small);
                GUILayout.Label(Odds(rule.Success, rule.Destroy) + "\n每次 " + rule.RefineFee + " 元石", text); GUI.enabled = CanWrite;
                if (GUILayout.Button("炼化这只蛊", button)) { long id = g.Id; Ask(Name(g) + "：" + Odds(rule.Success, rule.Destroy) + "。本次消耗 " + rule.RefineFee + " 元石，失败可能毁掉所选蛊。", () => Refine(id)); }
                GUI.enabled = true; GUILayout.EndVertical();
            }
            foreach (var recipe in Catalog.Recipes.Values)
            {
                GUILayout.BeginVertical(box); GUILayout.Label(recipe.Name, heading);
                string materialText = string.Join(" + ", recipe.Materials.Select(m => Catalog.Items[m.Id].Name + " × " + m.Count));
                GUILayout.Label(string.Join(" + ", recipe.Ingredients.Select(i => Catalog.Gu.Get(i.Id).Name + " × " + i.Count)) + " → " + Catalog.Gu.Get(recipe.Output).Name, text);
                GUILayout.Label("需要 " + recipe.MinimumRank + "转 · " + recipe.YuanShi + " 元石" + (materialText.Length == 0 ? "" : " + " + materialText), text);
                GUILayout.Label(Odds(recipe.Success, recipe.Destroy), text);
                ingredients.RemoveAll(id => !Client.Snapshot.Inventory.Any(g => g.Id == id));
                foreach (var g in Client.Snapshot.Inventory.Where(x => recipe.Ingredients.Any(i => i.Id == x.DefinitionId)))
                    if (GUILayout.Button((ingredients.Contains(g.Id) ? "☑ " : "□ ") + Name(g) + " · " + State(g), button))
                    { if (ingredients.Contains(g.Id)) ingredients.Remove(g.Id); else if (ingredients.Count < 6) ingredients.Add(g.Id); }
                GUI.enabled = CanWrite;
                if (GUILayout.Button("核对材料并合炼", button))
                {
                    var ids = ingredients.ToArray(); int recipeId = recipe.Id;
                    Ask("投入实例 #" + string.Join(", #", ids) + "。成功消耗全部投入蛊；失败可能随机毁掉其中一只。每次扣 " + recipe.YuanShi + " 元石及 " + materialText + "。" + Odds(recipe.Success, recipe.Destroy), () => Fuse(recipeId, ids));
                }
                GUI.enabled = true; GUILayout.Label(recipe.Source, small); GUILayout.EndVertical();
            }
            if (Client.LastResponse != null && Client.LastResponse.RemovedInstances.Count > 0) GUILayout.Label("最近消耗/损失的实例：#" + string.Join(", #", Client.LastResponse.RemovedInstances), text);
        }
        private void Loadout()
        {
            var s = Client.Snapshot; GUILayout.Label("组合 " + s.Loadout.Count + " / 6", heading); GUILayout.Label(Catalog.Gu.Describe(Catalog.ActiveProfile(s)), text);
            GUILayout.Label("同系列至多一只；只有已炼化、未休眠且修为足够的蛊可加入。休眠蛊保留槽位但不计入有效流派。", small);
            foreach (var g in s.Inventory)
            {
                bool equipped = s.Loadout.Contains(g.Id); GUILayout.BeginVertical(box); GUILayout.Label(Name(g), heading); GUILayout.Label(State(g), text); GUI.enabled = CanWrite;
                if (GUILayout.Button(equipped ? "卸下" : "加入组合", button))
                { var next = s.Loadout.Where(id => id != g.Id).ToList(); if (!equipped) next.Add(g.Id); Equip(next.ToArray()); }
                GUI.enabled = true; GUILayout.EndVertical();
            }
        }
    }
}
