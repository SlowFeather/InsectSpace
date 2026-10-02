#if UNITY_EDITOR && UNITY_6000_0_OR_NEWER
using System.Collections;
using System.Linq;
using InsectSpace.Rendering;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace InsectSpace.Tests
{
    public sealed class MeadowShowcaseTests
    {
        private const string ScenePath = "Assets/InsectSpace/Scenes/MeadowShowcase.unity";
        private Scene loadedScene;
        private Scene previousScene;
        private int previousQuality;

        [UnitySetUp]
        public IEnumerator LoadSavedScene()
        {
            previousScene = SceneManager.GetActiveScene();
            previousQuality = QualitySettings.GetQualityLevel();
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            loadedScene = SceneManager.GetSceneByPath(ScenePath);
            Assert.IsTrue(loadedScene.IsValid() && loadedScene.isLoaded);
            SceneManager.SetActiveScene(loadedScene);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator RestoreSceneAndQuality()
        {
            QualitySettings.SetQualityLevel(previousQuality, true);
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if (loadedScene.IsValid() && loadedScene.isLoaded) yield return SceneManager.UnloadSceneAsync(loadedScene);
        }

        private T[] Components<T>() where T : Component => loadedScene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

        [Test]
        public void ReloadedSceneKeepsPersistentMeshesMaterialsAndCamera()
        {
            var filters = Components<MeshFilter>();
            Assert.Greater(filters.Length, 100);
            foreach (var filter in filters)
            {
                Assert.IsNotNull(filter.sharedMesh, filter.name);
                Assert.IsTrue(EditorUtility.IsPersistent(filter.sharedMesh), filter.name);
                Assert.Greater(filter.sharedMesh.vertexCount, 0, filter.name);
            }
            foreach (var renderer in Components<Renderer>())
            foreach (var material in renderer.sharedMaterials)
            {
                Assert.IsNotNull(material, renderer.name);
                Assert.IsTrue(EditorUtility.IsPersistent(material), renderer.name);
                Assert.IsFalse(ShaderUtil.ShaderHasError(material.shader), material.name);
            }
            Assert.AreEqual(1, Components<Camera>().Length);
            Assert.AreEqual(0, Components<IsometricCameraRig>().Length, "A follow rig must not override the authored composition.");
            Assert.AreEqual(36, Components<MeadowGrassLod>().Single().ChunkCount);
        }

        [UnityTest]
        public IEnumerator QualitySwitchChangesEveryGrassChunkWithoutHoles()
        {
            var lod = Components<MeadowGrassLod>().Single();
            var grass = Components<MeshFilter>().Where(f => f.name.StartsWith("Grass_")).OrderBy(f => f.name).ToArray();
            Assert.AreEqual(36, grass.Length);
            int[][] counts = new int[3][];
            Bounds[] bounds = grass.Select(f => f.sharedMesh.bounds).ToArray();
            for (int q = 0; q < 3; q++)
            {
                QualitySettings.SetQualityLevel(q, true);
                yield return null;
                yield return null;
                Assert.AreEqual(q, lod.AppliedQuality);
                counts[q] = grass.Select(f => f.sharedMesh.vertexCount).ToArray();
                for (int i = 0; i < grass.Length; i++)
                {
                    Assert.Greater(counts[q][i], 0, grass[i].name);
                    Assert.Less(Vector3.Distance(bounds[i].center, grass[i].sharedMesh.bounds.center), .65f, grass[i].name);
                    Assert.IsTrue(EditorUtility.IsPersistent(grass[i].sharedMesh), grass[i].name);
                }
            }
            for (int i = 0; i < grass.Length; i++)
            {
                Assert.Less(counts[0][i], counts[1][i]);
                Assert.Less(counts[1][i], counts[2][i]);
                Assert.That((float)counts[0][i] / counts[2][i], Is.InRange(.32f, .35f));
            }
        }

        [UnityTest]
        public IEnumerator AnimatedHeroMovesBonesAndStaysOnTheClearing()
        {
            var animator = Components<Animator>().Single();
            Assert.IsTrue(animator.enabled);
            Assert.IsFalse(animator.applyRootMotion);
            Assert.IsNotNull(animator.runtimeAnimatorController);
            var hand = animator.GetComponentsInChildren<Transform>().First(t => t.name == "hand.r");
            Vector3 origin = animator.transform.position;
            Quaternion start = hand.rotation;
            float maxAngle = 0;
            float previousTime = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            var baked = new Mesh();
            try
            {
                for (int i = 0; i < 12; i++)
                {
                    yield return new WaitForSeconds(.12f);
                    maxAngle = Mathf.Max(maxAngle, Quaternion.Angle(start, hand.rotation));
                    Assert.Less(Vector3.Distance(origin, animator.transform.position), .01f);
                    foreach (var skin in animator.GetComponentsInChildren<SkinnedMeshRenderer>())
                    {
                        skin.BakeMesh(baked);
                        Assert.Less(baked.bounds.size.magnitude, 8, skin.name);
                        Vector3 center = skin.transform.TransformPoint(baked.bounds.center);
                        Assert.IsFalse(float.IsNaN(center.x) || float.IsInfinity(center.x), skin.name);
                        Assert.Less(Vector3.Distance(origin, center), 5, skin.name);
                    }
                }
                Assert.Greater(maxAngle, .05f, "The skeleton should animate, not remain in a frozen bind pose.");
                Assert.Greater(animator.GetCurrentAnimatorStateInfo(0).normalizedTime, previousTime);
            }
            finally { Object.Destroy(baked); }
        }
    }
}
#endif
