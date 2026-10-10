using System;
using System.Collections;
using Framework;
using Framework.Network;
using InsectSpace.Client;
using InsectSpace.Economy;
using InsectSpace.Gameplay.Economy;
using InsectSpace.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace InsectSpace.Gameplay.Moonlight
{
    // Editor-only local battle presentation. World navigation owns the map;
    // this panel only presents the current server-authoritative PvE room.
    public sealed class LocalMoonlightBattlePanel : MonoBehaviour
    {
        public int Port = 7779;
        public bool Ready { get; private set; }
        public string Error { get; private set; }
        public LocalEconomyTcpClient Transport { get; private set; }
        public bool ReturnRequested { get; private set; }
        public string ReturnSceneName = "LocalWorldNavigation";
        private InsectSpaceBootstrap boot;
        private bool ownsBoot;
        private Font font;
        private GUIStyle title, body, button, card;
        private Vector2 scroll;
        private GameObject player, monster;
        private Camera view;

        private IEnumerator Start()
        {
#if !UNITY_EDITOR
            Error = "LOCAL MOONLIGHT is an Editor-only development scene.";
            yield break;
#else
            Screen.autorotateToPortrait = true; Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = false; Screen.autorotateToLandscapeRight = false; Screen.orientation = ScreenOrientation.Portrait;
            try
            {
                var config = BootConfiguration.Load(); config.Validate();
                if (!config.editorSimulate || !config.localSmokeMode || config.useWeChatSdk)
                    throw new InvalidOperationException("月光战斗联调需要明确的 Editor LOCAL SMOKE 配置。");
            }
            catch (Exception ex) { Error = ex.Message; yield break; }
            boot = FindObjectOfType<InsectSpaceBootstrap>();
            if (boot == null) { ownsBoot = true; boot = new GameObject("Moonlight Bootstrap - LOCAL ONLY").AddComponent<InsectSpaceBootstrap>(); }
            float deadline = Time.realtimeSinceStartup + 45;
            while (!boot.Ready && boot.LastError == null && Time.realtimeSinceStartup < deadline) yield return null;
            if (!boot.Ready) { Error = boot.LastError ?? "Bootstrap timeout"; yield break; }
            try
            {
                Transport = new LocalEconomyTcpClient(GameFrameworkEntry.GetModule<INetworkManager>());
                Ready = true;
                Transport.Connect(Port);
                BuildArena();
            }
            catch (Exception ex) { Error = ex.Message; }
#endif
        }

        private void BuildArena()
        {
            view = Camera.main;
            if (view == null) view = new GameObject("Moonlight Camera").AddComponent<Camera>();
            view.transform.position = new Vector3(0, 8.5f, -8.5f);
            view.transform.rotation = Quaternion.Euler(42, 0, 0);
            view.fieldOfView = 48;
            player = CreateActor("月光蛊师", PrimitiveType.Capsule, new Vector3(-1.7f, 0, 0), new Color(.25f, .82f, .72f));
            monster = CreateActor("野怪", PrimitiveType.Sphere, new Vector3(1.7f, .7f, 0), new Color(.92f, .34f, .28f));
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Moonlight Arena Floor"; floor.transform.position = new Vector3(0, -.35f, 0); floor.transform.localScale = new Vector3(7, .3f, 5); Destroy(floor.GetComponent<Collider>());
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(.08f, .13f, .16f) }; floor.GetComponent<Renderer>().sharedMaterial = material;
        }
        private GameObject CreateActor(string name, PrimitiveType type, Vector3 position, Color color)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.position = position; Destroy(go.GetComponent<Collider>());
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color }; go.GetComponent<Renderer>().sharedMaterial = material; return go;
        }
        private void Update()
        {
            if (!Ready) return;
            Transport.Tick(Time.unscaledDeltaTime);
            var state = Transport.Moonlight?.Replica?.Snapshot;
            if (state != null && monster != null) monster.transform.localScale = Vector3.one * (.75f + .35f * state.MonsterHp / (float)state.Rules.MonsterMaxHp);
        }
        public void BeginBattle() => Execute(() => Transport.BeginMoonlightBattle());
        public void CastMoonBlade() => Execute(() => Transport.Moonlight.CastMoonBlade());
        public void ToggleAuto() => Execute(() => Transport.Moonlight.SetAutoCast(!(Transport.Moonlight.Replica?.Snapshot.AutoCast ?? false)));
        public void Retreat() => Execute(() => Transport.Moonlight.Retreat());
        public void ReturnToMap()
        {
            ReturnRequested = true;
            if (!string.IsNullOrWhiteSpace(ReturnSceneName)) SceneManager.LoadScene(ReturnSceneName);
            else gameObject.SetActive(false);
        }
        private void Execute(Action action) { try { Error = null; action(); } catch (Exception ex) { Error = ex.Message; } }

        private void OnGUI()
        {
            if (title == null)
            {
                font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Noto Sans CJK SC", "Arial" }, 18);
                title = new GUIStyle(GUI.skin.label) { font = font, fontSize = 30, fontStyle = FontStyle.Bold, wordWrap = true }; title.normal.textColor = new Color(.97f, .82f, .5f);
                body = new GUIStyle(GUI.skin.label) { font = font, fontSize = 18, wordWrap = true }; body.normal.textColor = new Color(.9f, .94f, .94f);
                button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 18, wordWrap = true, fixedHeight = 52 };
                card = new GUIStyle(GUI.skin.box) { padding = new RectOffset(16, 16, 12, 12) };
            }
            float scale = Mathf.Max(.48f, Mathf.Min(Screen.width / 900f, Screen.height / 1600f)); var old = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            float width = Screen.width / scale; float height = Screen.height / scale;
            GUILayout.BeginArea(new Rect(24, 20, width - 48, height - 40)); scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("月光蛊 · 月刃试炼", title); GUILayout.Label("LOCAL PvE · 1 名主角 vs 1 只野怪 · 服务器固定帧", body);
            if (Error != null) GUILayout.Label("错误：" + Error, body);
            var battle = Transport?.Moonlight; var state = battle?.Replica?.Snapshot;
            GUILayout.BeginVertical(card);
            GUILayout.Label(Ready ? (battle?.Status ?? "等待连接") : "正在启动本地服务…", body);
            if (state == null) GUILayout.Label("尚未进入战斗。地图遭遇后点击进入；本面板也可直接演示。", body);
            else
            {
                GUILayout.Label("主角 HP  " + state.PlayerHp + " / " + state.Rules.PlayerMaxHp + "    野怪 HP  " + state.MonsterHp + " / " + state.Rules.MonsterMaxHp, body);
                GUILayout.Label("月光资源  " + state.Resource + " / " + state.Rules.MaxResource + "    月刃冷却  " + state.MoonBladeCooldown + " 帧    自动释放  " + (state.AutoCast ? "开" : "关"), body);
                GUILayout.Label("帧 " + state.Frame + "    状态 " + state.Phase + "    hash " + state.Hash.ToString("X16"), body);
            }
            GUILayout.BeginHorizontal();
            GUI.enabled = Ready && battle != null && !battle.Busy && !battle.Ready && !battle.Finished; if (GUILayout.Button("进入 / 恢复战斗", button)) BeginBattle();
            GUI.enabled = battle != null && battle.Ready; if (GUILayout.Button("月刃", button)) CastMoonBlade(); if (GUILayout.Button(state != null && state.AutoCast ? "关闭自动" : "开启自动", button)) ToggleAuto(); if (GUILayout.Button("撤退", button)) Retreat();
            GUI.enabled = true; GUILayout.EndHorizontal();
            if (battle?.Finished == true) { GUILayout.Label("战斗结果：" + state.Phase, title); if (GUILayout.Button("返回地图", button)) ReturnToMap(); }
            GUILayout.EndVertical(); GUILayout.Label("自动跑图只负责移动；购买、喂养、炼制和月刃释放都由玩家确认。", body);
            GUILayout.EndScrollView(); GUILayout.EndArea(); GUI.matrix = old;
        }
        private void OnDestroy()
        {
            Transport?.Dispose(); if (player != null) Destroy(player); if (monster != null) Destroy(monster); if (ownsBoot && boot != null) Destroy(boot.gameObject); if (font != null) Destroy(font);
        }
    }
}
