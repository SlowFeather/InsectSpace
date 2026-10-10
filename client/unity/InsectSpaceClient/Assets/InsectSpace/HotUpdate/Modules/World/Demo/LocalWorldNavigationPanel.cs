using UnityEngine;
using UnityEngine.SceneManagement;

namespace InsectSpace.Gameplay.Demo
{
    public sealed class LocalWorldNavigationPanel : MonoBehaviour
    {
        public LocalNavigationMotor Motor;
        public Camera View;
        public Transform[] Stations;
        public Transform[] Encounters;
        public int WorkshopPort = 7779;
        public string MoonlightSceneName = "LocalMoonlightBattle";
        public InsectSpace.Gameplay.GuWorkshop.LocalWorkshopPanel Workshop { get; private set; }
        public bool WorkshopVisible => Workshop != null && Workshop.Visible;
        private readonly string[] names = { "坊市掌柜", "饲蛊师", "炼蛊工坊" };
        private readonly string[] hints = { "购入蛊虫、食料和炼材", "照料蛊虫 · 补喂恢复", "单炼与合炼 · 炼为己用" };
        public bool AutoTravel => Motor != null && Motor.AutoTravel;
        public int CurrentTask { get; private set; }
        private Font font;
        private GUIStyle title, body, small, button, label;
        private Vector2 stick;
        private bool dragging, up, down, left, right, touchDragging;
        private Vector3 cameraOffset;
        private bool cameraReady;
        private float Scale => Mathf.Min(Screen.width / 900f, Screen.height / 1600f);
        private float UiHeight => Screen.height / Scale;
        private float UiLeft => (Screen.width / Scale - 900) / 2;

        private void Awake()
        {
            // This local scene is the portrait interaction prototype. Production platform
            // orientation remains owned by the platform team and its release gates.
            Screen.autorotateToPortrait = true;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = false;
            Screen.autorotateToLandscapeRight = false;
            Screen.orientation = ScreenOrientation.Portrait;
        }

        private void Update()
        {
            if (WorkshopVisible) return;
            if (Motor == null || !Motor.Ready || Input.touchCount == 0)
            {
                if (touchDragging) { touchDragging = false; stick = Vector2.zero; }
                return;
            }
            var touch = Input.GetTouch(0);
            var logical = new Vector2(touch.position.x / Scale, (Screen.height - touch.position.y) / Scale);
            var pad = new Rect(UiLeft + 24, UiHeight - 230, 228, 182);
            if (touch.phase == TouchPhase.Began)
            {
                if (pad.Contains(logical)) { touchDragging = true; Motor.CancelTravel(); }
                else if (logical.y > 248 && logical.y < UiHeight - 354) MoveAtScreenPoint(touch.position);
            }
            if (touchDragging && (touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary))
                stick = Vector2.ClampMagnitude(new Vector2(logical.x - pad.center.x, pad.center.y - logical.y) / 60, 1);
            if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
            { touchDragging = false; stick = Vector2.zero; }
        }

        private void MoveAtScreenPoint(Vector2 pixel)
        {
            var plane = new Plane(Vector3.up, Vector3.zero);
            var ray = View.ScreenPointToRay(pixel);
            if (plane.Raycast(ray, out float distance)) Motor.TravelTo(ray.GetPoint(distance), false);
        }

