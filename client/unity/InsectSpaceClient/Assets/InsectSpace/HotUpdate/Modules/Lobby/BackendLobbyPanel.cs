using System;
using System.Collections;
using System.Collections.Generic;
using InsectSpace.Client;
using InsectSpace.Contracts;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using YooAsset;
using YooSceneHandle = YooAsset.SceneHandle;

namespace InsectSpace.Gameplay.Modules
{
    internal sealed class BackendLobbyPanel : MonoBehaviour
    {
        private BootContext context;
        private BackendHttpClient client;
        private Canvas canvas;
        private Text title;
        private Text status;
        private Text resource;
        private Text error;
        private InputField phone;
        private InputField code;
        private Button requestCode;
        private Button login;
        private Button enterWorld;
        private Button resume;
        private Button logout;
        private BackendSessionCache sessionCache;
        private Font uiFont;
        private Scene loadedWorldScene;
        private Slider progress;
        private string token;
        private LoginResponse identity;
        private bool busy;
        private bool worldLoading;
        private YooSceneHandle worldScene;
        private GameObject ownedEvents;
        public bool Busy => busy;
        public string LastError { get; private set; }
        public bool WorldSceneLoaded => worldScene != null && worldScene.IsValid && worldScene.SceneObject.isLoaded;
        public bool RestoredFromCache { get; private set; }
        public bool HasCachedSession => sessionCache != null && sessionCache.Exists;

        public void Initialize(BootContext value)
        {
            context = value ?? throw new ArgumentNullException(nameof(value));
            client = new BackendHttpClient(this);
            BuildUi();
            if (!string.IsNullOrWhiteSpace(context.Configuration.identityUrl))
                sessionCache = new BackendSessionCache(context.Configuration.identityUrl);
            RefreshUi();
            if (!context.Configuration.localSmokeMode) RestoreCachedSession();
        }

        private void Update() => RefreshUi();

        private void BuildUi()
        {
            EnsureEventSystem();
            uiFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Noto Sans CJK SC", "Arial" }, 18);
            var canvasObject = new GameObject("Backend Lobby Canvas");
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            canvasObject.AddComponent<GraphicRaycaster>();

            var body = Panel(canvasObject.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(700f, 530f), new Color(0.035f, 0.05f, 0.08f, 0.96f));
            title = Label(body.transform, "INSECT SPACE", 28, TextAnchor.UpperLeft, new Vector2(30f, -28f), new Vector2(640f, 44f));
            status = Label(body.transform, "启动中", 16, TextAnchor.UpperLeft, new Vector2(30f, -78f), new Vector2(640f, 70f));
            resource = Label(body.transform, "资源", 14, TextAnchor.UpperLeft, new Vector2(30f, -145f), new Vector2(640f, 32f));
            progress = Slider(body.transform, new Vector2(30f, -178f), new Vector2(640f, 18f));
            phone = Input(body.transform, "手机号（带国家区号，以 + 开头）", new Vector2(30f, -225f));
            phone.gameObject.name = "PhoneNumber";
            code = Input(body.transform, "验证码（白名单可留空）", new Vector2(30f, -278f));
            code.gameObject.name = "VerificationCode";
            requestCode = Button(body.transform, "获取验证码", new Vector2(30f, -332f), new Vector2(190f, 42f), RequestPhoneCode);
            login = Button(body.transform, "手机号登录", new Vector2(235f, -332f), new Vector2(190f, 42f), Login);
            enterWorld = Button(body.transform, "进入世界", new Vector2(440f, -332f), new Vector2(190f, 42f), EnterWorld);
            resume = Button(body.transform, "重试 Token 登录", new Vector2(30f, -387f), new Vector2(300f, 42f), RestoreCachedSession);
            logout = Button(body.transform, "退出登录 / 清除缓存", new Vector2(340f, -387f), new Vector2(300f, 42f), Logout);
            error = Label(body.transform, "", 14, TextAnchor.UpperLeft, new Vector2(30f, -448f), new Vector2(640f, 58f));
            error.color = new Color(1f, 0.45f, 0.4f);
            foreach (var text in canvasObject.GetComponentsInChildren<Text>()) text.font = uiFont;
        }

