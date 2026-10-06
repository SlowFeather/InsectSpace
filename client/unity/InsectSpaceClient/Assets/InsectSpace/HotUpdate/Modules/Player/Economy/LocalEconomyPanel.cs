using System;
using System.Collections;
using Framework;
using Framework.Network;
using InsectSpace.Client;
using InsectSpace.Economy;
using InsectSpace.BattleEconomy;
using UnityEngine;

namespace InsectSpace.Gameplay.Economy
{
    // Opt-in local Unity scene. Production has no automatic connection or fixture account.
    public sealed class LocalEconomyPanel : MonoBehaviour
    {
        public int Port = 7779;
        public bool Ready { get; private set; }
        public string Error { get; private set; }
        public LocalEconomyTcpClient Transport { get; private set; }
        private InsectSpaceBootstrap boot;
        private bool ownsBoot;
        private string yuanShi = "20", xianYuanShi = "2";
        private Font font;
        private GUIStyle heading, body, card, button, input;
        private Vector2 scroll;
        private IEnumerator Start()
        {
#if !UNITY_EDITOR
            Error = "LOCAL ECONOMY is an Editor-only development scene.";
            yield break;
#else
            BootConfiguration config = null;
            try
            {
                config = BootConfiguration.Load(); config.Validate();
                if (!config.editorSimulate || !config.localSmokeMode || config.useWeChatSdk)
                    throw new InvalidOperationException("经济联调需要明确的 Editor LOCAL SMOKE 配置。");
            }
            catch (Exception ex) { Error = ex.Message; }
            if (Error != null) yield break;
            boot = FindObjectOfType<InsectSpaceBootstrap>();
            if (boot == null) { ownsBoot = true; boot = new GameObject("Economy Bootstrap - LOCAL ONLY").AddComponent<InsectSpaceBootstrap>(); }
            float deadline = Time.realtimeSinceStartup + 45;
            while (!boot.Ready && boot.LastError == null && Time.realtimeSinceStartup < deadline) yield return null;
            if (!boot.Ready) { Error = boot.LastError ?? "Bootstrap timeout"; yield break; }
            Transport = new LocalEconomyTcpClient(GameFrameworkEntry.GetModule<INetworkManager>());
            Ready = true;
            Execute(() => Transport.Connect(Port));
#endif
        }
        private void Update() { if (Ready) Transport.Tick(Time.unscaledDeltaTime); }
        private void OnDestroy()
        {
            Transport?.Dispose(); Ready = false;
            if (ownsBoot && boot != null) Destroy(boot.gameObject);
            if (font != null) Destroy(font);
        }
        public void Prepare(long yuan, long xian) => Execute(() => Transport.Client.Prepare(new StoneAmounts(yuan, xian)));
        public void Refresh() => Execute(() => Transport.Client.Refresh());
        public void CancelReserve() => Execute(() => Transport.Client.Cancel());
        public void SpendWorld(bool premium) => Execute(() => Transport.Client.WorldAction(premium ? "local-world-premium" : "local-world-skill", 1));
        public void BeginBattle() => Execute(() => Transport.BeginBattle());
        public void BattleAction(ResourceAction action) => Execute(() => Transport.Battle.Send(action));
        private void Execute(Action action)
        { try { Error = null; action(); } catch (Exception ex) { Error = ex.Message; } }
        private void OnGUI()
        {
            if (heading == null)
            {
                font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Noto Sans CJK SC", "Arial" }, 18);
                heading = new GUIStyle(GUI.skin.label) { font = font, fontSize = 27, fontStyle = FontStyle.Bold, wordWrap = true };
                heading.normal.textColor = new Color(.87f, .78f, .49f);
                body = new GUIStyle(GUI.skin.label) { font = font, fontSize = 17, wordWrap = true };
                body.normal.textColor = new Color(.88f, .91f, .94f);
                card = new GUIStyle(GUI.skin.box) { padding = new RectOffset(18, 18, 12, 12), margin = new RectOffset(0, 0, 8, 8) };
                button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 17, fixedHeight = 38 };
                input = new GUIStyle(GUI.skin.textField) { font = font, fontSize = 18, fixedHeight = 32 };
            }
            float scale = Mathf.Max(.45f, Mathf.Min(Screen.width / 1000f, Screen.height / 780f));
            Matrix4x4 old = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            GUILayout.BeginArea(new Rect(25, 18, Screen.width / scale - 50, Screen.height / scale - 36));
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("元石 · 仙元石   /   经济通讯实验室", heading);
            GUILayout.Label("LOCAL DEVELOPMENT  ·  真实 TCP + KCP  ·  内存数据  ·  模拟充值（不是真实支付）", body);
            GUILayout.Label("连接 127.0.0.1:" + Port + "   本地玩家 1 / local-home / local-world · Epoch 1", body);
            GUILayout.Space(8);
            var client = Transport?.Client; var snapshot = client?.Snapshot;
            GUILayout.BeginVertical(card);
            GUILayout.Label(snapshot == null ? "钱包：等待服务器快照" : "可用钱包     元石  " + snapshot.Wallet.YuanShi + "        仙元石  " + snapshot.Wallet.XianYuanShi, heading);
            GUILayout.Label(snapshot == null ? "储备：不可用" : "战斗托管     元石  " + snapshot.Reserve.YuanShi + "        仙元石  " + snapshot.Reserve.XianYuanShi + "        " + snapshot.Phase, body);
            GUILayout.Label(snapshot == null ? "仙元：不可用" : "空窍仙元     " + snapshot.ImmortalEssence + " / " + snapshot.EssenceCapacity + "        快照版本  " + snapshot.Revision, body);
            if (snapshot != null && snapshot.Phase != ReservePhase.None)
                GUILayout.Label("储备 ID  " + snapshot.ReserveId + "     战斗房间  " + (snapshot.RoomId.Length == 0 ? "尚未开战" : snapshot.RoomId), body);
            GUILayout.EndVertical();
            GUILayout.Label(Ready ? client.Status : "等待客户端启动…", body);
            if (Error != null) GUILayout.Label("错误：" + Error, body);
            GUILayout.BeginHorizontal();
            GUI.enabled = Ready;
            if (GUILayout.Button("连接 / 重连本地服务", button)) Execute(() => Transport.Connect(Port));
            GUI.enabled = client != null && client.Connected && client.Pending == null;
            if (GUILayout.Button("刷新服务器快照", button)) Refresh();
            GUI.enabled = client != null && client.Connected && client.Pending == null && client.RetryableRequest != null;
            if (GUILayout.Button("重试未确认操作", button)) Execute(() => client.Retry());
            GUILayout.EndHorizontal(); GUI.enabled = true;
            GUILayout.BeginVertical(card);
            GUILayout.Label("准备战斗储备", heading);
            GUILayout.Label("准备时从钱包转入托管；未开战可取消。断线不会自动退款。", body);
            GUILayout.BeginHorizontal();
            GUILayout.Label("元石", body, GUILayout.Width(60)); yuanShi = GUILayout.TextField(yuanShi, 19, input, GUILayout.Width(170));
            GUILayout.Label("仙元石", body, GUILayout.Width(75)); xianYuanShi = GUILayout.TextField(xianYuanShi, 19, input, GUILayout.Width(170));
            GUI.enabled = client != null && client.Ready && client.RetryableRequest == null && snapshot.Phase == ReservePhase.None;
            if (GUILayout.Button("转入储备", button))
            {
                if (long.TryParse(yuanShi, out long yuan) && long.TryParse(xianYuanShi, out long xian)) Prepare(yuan, xian);
                else Error = "请输入非负整数数量。";
            }
            GUI.enabled = client != null && client.Ready && client.RetryableRequest == null && snapshot.Phase == ReservePhase.Prepared;
            if (GUILayout.Button("取消并退回", button)) CancelReserve();
            GUI.enabled = true; GUILayout.EndHorizontal(); GUILayout.EndVertical();
            GUILayout.BeginVertical(card);
            GUILayout.Label("KCP 帧同步资源战斗", heading);
            var battle = Transport?.Battle;
            var frame = battle?.Replica?.Snapshot;
            GUILayout.Label(battle == null ? "等待启动" : battle.Status, body);
            if (frame != null)
                GUILayout.Label("权威帧 " + frame.Frame + "   仙元 " + frame.Essence + "/" + frame.Capacity +
                    "   随身储备 " + frame.Remaining.YuanShi + " 元石 / " + frame.Remaining.XianYuanShi + " 仙元石   hash " + frame.Hash.ToString("X16"), body);
            GUILayout.BeginHorizontal();
            GUI.enabled = client != null && client.Ready && client.RetryableRequest == null && !battle.Busy && !battle.Ready && !battle.Pending.HasValue;
            if (GUILayout.Button("进入 / 恢复战斗", button)) BeginBattle();
            GUI.enabled = battle != null && battle.Ready;
            if (GUILayout.Button("施法：30 仙元", button)) BattleAction(ResourceAction.CastSkill);
            if (GUILayout.Button("使用 1 元石储备", button)) BattleAction(ResourceAction.UseYuanShiReserve);
            if (GUILayout.Button("使用 1 仙元石储备", button)) BattleAction(ResourceAction.UseXianYuanShiReserve);
            if (GUILayout.Button("撤离并结算", button)) BattleAction(ResourceAction.LeaveBattle);
            GUI.enabled = true; GUILayout.EndHorizontal();
            GUILayout.Label("服务器分配房间、确认输入并按 20 Hz 发送资源帧；客户端收到后重放并核对 hash。撤离后自动刷新钱包。", body);
            GUILayout.EndVertical();
            GUILayout.BeginVertical(card);
            GUILayout.Label("TCP 大世界消耗", heading);
            GUILayout.Label("价格由服务器决定；操作确认后立即消耗钱包，战斗结束不返还。以下仅为联调价格。", body);
            GUILayout.BeginHorizontal();
            GUI.enabled = client != null && client.Ready && client.RetryableRequest == null && snapshot.Phase != ReservePhase.InBattle;
            if (GUILayout.Button("世界技能：3 元石", button)) SpendWorld(false);
            if (GUILayout.Button("高级动作：1 仙元石", button)) SpendWorld(true);
            GUI.enabled = true; GUILayout.EndHorizontal(); GUILayout.EndVertical();
            GUILayout.Label("帧同步：普通技能只耗空窍仙元；显式补充才使用随身储备。服务器结算后剩余带出。", body);
            GUILayout.Label("服务端控制台：help 查看活动奖励与模拟充值。网络战斗请使用上方按钮；掉线不自动退款。", body);
            GUILayout.Label("启动服务：./tools/Run-LocalServer.ps1 -LocalEconomy -EconomyConsole", body);
            GUILayout.EndScrollView(); GUILayout.EndArea(); GUI.matrix = old;
        }
    }
}
