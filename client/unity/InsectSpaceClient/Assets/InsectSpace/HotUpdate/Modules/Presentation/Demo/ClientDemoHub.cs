using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using InsectSpace.Client;
using InsectSpace.Contracts;
using InsectSpace.Foundation;
using InsectSpace.Rendering;
using Luban;
using UnityEngine;
using YooAsset;

namespace InsectSpace.Gameplay.Demo
{
    // Opt-in teaching scene only. Not registered in the production module lifecycle.
    public sealed partial class ClientDemoHub : MonoBehaviour
    {
        public const int LessonCount = 12;
        public bool Ready { get; private set; }
        public string Error { get; private set; }
        public bool Busy { get; private set; }
        public bool Stopped { get; private set; }
        public int SelectedLesson { get; private set; }
        public string LastResult { get; private set; } = "等待真实启动链…";
        public int CompletedCount => completed.Count(x => x);
        public bool ResourceLoaded => asset != null && asset.IsValid && resourceInstance != null;
        public bool AdditiveSceneLoaded => extraScene != null && extraScene.IsValid && extraScene.SceneObject.isLoaded;
        public LocalWorldLesson World { get; private set; }
        public DemoBattleLab Battle { get; private set; }
        public LocalPlayerLesson Player { get; } = new LocalPlayerLesson();
        public LocalQuestLesson Quest { get; } = new LocalQuestLesson();
        public LocalNetworkProbe Network { get; private set; }
        public SessionCoordinator Lobby { get; } = new SessionCoordinator();
        public int VisibleActors => presenter == null ? 0 : presenter.VisibleCount;
        public string[] LifecycleTrace { get; private set; } = Array.Empty<string>();
        private InsectSpaceBootstrap boot;
        private Config.Tables tables;
        private WorldActorPresenter presenter;
        private Camera demoCamera;
        private RenderTexture preview;
        private RenderTexture savedCameraTarget;
        private float savedCameraSize;
        private GameObject displayCamera;
        private GameObject resourceInstance;
        private AssetHandle asset;
        private SceneHandle extraScene;
        private readonly List<Material> ownedMaterials = new List<Material>();
        private readonly Queue<string> journal = new Queue<string>();
        private readonly bool[] completed = new bool[LessonCount];
        private int savedQuality, savedFps, savedVsync;
        private bool settingsCaptured;
        private bool ownsBootstrap;
        private bool quitting;
        private bool autoBattle;
        private float battleTimer;
        private Vector2 pageScroll;
        private string tableFilter = "";
        private string gateResult = "请选择一个失败案例。";
        private string versionResult = "点击检查，读取当前程序集边界。";

        private IEnumerator Start()
        {
#if !UNITY_EDITOR
            Error = "ClientDemoHub is an explicit Editor-only local teaching scene.";
            yield break;
#else
            BootConfiguration config = null;
            try
            {
                config = BootConfiguration.Load();
                config.Validate();
                if (!config.editorSimulate || !config.localSmokeMode || config.useWeChatSdk)
                    throw new InvalidOperationException("Demo requires explicit editorSimulate + localSmokeMode, without WeChat SDK.");
            }
            catch (Exception ex) { Error = ex.Message; }
            if (Error != null) yield break;
            savedQuality = QualitySettings.GetQualityLevel();
            savedFps = Application.targetFrameRate; savedVsync = QualitySettings.vSyncCount;
            settingsCaptured = true;
            boot = FindObjectOfType<InsectSpaceBootstrap>();
            if (boot == null)
            {
                ownsBootstrap = true;
                boot = new GameObject("Demo Bootstrap - LOCAL ONLY").AddComponent<InsectSpaceBootstrap>();
            }
            float deadline = Time.realtimeSinceStartup + 45;
            while (!boot.Ready && boot.LastError == null && Time.realtimeSinceStartup < deadline) yield return null;
            if (!boot.Ready) { Error = boot.LastError ?? "Bootstrap timed out: " + boot.Stage; yield break; }
            try
            {
                tables = new Config.Tables(name => new ByteBuf(boot.Context.TableData[name]));
                World = new LocalWorldLesson(); Battle = new DemoBattleLab();
                Network = new LocalNetworkProbe(Record);
                presenter = new GameObject("AOI - local fixture").AddComponent<WorldActorPresenter>();
                presenter.transform.SetParent(transform, false);
                presenter.Bind(World.Presence, CreateActor, go => Destroy(go));
                demoCamera = Camera.main;
                if (demoCamera == null) throw new InvalidOperationException("Demo scene camera is missing. Run Start-ClientDemo.ps1.");
                savedCameraTarget = demoCamera.targetTexture;
                savedCameraSize = demoCamera.orthographicSize;
                preview = new RenderTexture(900, 680, 24) { name = "Demo presentation preview" };
                demoCamera.targetTexture = preview;
                displayCamera = new GameObject("Demo UI display");
                var uiCamera = displayCamera.AddComponent<Camera>();
                uiCamera.cullingMask = 0; uiCamera.clearFlags = CameraClearFlags.SolidColor;
                uiCamera.backgroundColor = new Color(0.035f, 0.055f, 0.08f);
                uiCamera.depth = 10;
                World.Spawn(); Player.Apply(LocalPlayerLesson.Fixture());
                Ready = true; completed[0] = true;
                Record("真实 Bootstrap Ready：8 模块 / 2 张表；教学会话已隔离。");
                Debug.Log("CLIENT_DEMO_READY lessons=" + LessonCount);
            }
            catch (Exception ex) { Error = ex.Message; Debug.LogException(ex); }
#endif
        }

