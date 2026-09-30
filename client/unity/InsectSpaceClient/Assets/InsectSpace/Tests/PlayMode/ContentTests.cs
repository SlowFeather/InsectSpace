using System;
using System.Collections;
using InsectSpace.Client;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using YooAsset;
using Object = UnityEngine.Object;

namespace InsectSpace.Tests
{
    public sealed class ContentTests
    {
        [UnityTest]
        public IEnumerator ContentUsesSeparatePackageAndReleasesPrefabAndScene()
        {
            var root = new GameObject("Content lifecycle test");
            var boot = root.AddComponent<InsectSpaceBootstrap>();
            AssetHandle asset = null;
            SceneHandle scene = null;
            GameObject instance = null;
            try
            {
                float deadline = Time.realtimeSinceStartup + 30;
                while (!boot.Ready && boot.LastError == null && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.IsTrue(boot.Ready, boot.LastError ?? boot.Stage);
                var service = boot.Context.Resources;
                Assert.Throws<InvalidOperationException>(() => service.LoadAsset<GameObject>("WorldCommon", "WorldActor"));
                yield return service.PrepareContentPackage("WorldCommon");
                yield return service.PrepareContentPackage("WorldCommon");
                asset = service.LoadAsset<GameObject>("WorldCommon", "WorldActor");
                yield return asset;
                Assert.AreEqual(EOperationStatus.Succeeded, asset.Status, asset.Error);
                instance = Object.Instantiate(asset.GetAssetObject<GameObject>());
                Assert.NotNull(instance.GetComponent<Renderer>());
                Object.Destroy(instance);
                yield return null;
                asset.Release();
                Assert.IsFalse(asset.IsValid);
                scene = service.LoadScene("WorldCommon", "WorldSandbox");
                yield return scene;
                Assert.AreEqual(EOperationStatus.Succeeded, scene.Status, scene.Error);
                var loaded = scene.SceneObject;
                Assert.IsTrue(loaded.isLoaded);
                yield return scene.UnloadSceneAsync();
                Assert.IsFalse(loaded.isLoaded);
                Assert.IsFalse(scene.IsValid);
                Assert.Throws<InvalidOperationException>(() => service.BindContentCatalog(Array.Empty<ContentPackageVersion>()));
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
                if (asset != null && asset.IsValid) asset.Release();
                if (scene != null && scene.IsValid && scene.SceneObject.isLoaded)
                    UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene.SceneObject);
                Object.Destroy(root);
            }
            yield return null;
        }
    }
}