        private void RefreshUi()
        {
            if (context == null) return;
            var local = context.Configuration.localSmokeMode;
            title.text = local ? "INSECT SPACE  ·  LOCAL SMOKE" : context.Configuration.localBackendMode ? "INSECT SPACE  ·  WSL DEV/TEST" : "INSECT SPACE  ·  ONLINE";
            status.text = "会话：" + context.Session.Phase +
                "\n玩家：" + (context.Session.PlayerId > 0 ? context.Session.PlayerId.ToString() : "未登录") +
                (identity == null ? "" : "\n归属区：" + identity.homeRealmId);
            resource.text = "资源：" + context.Resources.ProgressStage + "  " +
                Mathf.RoundToInt(context.Resources.Progress * 100f) + "%" +
                "  版本 " + (context.Resources.ActiveVersion ?? "准备中");
            progress.value = Mathf.Clamp01(context.Resources.Progress);
            phone.interactable = code.interactable = !busy && context.Session.Phase == SessionPhase.SignedOut;
            requestCode.interactable = !local && !busy && context.Session.Phase == SessionPhase.SignedOut;
            login.interactable = !local && !busy && context.Session.Phase == SessionPhase.SignedOut;
            enterWorld.interactable = !local && !busy && !worldLoading && context.Session.Phase == SessionPhase.Lobby;
            phone.gameObject.SetActive(!local);
            code.gameObject.SetActive(!local);
            requestCode.gameObject.SetActive(!local);
            login.gameObject.SetActive(!local);
            enterWorld.gameObject.SetActive(!local);
            resume.interactable = !busy && HasCachedSession && context.Session.Phase == SessionPhase.SignedOut;
            logout.interactable = !busy && (HasCachedSession || identity != null);
        }

        private void RequestPhoneCode()
        {
            if (busy) return;
            if (string.IsNullOrWhiteSpace(phone.text)) { error.text = "请先填写手机号。"; return; }
            busy = true; LastError = null; error.text = "正在请求验证码...";
            client.Post<PhoneCodeResponse>(context.Configuration.identityUrl, "v1/identity/phone/code",
                new PhoneCodeRequest { phoneNumber = NormalizePhoneInput(phone.text) }, null,
                result =>
                {
                    busy = false;
                    if (!string.IsNullOrEmpty(result.developmentCode)) code.text = result.developmentCode;
                    error.text = string.IsNullOrEmpty(result.developmentCode) ? "验证码已发送，请查看短信。" : "DEV/TEST：测试验证码已填入，未发送真实短信。";
                }, Failure);
        }

        private void Login()
        {
            if (busy) return;
            if (string.IsNullOrWhiteSpace(phone.text)) { error.text = "请先填写手机号。"; return; }
            busy = true; LastError = null; error.text = "正在登录...";
            client.Post<LoginResponse>(context.Configuration.identityUrl, "v1/identity/phone/login",
                new PhoneLoginRequest { phoneNumber = NormalizePhoneInput(phone.text), code = string.IsNullOrWhiteSpace(code.text) ? null : code.text.Trim() }, null,
                result =>
                {
                    busy = false;
                    try
                    {
                        if (result.playerId <= 0 || string.IsNullOrWhiteSpace(result.sessionToken) || string.IsNullOrWhiteSpace(result.homeRealmId))
                            throw new InvalidOperationException("登录响应不完整。");
                        context.Session.Authenticated(result.playerId);
                        identity = result; token = result.sessionToken; code.text = "";
                        RestoredFromCache = false;
                        bool saved = sessionCache != null && sessionCache.Save(token, result.expiresAt);
                        error.text = saved ? "登录成功，已安全缓存 Token，下次自动登录。" : "登录成功；本机安全缓存不可用，下次需重新登录。";
                    }
                    catch (Exception exception) { Failure(exception.Message); }
                }, Failure);
        }