        private GameObject CreateActor(int appearance)
        {
            var view = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            view.name = "LOCAL actor appearance " + appearance;
            // Remove the primitive collider; visual interpolation is not physics simulation.
            Destroy(view.GetComponent<Collider>());
            while (ownedMaterials.Count < 2)
            {
                var material = new Material(Shader.Find("Universal Render Pipeline/Lit"))
                { color = ownedMaterials.Count == 0 ? new Color(0.25f, 0.88f, 0.70f) : new Color(0.98f, 0.67f, 0.28f) };
                ownedMaterials.Add(material);
            }
            view.GetComponent<Renderer>().sharedMaterial = ownedMaterials[appearance == 1 ? 0 : 1];
            return view;
        }

        private void Update()
        {
            if (!Ready) return;
            if (!boot.Ready) { Ready = false; Error = boot.LastError ?? boot.Stage; return; }
            Network.Tick(Time.unscaledDeltaTime);
            if (autoBattle)
            {
                battleTimer += Time.unscaledDeltaTime;
                if (battleTimer >= 0.4f)
                {
                    battleTimer = 0;
                    if (!Battle.Step()) autoBattle = false;
                }
            }
        }

        public void SelectLesson(int index)
        {
            if (index < 0 || index >= LessonCount) throw new ArgumentOutOfRangeException(nameof(index));
            SelectedLesson = index; pageScroll = Vector2.zero; autoBattle = false;
        }

        public void Record(string message)
        {
            LastResult = message;
            if (journal.Count == 9) journal.Dequeue();
            journal.Enqueue(message);
        }

        private void ActionButton(string label, Action action, bool available = true, bool markLesson = true)
        {
            bool enabledBefore = GUI.enabled;
            GUI.enabled = enabledBefore && Ready && !Busy && available;
            if (GUILayout.Button(label, buttonStyle, GUILayout.MinHeight(39)))
            {
                int lesson = SelectedLesson;
                try { action(); if (markLesson) completed[lesson] = true; }
                catch (Exception ex) { Record("操作被拒绝：" + ex.Message); }
            }
            GUI.enabled = enabledBefore;
        }

        public void RunLifecycle(bool fail)
        { LifecycleTrace = DemoModuleLesson.Run(fail); Record(fail ? "失败模块也已逆序清理。" : "拓扑启动、Tick、逆序 Stop 和服务冻结已执行。"); }

        public void ApplyQuality(int value)
        {
            var profile = tables.TbQualityProfile.Get(value);
            QualityController.Apply((QualityTier)value, profile.TargetFps);
            Record("实际画质：" + profile.Name + " / " + profile.TargetFps + " FPS 配置预算");
        }

        public void ResetBattle(bool ordered)
        { autoBattle = false; Battle.Reset(ordered); Record(ordered ? "本地有序收件箱：缺帧时停住。" : "本地整数对战已重置。所有状态来自 GF 确定性世界。"); }

        public bool VerifyBattleReplay()
        {
            bool equal = Battle.VerifyReplay(out int count);
            Record(count == 0 ? "请先推进至少一帧，再验证回放。" : "逐帧回放 " + count + " 帧 / hash 一致=" + equal);
            return count > 0 && equal;
        }

        public void RunResourceOperation(IEnumerator routine)
        { RunOperation(routine, 3); }

        private void RunOperation(IEnumerator routine, int lesson = -1)
        {
            if (!Ready || Busy) { (routine as IDisposable)?.Dispose(); return; }
            Busy = true;
            StartCoroutine(CompleteOperation(routine, lesson));
        }

        private IEnumerator CompleteOperation(IEnumerator routine, int lesson)
        {
            bool failed = false;
            var stack = new Stack<IEnumerator>();
            stack.Push(routine);
            try
            {
                while (stack.Count > 0)
                {
                    object current = null;
                    bool more = false;
                    try { more = stack.Peek().MoveNext(); if (more) current = stack.Peek().Current; }
                    catch (Exception ex) { failed = true; Record("操作失败：" + ex.Message); }
                    if (failed) break;
                    if (!more) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                    // YooAsset handles are yieldables AND IDisposable; disposing the wait would
                    // release our owned handle before the caller can instantiate the asset.
                    if (current is IEnumerator nested && !(current is AsyncOperationBase) &&
                        !(current is HandleBase) && !(current is CustomYieldInstruction)) stack.Push(nested);
                    else yield return current;
                }
            }
            finally
            {
                while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
                Busy = false;
            }
            if (!failed && lesson >= 0) completed[lesson] = true;
        }