        private void LateUpdate()
        {
            if (Motor == null || View == null || !Motor.Ready) return;
            if (!cameraReady) { cameraOffset = View.transform.position - Motor.Position; cameraReady = true; }
            View.transform.position = Vector3.Lerp(View.transform.position, Motor.Position + cameraOffset, 1 - Mathf.Exp(-7 * Time.deltaTime));
            if (WorkshopVisible) return;
            Vector2 input = (dragging || touchDragging) ? stick : new Vector2((right ? 1 : 0) - (left ? 1 : 0), (up ? 1 : 0) - (down ? 1 : 0));
            var forward = View.transform.forward; forward.y = 0; forward.Normalize(); var side = View.transform.right; side.y = 0; side.Normalize();
            Motor.ManualMove(side * input.x + forward * input.y);
        }
        public bool TravelToStation(int index)
        {
            if (WorkshopVisible || Motor == null || Stations == null || index < 0 || index >= Stations.Length || Stations[index] == null) return false;
            ClearInput(); if (!Motor.TravelTo(Stations[index].position, true)) return false; CurrentTask = index; return true;
        }
        public int NearbyStation
        {
            get
            {
                if (Motor == null || !Motor.Ready || Motor.AutoTravel || Stations == null) return -1;
                int nearest = -1; float distance = 1.8f * 1.8f;
                for (int i = 0; i < Stations.Length && i < names.Length; i++)
                {
                    if (Stations[i] == null) continue;
                    float candidate = (Stations[i].position - Motor.Position).sqrMagnitude;
                    if (candidate <= distance) { distance = candidate; nearest = i; }
                }
                return nearest;
            }
        }
        public bool OpenNearbyStation()
        {
            int index = NearbyStation;
            if (index < 0 || WorkshopVisible) return false;
            CurrentTask = index; ClearInput(); Motor.CancelTravel();
            if (Workshop == null)
            {
                var host = new GameObject("NPC Workshop - LOCAL TCP"); host.transform.SetParent(transform, false);
                Workshop = host.AddComponent<InsectSpace.Gameplay.GuWorkshop.LocalWorkshopPanel>();
                Workshop.Port = WorkshopPort; Workshop.CanReturnToWorld = true;
            }
            Workshop.ShowAt(index); return true;
        }
        public int NearbyEncounter
        {
            get
            {
                if (Motor == null || !Motor.Ready || Motor.AutoTravel || Encounters == null) return -1;
                for (int i = 0; i < Encounters.Length; i++)
                {
                    if (Encounters[i] != null && (Encounters[i].position - Motor.Position).sqrMagnitude <= 2.2f * 2.2f) return i;
                }
                return -1;
            }
        }
        public bool EnterNearbyEncounter()
        {
            if (NearbyEncounter < 0 || WorkshopVisible || string.IsNullOrWhiteSpace(MoonlightSceneName)) return false;
            ClearInput(); Motor.CancelTravel();
            SceneManager.LoadScene(MoonlightSceneName);
            return true;
        }
        private void ClearInput() { dragging = touchDragging = up = down = left = right = false; stick = Vector2.zero; }
        private void OnApplicationFocus(bool focused) { if (!focused) { ClearInput(); Motor?.ManualMove(Vector3.zero); } }
        private void OnDestroy() { if (font != null) Destroy(font); }
        private void Styles()
        {
            if (title != null) return;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Noto Sans CJK SC", "Arial" }, 22);
            body = new GUIStyle(GUI.skin.label) { font = font, fontSize = 23, wordWrap = true }; body.normal.textColor = new Color(.91f, .94f, .86f);
            small = new GUIStyle(body) { fontSize = 18 }; small.normal.textColor = new Color(.69f, .8f, .75f);
            title = new GUIStyle(body) { fontSize = 36, fontStyle = FontStyle.Bold }; title.normal.textColor = new Color(.98f, .82f, .5f);
            button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 22, wordWrap = true }; label = new GUIStyle(body) { alignment = TextAnchor.MiddleCenter, fontSize = 20 };
        }
        private static void Fill(Rect r, Color color) { var old = GUI.color; GUI.color = color; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = old; }
        private void OnGUI()
        {
            if (WorkshopVisible) return;
            Styles(); float scale = Scale, x = UiLeft, h = UiHeight; var old = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            Fill(new Rect(x + 24, 24, 852, 214), new Color(.035f, .09f, .085f, .94f)); GUI.Label(new Rect(x + 48, 40, 740, 56), "青岚坊 · 山间集市", title);
            GUI.Label(new Rect(x + 48, 97, 760, 30), "LOCAL 探索样例 / 斜俯视移动与寻路", small);
            string status = AutoTravel ? "正在前往：" + names[CurrentTask] + "\n触碰摇杆可随时接管" : Motor != null && Motor.Arrived ? "已到达：" + names[CurrentTask] + " · 等待玩家操作" : "点击地面移动，或拖动左下方摇杆";
            GUI.Label(new Rect(x + 48, 144, 750, 70), status, body); if (Motor?.Error != null) GUI.Label(new Rect(x + 48, 250, 750, 80), Motor.Error, body);
            if (Stations != null && View != null) for (int i = 0; i < Stations.Length && i < names.Length; i++)
            { var p = View.WorldToScreenPoint(Stations[i].position + Vector3.up * 2.4f); if (p.z <= 0) continue; var r = new Rect(p.x / scale - 100, (Screen.height - p.y) / scale - 18, 200, 36); if (r.y < 248 || r.yMax > h - 354) continue; Fill(r, new Color(.03f, .07f, .06f, .85f)); GUI.Label(r, names[i], label); }
            Fill(new Rect(x + 24, h - 350, 852, 104), new Color(.035f, .09f, .085f, .94f));
            for (int i = 0; i < names.Length; i++) if (GUI.Button(new Rect(x + 36 + i * 280, h - 340, 270, 84), names[i] + "\n自动前往", button)) TravelToStation(i);
            Fill(new Rect(x + 300, h - 226, 576, 178), new Color(.035f, .09f, .085f, .9f)); GUI.Label(new Rect(x + 322, h - 214, 528, 36), hints[CurrentTask], body); GUI.Label(new Rect(x + 322, h - 168, 528, 62), "跑图只负责到达。购买、喂养、炼制均需手动确认。", small);
            if (GUI.Button(new Rect(x + 322, h - 92, 170, 36), "停止移动", button)) { ClearInput(); Motor?.CancelTravel(); }
            int nearby = NearbyStation; int encounter = NearbyEncounter;
            GUI.enabled = encounter >= 0 || nearby >= 0;
            string interaction = encounter >= 0 ? "遭遇野怪 · 进入月光战斗" : nearby < 0 ? "靠近 NPC 后交互" : "与" + names[nearby] + "交互";
            if (GUI.Button(new Rect(x + 505, h - 98, 345, 48), interaction, button))
            {
                if (encounter >= 0) EnterNearbyEncounter(); else OpenNearbyStation();
            }
            GUI.enabled = true;
            var pad = new Rect(x + 24, h - 230, 228, 182); Fill(pad, new Color(.035f, .09f, .085f, .75f)); var center = pad.center; Fill(new Rect(center.x - 2, center.y - 64, 4, 128), new Color(.4f, .55f, .5f, .4f)); Fill(new Rect(center.x - 64, center.y - 2, 128, 4), new Color(.4f, .55f, .5f, .4f)); Fill(new Rect(center.x + stick.x * 60 - 24, center.y - stick.y * 60 - 24, 48, 48), new Color(.82f, .71f, .42f, .9f)); HandleInput(Event.current, pad, h, scale);
            GUI.matrix = old;
        }
        private void HandleInput(Event e, Rect pad, float height, float scale)
        {
            if (Motor == null || !Motor.Ready) return;
            if (e.type == EventType.KeyDown || e.type == EventType.KeyUp) { bool pressed = e.type == EventType.KeyDown; switch (e.keyCode) { case KeyCode.W: case KeyCode.UpArrow: up = pressed; break; case KeyCode.S: case KeyCode.DownArrow: down = pressed; break; case KeyCode.A: case KeyCode.LeftArrow: left = pressed; break; case KeyCode.D: case KeyCode.RightArrow: right = pressed; break; default: return; } e.Use(); }
            if (e.type == EventType.MouseDown && e.button == 0) { if (pad.Contains(e.mousePosition)) { dragging = true; Motor.CancelTravel(); e.Use(); } else if (e.mousePosition.y > 248 && e.mousePosition.y < height - 354) { var pixel = new Vector2(e.mousePosition.x * scale, Screen.height - e.mousePosition.y * scale); MoveAtScreenPoint(pixel); e.Use(); } }
            if (dragging && (e.type == EventType.MouseDrag || e.type == EventType.Used)) stick = Vector2.ClampMagnitude(new Vector2(e.mousePosition.x - pad.center.x, pad.center.y - e.mousePosition.y) / 60, 1);
            if (e.type == EventType.MouseUp) { dragging = false; stick = Vector2.zero; }
        }
    }
}

