using System.Collections;
using UnityEngine;
using YooAsset;

namespace InsectSpace.Gameplay.Demo
{
    // Survives a caller destroying the hub while it still owns an additive scene.
    // Keeps the Bootstrap alive until YooAsset has finished releasing that scene.
    public sealed class ClientDemoCleanup : MonoBehaviour
    {
        public static void Begin(SceneHandle scene, GameObject ownedBootstrap)
        {
            if (scene == null || !scene.IsValid)
            {
                if (ownedBootstrap != null) Destroy(ownedBootstrap);
                return;
            }
            var cleanup = new GameObject("Demo resource cleanup").AddComponent<ClientDemoCleanup>();
            DontDestroyOnLoad(cleanup.gameObject);
            cleanup.StartCoroutine(cleanup.Release(scene, ownedBootstrap));
        }

        private IEnumerator Release(SceneHandle scene, GameObject ownedBootstrap)
        {
            yield return scene.UnloadSceneAsync();
            if (ownedBootstrap != null) Destroy(ownedBootstrap);
            Destroy(gameObject);
        }
    }
}