        private void RestoreCachedSession()
        {
            if (busy || context.Session.Phase != SessionPhase.SignedOut || sessionCache == null || !sessionCache.TryLoad(out var cached)) return;
            busy = true; LastError = null; error.text = "正在校验本机 Token...";
            client.Post<SessionProfileResponse>(context.Configuration.identityUrl, "v1/identity/session/login", new EmptyRequest(), cached,
                profile =>
                {
                    if (profile.playerId <= 0 || string.IsNullOrWhiteSpace(profile.homeRealmId) || !DateTimeOffset.TryParse(profile.expiresAt, out var expires) || expires <= DateTimeOffset.UtcNow)
                    { sessionCache.Clear(); Failure("Token 校验响应无效，请重新登录。"); return; }
                    context.Session.Authenticated(profile.playerId);
                    identity = new LoginResponse { sessionToken = cached, playerId = profile.playerId, homeRealmId = profile.homeRealmId, expiresAt = profile.expiresAt };
                    token = cached; RestoredFromCache = true; busy = false;
                    sessionCache.Save(cached, profile.expiresAt);
                    error.text = "Token 自动登录成功，无需短信；有效期至 " + expires.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
                }, failed =>
                {
                    if (failed.Status == 401 || failed.Status == 403)
                    { sessionCache.Clear(); Failure("Token 已过期或撤销，请重新登录。"); }
                    else Failure("Token 校验暂时失败，缓存已保留，请重试。" );
                });
        }

        private void Logout()
        {
            if (busy) return;
            string toRevoke = token;
            if (string.IsNullOrEmpty(toRevoke)) sessionCache?.TryLoad(out toRevoke);
            if (string.IsNullOrEmpty(toRevoke)) { ClearLocalSession(); return; }
            busy = true;
            client.Post<RevokeResponse>(context.Configuration.identityUrl, "v1/identity/session/revoke", new EmptyRequest(), toRevoke,
                _ => ClearLocalSession(), failed =>
                { if (failed.Status == 401 || failed.Status == 403) ClearLocalSession(); else Failure("退出失败，尚未撤销服务端 Token，请联网后重试。"); });
        }

        private void ClearLocalSession()
        {
            bool cleared = sessionCache == null || sessionCache.Clear();
            token = null; identity = null; RestoredFromCache = false; busy = false;
            context.Session.SignOut();
            if (loadedWorldScene.IsValid() && loadedWorldScene.isLoaded) SceneManager.UnloadSceneAsync(loadedWorldScene);
            loadedWorldScene = default; worldScene = null;
            error.text = cleared ? "已退出登录并清除本机缓存。" : "已撤销会话；本机缓存删除失败，请检查目录权限。";
        }

        private static string NormalizePhoneInput(string value)
        {
            var phoneNumber = value.Trim().Replace(" ", "").Replace("-", "");
            return System.Text.RegularExpressions.Regex.IsMatch(phoneNumber, "^1[3-9][0-9]{9}$") ? "+86" + phoneNumber : phoneNumber;
        }

        private void EnterWorld()
        {
            if (busy || identity == null || context.Session.Phase != SessionPhase.Lobby) return;
            busy = true; worldLoading = true; LastError = null; error.text = "正在连接大厅并请求世界路由...";
            var request = new RouteRequest { playerId = identity.playerId, homeRealmId = identity.homeRealmId,
                preferredClusterId = context.Configuration.backendWorldClusterId, preferredSceneId = context.Configuration.backendSceneId };
            JoinWorld(request);
        }

        private void JoinWorld(RouteRequest request)
        {
            client.Post<WorldRouteResponse>(context.Configuration.lobbyUrl, "v1/world/join", request, token,
                route => StartCoroutine(LoadWorld(route)), Failure);
        }

        private IEnumerator LoadWorld(WorldRouteResponse route)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(LoadWorldSteps(route));
            try
            {
                while (stack.Count > 0)
                {
                    var routine = stack.Peek();
                    object current = null; bool moved = false; Exception failure = null;
                    try { moved = routine.MoveNext(); if (moved) current = routine.Current; }
                    catch (Exception exception) { failure = exception; }
                    if (failure != null) { Failure("世界资源加载失败，请重试或重启客户端。"); yield break; }
                    if (!moved) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                    if (current is IEnumerator nested && !(current is AsyncOperationBase) && !(current is HandleBase) && !(current is CustomYieldInstruction))
                        stack.Push(nested);
                    else yield return current;
                }
            }
            finally
            {
                while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
                busy = false; worldLoading = false;
            }
        }