        public IEnumerator ResetLessons()
        {
            autoBattle = false;
            yield return ReleaseResource();
            yield return UnloadAdditiveScene();
            Network.Dispose(); Network = new LocalNetworkProbe(Record);
            Lobby.SignOut(); Player.Reset(); Player.Apply(LocalPlayerLesson.Fixture()); Quest.Reset();
            presenter.Unbind(); World.Clear(); World = new LocalWorldLesson();
            presenter.Bind(World.Presence, CreateActor, go => Destroy(go)); World.Spawn();
            Battle.Reset(false); battleTimer = 0;
            demoCamera.orthographicSize = savedCameraSize;
            RestoreQuality();
            Array.Clear(completed, 0, completed.Length); completed[0] = true;
            LifecycleTrace = Array.Empty<string>(); tableFilter = "";
            gateResult = "请选择一个失败案例。"; versionResult = "点击检查，读取当前程序集边界。";
            journal.Clear(); SelectLesson(0);
            Record("教学状态已重置；资源已释放，可以开始下一轮讲解。");
        }

        public IEnumerator Shutdown()
        {
            autoBattle = false;
            yield return ReleaseResource();
            yield return UnloadAdditiveScene();
            Network.Dispose(); Battle.Dispose(); presenter.Unbind(); World.Clear();
            Ready = false; Stopped = true;
            RestoreQuality();
            if (ownsBootstrap && boot != null) { Destroy(boot.gameObject); boot = null; yield return null; }
            Record("演示已结束，资源和连接已释放。停止 Play 后可再次运行启动脚本。");
        }

        private void RestoreQuality()
        {
            if (!settingsCaptured) return;
            QualitySettings.SetQualityLevel(savedQuality, true);
            Application.targetFrameRate = savedFps; QualitySettings.vSyncCount = savedVsync;
        }

        public IEnumerator LoadResource()
        {
            if (ResourceLoaded) yield break;
            if (asset != null && asset.IsValid) { asset.Release(); asset = null; }
            yield return boot.Context.Resources.PrepareContentPackage("WorldCommon");
            asset = boot.Context.Resources.LoadAsset<GameObject>("WorldCommon", "WorldActor");
            yield return asset;
            if (asset.Status != EOperationStatus.Succeeded)
            {
                string reason = asset.Error; asset.Release(); asset = null;
                throw new InvalidOperationException(reason);
            }
            resourceInstance = Instantiate(asset.GetAssetObject<GameObject>(), new Vector3(0, 0, -3), Quaternion.identity);
            resourceInstance.name = "YooAsset WorldActor - handle retained";
            resourceInstance.transform.SetParent(transform, true);
            Record("WorldCommon/WorldActor 真实加载并实例化；handle 保持有效。");
        }

        public IEnumerator ReleaseResource()
        {
            if (resourceInstance != null) { Destroy(resourceInstance); resourceInstance = null; yield return null; }
            if (asset != null && asset.IsValid) asset.Release();
            asset = null;
            Record("实例已销毁，再 Release 资源 handle。");
        }

        public IEnumerator LoadAdditiveScene()
        {
            if (AdditiveSceneLoaded) yield break;
            // A failed load still owns a handle. Unload it before replacing the reference.
            if (extraScene != null && extraScene.IsValid) yield return extraScene.UnloadSceneAsync();
            extraScene = null;
            yield return boot.Context.Resources.PrepareContentPackage("WorldCommon");
            extraScene = boot.Context.Resources.LoadScene("WorldCommon", "WorldSandbox");
            yield return extraScene;
            if (extraScene.Status != EOperationStatus.Succeeded) throw new InvalidOperationException(extraScene.Error);
            Record("WorldSandbox 已附加加载；Bootstrap 保持 Ready。");
        }

        public IEnumerator UnloadAdditiveScene()
        {
            if (extraScene != null && extraScene.IsValid) yield return extraScene.UnloadSceneAsync();
            if (extraScene != null && extraScene.IsValid) throw new InvalidOperationException("Scene handle was not released.");
            extraScene = null;
            Record("附加场景已卸载，场景 handle 已自动释放。");
        }

        private void OnApplicationQuit() { quitting = true; }

        private void OnDestroy()
        {
            StopAllCoroutines();
            Network?.Dispose(); Battle?.Dispose();
            if (presenter != null) presenter.Unbind();
            World?.Clear();
            if (resourceInstance != null) DestroyImmediate(resourceInstance);
            if (asset != null && asset.IsValid) asset.Release();
            // Normal teardown must await additive unload before disposing the owning Core package.
            // On quitting Play, Unity itself destroys every scene and the Bootstrap.
            if (!quitting && Application.isPlaying)
                ClientDemoCleanup.Begin(extraScene, ownsBootstrap && boot != null ? boot.gameObject : null);
            if (demoCamera != null) { demoCamera.targetTexture = savedCameraTarget; demoCamera.orthographicSize = savedCameraSize; }
            if (preview != null) { preview.Release(); Destroy(preview); }
            if (displayCamera != null) Destroy(displayCamera);
            foreach (var material in ownedMaterials) if (material != null) Destroy(material);
            if (uiFont != null) Destroy(uiFont);
            RestoreQuality();
        }
    }
}
