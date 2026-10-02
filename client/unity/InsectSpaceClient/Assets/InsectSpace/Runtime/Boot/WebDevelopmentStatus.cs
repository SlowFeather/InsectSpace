#if UNITY_WEBGL && INSECTSPACE_WEB_DEVELOPMENT && DEVELOPMENT_BUILD && !UNITY_EDITOR
using System.Collections;
using UnityEngine;
using YooAsset;

namespace InsectSpace.Client
{
    public sealed class WebDevelopmentStatus : MonoBehaviour
    {
        private string state = "Starting";
        private InsectSpaceBootstrap bootstrap;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create() => new GameObject("Local Web Development Status").AddComponent<WebDevelopmentStatus>();

        private IEnumerator Start() => CoroutineGuard.Run(VerifyContent(), error =>
        {
            state = "Failed: " + error.Message;
            Debug.LogError("[InsectSpace] WEB_DEVELOPMENT_FAILED " + error);
        });

        private IEnumerator VerifyContent()
        {
            float deadline = Time.realtimeSinceStartup + 120;
            while (Time.realtimeSinceStartup < deadline)
            {
                bootstrap = FindFirstObjectByType<InsectSpaceBootstrap>();
                if (bootstrap != null && (bootstrap.Ready || bootstrap.LastError != null)) break;
                yield return null;
            }
            if (bootstrap == null || !bootstrap.Ready) throw new System.InvalidOperationException(
                bootstrap == null ? "Bootstrap timeout" : bootstrap.LastError ?? "Bootstrap timeout");
            var resources = bootstrap.Context.Resources;
            yield return resources.PrepareContentPackage("WorldCommon");
            AssetHandle handle = resources.LoadAsset<GameObject>("WorldCommon", "WorldActor");
            try
            {
                yield return handle;
                if (handle.Status != EOperationStatus.Succeeded) throw new System.InvalidOperationException(handle.Error);
                var actor = Instantiate(handle.GetAssetObject<GameObject>(), new Vector3(0, 1, 0), Quaternion.identity);
                state = "Ready: bundled tables, gameplay and WorldCommon loaded";
                Debug.Log("[InsectSpace] WEB_DEVELOPMENT_READY modules=" + bootstrap.ModuleCount + " tables=" + bootstrap.TableCount);
                while (actor != null) yield return null;
            }
            finally { handle.Release(); }
        }

        private void OnGUI()
        {
            GUI.Box(new Rect(10, 10, 680, 70), "LOCAL WEB DEVELOPMENT — compiled gameplay; no native hot update");
            GUI.Label(new Rect(20, 38, 660, 30), state);
        }
    }
}
#endif