        private IEnumerator LoadWorldSteps(WorldRouteResponse route)
        {
            var world = new WorldRoute { HomeRealmId = route.homeRealmId, WorldClusterId = route.worldClusterId,
                InstanceId = route.instanceId, SceneId = route.sceneId, Epoch = route.epoch };
            world.Validate();
            if (world.HomeRealmId != identity.homeRealmId) throw new InvalidOperationException("Route home realm mismatch.");
            yield return context.Resources.PrepareContentPackage("WorldCommon");
            worldScene = context.Resources.LoadScene("WorldCommon", context.Configuration.worldSceneAddress);
            yield return worldScene;
            if (worldScene.Status != EOperationStatus.Succeeded) throw new InvalidOperationException(worldScene.Error);
            loadedWorldScene = worldScene.SceneObject;
            if (worldScene.SceneObject.IsValid()) SceneManager.SetActiveScene(worldScene.SceneObject);
            context.Session.EnterWorld(world);
            error.text = "场景加载完成：" + route.instanceId + " / " + route.sceneId + "（AOI 在线同步尚未接入）";
        }

        private void Failure(string message)
        {
            busy = false; worldLoading = false; LastError = message; error.text = "在线请求失败：" + message;
        }

        private void Failure(BackendRequestError failure) => Failure(failure.Message);

        private void OnDestroy()
        {
            token = null; identity = null;
            if (ownedEvents != null) Destroy(ownedEvents);
            // SceneManager owns the scene unload after Core releases YooAsset on shutdown.
            if (loadedWorldScene.IsValid() && loadedWorldScene.isLoaded) SceneManager.UnloadSceneAsync(loadedWorldScene);
            if (uiFont != null) Destroy(uiFont);
        }

        private void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var events = new GameObject("InsectSpace UI EventSystem");
            ownedEvents = events; events.transform.SetParent(transform, false);
            events.AddComponent<EventSystem>();
            events.AddComponent<StandaloneInputModule>();
        }

        private static GameObject Panel(Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size, Color color)
        {
            var go = new GameObject("Panel"); go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>(); rect.anchorMin = anchorMin; rect.anchorMax = anchorMax; rect.pivot = anchorMin; rect.anchoredPosition = position; rect.sizeDelta = size;
            go.AddComponent<Image>().color = color; return go;
        }

        private static Text Label(Transform parent, string value, int size, TextAnchor anchor, Vector2 position, Vector2 dimensions)
        {
            var go = new GameObject("Text"); go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>(); rect.anchorMin = new Vector2(0f, 1f); rect.anchorMax = new Vector2(0f, 1f); rect.pivot = new Vector2(0f, 1f); rect.anchoredPosition = position; rect.sizeDelta = dimensions;
            var text = go.AddComponent<Text>(); text.text = value;
#if UNITY_2023_1_OR_NEWER
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
            text.fontSize = size; text.alignment = anchor; text.color = Color.white; text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Overflow; return text;
        }

        private static InputField Input(Transform parent, string placeholder, Vector2 position)
        {
            var go = Panel(parent, new Vector2(0f, 1f), new Vector2(0f, 1f), position, new Vector2(640f, 40f), new Color(0.1f, 0.13f, 0.18f, 1f));
            var field = go.AddComponent<InputField>();
            var text = Label(go.transform, "", 17, TextAnchor.MiddleLeft, new Vector2(12f, -1f), new Vector2(616f, 38f)); field.textComponent = text;
            var hint = Label(go.transform, placeholder, 15, TextAnchor.MiddleLeft, new Vector2(12f, -1f), new Vector2(616f, 38f)); hint.color = new Color(0.55f, 0.6f, 0.68f); field.placeholder = hint;
            return field;
        }

        private static Button Button(Transform parent, string value, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction click)
        {
            var go = Panel(parent, new Vector2(0f, 1f), new Vector2(0f, 1f), position, size, new Color(0.14f, 0.32f, 0.5f, 1f));
            var button = go.AddComponent<Button>(); var label = Label(go.transform, value, 16, TextAnchor.MiddleCenter, Vector2.zero, size); label.alignment = TextAnchor.MiddleCenter; button.targetGraphic = go.GetComponent<Image>(); button.onClick.AddListener(click); return button;
        }

        private static Slider Slider(Transform parent, Vector2 position, Vector2 size)
        {
            var go = Panel(parent, new Vector2(0f, 1f), new Vector2(0f, 1f), position, size, new Color(0.12f, 0.15f, 0.2f, 1f));
            var slider = go.AddComponent<Slider>(); slider.minValue = 0f; slider.maxValue = 1f; slider.value = 0f; slider.interactable = false;
            var fill = Panel(go.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0.2f, 0.7f, 0.9f));
            slider.fillRect = fill.GetComponent<RectTransform>();
            return slider;
        }
    }
}
