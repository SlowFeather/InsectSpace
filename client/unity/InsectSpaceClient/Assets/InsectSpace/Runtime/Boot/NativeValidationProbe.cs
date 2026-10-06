#if DEVELOPMENT_BUILD && ENABLE_IL2CPP && !UNITY_EDITOR && !INSECTSPACE_WEB_DEVELOPMENT
using System;
using System.Collections;
using InsectSpace.Contracts;
using UnityEngine;
using YooAsset;

namespace InsectSpace.Client
{
    public sealed class NativeValidationProbe : MonoBehaviour
    {
        public static bool IsLoopbackRun { get; private set; }

        internal static void ConfigureRemoteRun(BootConfiguration config)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-insectspace-remote");
            if (index < 0) return;
            if (Application.platform != RuntimePlatform.WindowsPlayer ||
                Array.IndexOf(args, "-insectspace-validate") < 0 || index + 1 >= args.Length ||
                !Uri.TryCreate(args[index + 1], UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttp || uri.Host != "127.0.0.1" || !string.IsNullOrEmpty(uri.UserInfo))
                throw new InvalidOperationException("Native patch validation requires an explicit loopback HTTP endpoint.");
            IsLoopbackRun = true;
            config.editorSimulate = false;
            config.localSmokeMode = false;
            config.resourceMode = ResourceMode.Host;
            config.discoverRemoteVersion = true;
            config.remoteRoot = uri.AbsoluteUri;
            config.fallbackRoot = "";
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartWhenRequested()
        {
            if (Application.platform != RuntimePlatform.WindowsPlayer ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-insectspace-validate") < 0) return;
            new GameObject("Native validation probe").AddComponent<NativeValidationProbe>();
        }

        private IEnumerator Start() => CoroutineGuard.Run(Verify(), error =>
        {
            Debug.LogError("[InsectSpace] NATIVE_VALIDATION_FAILED " + error);
            Application.Quit(1);
        });

        private IEnumerator Verify()
        {
            float deadline = Time.realtimeSinceStartup + 120;
            InsectSpaceBootstrap bootstrap = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                bootstrap = FindObjectOfType<InsectSpaceBootstrap>();
                if (bootstrap != null && (bootstrap.Ready || bootstrap.LastError != null)) break;
                yield return null;
            }
            for (int i = 0; i < 20 && bootstrap != null && bootstrap.Ready; i++) yield return null;
            if (bootstrap == null || !bootstrap.Ready || bootstrap.ModuleCount != 8 || bootstrap.TableCount != 4 ||
                bootstrap.Context.Configuration.editorSimulate || bootstrap.Context.Configuration.localSmokeMode ||
                bootstrap.Context.Session.Phase != SessionPhase.SignedOut)
            {
                Debug.LogError("[InsectSpace] NATIVE_VALIDATION_FAILED " + (bootstrap == null ? "Missing bootstrap" : bootstrap.Status));
                Application.Quit(1);
                yield break;
            }
            var resources = bootstrap.Context.Resources;
            yield return resources.PrepareContentPackage("WorldCommon");
            var prefab = resources.LoadAsset<GameObject>("WorldCommon", "WorldActor");
            GameObject instance = null;
            try
            {
                yield return prefab;
                if (prefab.Status != EOperationStatus.Succeeded) throw new InvalidOperationException(prefab.Error);
                instance = UnityEngine.Object.Instantiate(prefab.GetAssetObject<GameObject>());
                if (instance.GetComponent<Renderer>() == null) throw new InvalidOperationException("Invalid world prefab.");
                Destroy(instance);
                yield return null;
            }
            finally
            {
                if (instance != null) Destroy(instance);
                if (prefab.IsValid) prefab.Release();
            }
            var scene = resources.LoadScene("WorldCommon", "WorldSandbox");
            yield return scene;
            if (scene.Status != EOperationStatus.Succeeded) throw new InvalidOperationException(scene.Error);
            var loaded = scene.SceneObject;
            var unload = scene.UnloadSceneAsync();
            yield return unload;
            if (unload.Status != EOperationStatus.Succeeded || loaded.isLoaded || scene.IsValid)
                throw new InvalidOperationException("Content scene did not unload and release its handle.");
            Debug.Log("[InsectSpace] CONTENT_LIFECYCLE_PASSED prefab=WorldActor scene=WorldSandbox");
            Debug.Log("[InsectSpace] NATIVE_VALIDATION_PASSED modules=8 tables=4 session=SignedOut");
            Application.Quit(0);
        }
    }
}
#endif
