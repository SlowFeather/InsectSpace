using System.Collections;
using InsectSpace.Client;
using InsectSpace.Contracts;
using InsectSpace.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace InsectSpace.Tests
{
    public sealed class BootstrapTests
    {
        [UnityTest]
        public IEnumerator BootstrapSceneLoadsAndReachesWorld()
        {
            const string path = "Assets/InsectSpace/Scenes/Bootstrap.scene";
            yield return SceneManager.LoadSceneAsync(path, LoadSceneMode.Additive);
            var scene = SceneManager.GetSceneByPath(path);
            var bootstrap = Object.FindObjectOfType<InsectSpaceBootstrap>();
            Assert.NotNull(bootstrap, "Bootstrap scene has a missing script reference.");
            float deadline = Time.realtimeSinceStartup + 30;
            while (!bootstrap.Ready && bootstrap.LastError == null && Time.realtimeSinceStartup < deadline)
                yield return null;
            try
            {
                Assert.IsTrue(bootstrap.Ready, bootstrap.LastError ?? bootstrap.Stage);
                Assert.AreEqual(SessionPhase.World, bootstrap.Context.Session.Phase);
                Assert.NotNull(Camera.main);
                Assert.IsTrue(Camera.main.orthographic);
                Assert.NotNull(Camera.main.GetComponent<IsometricCameraRig>());
            }
            finally { Object.Destroy(bootstrap.gameObject); }
            yield return null;
            yield return SceneManager.UnloadSceneAsync(scene);
        }

        [UnityTest]
        public IEnumerator BootsRealYooAssetLubanAndGameplayAssembly()
        {
            var root = new GameObject("Test Bootstrap");
            var bootstrap = root.AddComponent<InsectSpaceBootstrap>();
            float deadline = Time.realtimeSinceStartup + 30;
            while (!bootstrap.Ready && bootstrap.LastError == null && Time.realtimeSinceStartup < deadline)
                yield return null;
            try
            {
                Assert.IsNull(bootstrap.LastError);
                Assert.IsTrue(bootstrap.Ready, bootstrap.Stage);
                Assert.AreEqual(8, bootstrap.ModuleCount);
                Assert.AreEqual(2, bootstrap.TableCount);
                Assert.NotNull(bootstrap.Context.Connections);
                Assert.IsFalse(bootstrap.Context.Connections.LobbyAuthenticated,
                    "Local smoke must not pretend to have an authenticated TCP session.");
                Assert.AreEqual(SessionPhase.World, bootstrap.Context.Session.Phase);
            }
            finally { Object.Destroy(root); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator BootstrapCanStopAndRestartWithoutStaticLeaks()
        {
            for (int iteration = 0; iteration < 2; iteration++)
            {
                var root = new GameObject("Restart Test");
                var bootstrap = root.AddComponent<InsectSpaceBootstrap>();
                float deadline = Time.realtimeSinceStartup + 30;
                while (!bootstrap.Ready && bootstrap.LastError == null && Time.realtimeSinceStartup < deadline)
                    yield return null;
                bool ready = bootstrap.Ready;
                string error = bootstrap.LastError;
                var context = bootstrap.Context;
                Object.Destroy(root);
                yield return null;
                Assert.IsTrue(ready, error);
                Assert.AreEqual(SessionPhase.SignedOut, context.Session.Phase);
                Assert.Throws<System.ObjectDisposedException>(() => context.Connections.Tick(0));
            }
        }

        [Test]
        public void AllQualityLevelsUseAssignedPipelines()
        {
            int original = QualitySettings.GetQualityLevel();
            int fps = Application.targetFrameRate;
            try
            {
                foreach (QualityTier tier in System.Enum.GetValues(typeof(QualityTier)))
                {
                    QualityController.Apply(tier, tier == QualityTier.High ? 60 : 30);
                    Assert.AreEqual((int)tier, QualitySettings.GetQualityLevel());
                    Assert.NotNull(QualitySettings.renderPipeline);
                }
            }
            finally
            {
                QualitySettings.SetQualityLevel(original, true);
                Application.targetFrameRate = fps;
            }
        }
    }
}
