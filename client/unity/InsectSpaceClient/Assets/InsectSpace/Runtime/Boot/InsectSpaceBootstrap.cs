using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Framework;
using Framework.Fsm;
using UnityEngine;
using UnityFramework.Runtime;

namespace InsectSpace.Client
{
    [DisallowMultipleComponent]
    public sealed class InsectSpaceBootstrap : MonoBehaviour
    {
        private static InsectSpaceBootstrap instance;
        private YooResourceService resources;
        private IHotUpdateApplication application;
        private IFsmManager fsms;
        private bool disposed;
        public bool Ready { get; private set; }
        public string LastError { get; private set; }
        public string Stage { get; private set; } = "Created";
        public string Status => application?.Status ?? Stage;
        public float Progress => resources?.Progress ?? (Ready ? 1f : 0f);
        public string ProgressStage => Stage == "YooAsset" ? resources?.ProgressStage ?? Stage : Stage;
        public int ModuleCount => application?.Modules.Count ?? 0;
        public int TableCount { get; private set; }
        public BootContext Context { get; private set; }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);
            gameObject.AddComponent<BaseComponent>();
            gameObject.AddComponent<BootstrapProgressOverlay>();
            fsms = GameFrameworkEntry.GetModule<IFsmManager>();
            var fsm = fsms.CreateFsm(this, new StartupState(), new ReadyState(), new FailedState());
            fsm.Start<StartupState>();
            StartCoroutine(CoroutineGuard.Run(Boot(), Fail));
        }

        private IEnumerator Boot()
        {
            Stage = "Configuration";
            var config = BootConfiguration.Load();
            config.Validate();
            Stage = "Platform";
            yield return PlatformServices.Initialize(config.useWeChatSdk);
            QualitySettings.SetQualityLevel((int)config.quality, true);
            resources = PlatformServices.CreateResources();
            Stage = "YooAsset";
            yield return resources.Initialize(config);
            var data = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (string location in config.tableLocations)
                yield return resources.ReadBytes(location, bytes => data.Add(location, bytes));
            TableCount = data.Count;
            Stage = "HotUpdate";
            Assembly assembly = null;
            yield return new HotUpdateLoader().Load(resources, config, value => assembly = value);
            Context = new BootContext(config, data, message => Debug.Log("[InsectSpace] " + message), resources);
            Stage = "Modules";
            application = HotUpdateLoader.CreateApplication(assembly, Context);
            Ready = true;
            Stage = "Ready";
            Debug.Log("[InsectSpace] FOUNDATION_READY modules=" + ModuleCount + " tables=" + TableCount);
        }

        private void Update()
        {
            if (!Ready) return;
            try
            {
                Context.Connections.Tick(Time.unscaledDeltaTime);
                application.Tick(Time.unscaledDeltaTime);
            }
            catch (Exception exception) { Fail(exception); }
        }

        private void Fail(Exception error)
        {
            Ready = false;
            LastError = error.ToString();
            Stage = "Failed";
            Debug.LogError("[InsectSpace] BOOT_FAILED " + error);
            ReleaseApplication();
        }

        private void ReleaseApplication()
        {
            var current = application;
            application = null;
            try { current?.Dispose(); }
            catch (Exception error) { Debug.LogException(error); }
            try { Context?.Dispose(); }
            catch (Exception error) { Debug.LogException(error); }
            Context = null;
            resources?.Dispose();
            resources = null;
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && Ready && !Context.Configuration.localSmokeMode)
                Context.Connections.Suspend();
        }

        private void OnDestroy()
        {
            if (instance != this || disposed) return;
            disposed = true;
            instance = null;
            Ready = false;
            StopAllCoroutines();
            ReleaseApplication();
            fsms?.DestroyFsm<InsectSpaceBootstrap>();
            UnityFrameworkEntry.Shutdown(ShutdownType.None);
        }

        private sealed class StartupState : FsmState<InsectSpaceBootstrap>
        {
            protected override void OnUpdate(IFsm<InsectSpaceBootstrap> fsm, float elapsed, float realElapsed)
            {
                if (fsm.Owner.LastError != null) ChangeState<FailedState>(fsm);
                else if (fsm.Owner.Ready) ChangeState<ReadyState>(fsm);
            }
        }
        private sealed class ReadyState : FsmState<InsectSpaceBootstrap>
        {
            protected override void OnUpdate(IFsm<InsectSpaceBootstrap> fsm, float elapsed, float realElapsed)
            {
                if (fsm.Owner.LastError != null) ChangeState<FailedState>(fsm);
            }
        }
        private sealed class FailedState : FsmState<InsectSpaceBootstrap> { }
    }
}
