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
    public sealed class StylizedSkyTests
    {
        private const string ScenePath="Assets/InsectSpace/Scenes/SkyShowcase.unity";
        private Scene scene, previousScene;
        private int previousQuality;
        private StylizedSkyController sky;
        [UnitySetUp]
        public IEnumerator Load()
        {
            previousScene=SceneManager.GetActiveScene();previousQuality=QualitySettings.GetQualityLevel();
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath,new LoadSceneParameters(LoadSceneMode.Additive));
            scene=SceneManager.GetSceneByPath(ScenePath);SceneManager.SetActiveScene(scene);
            sky=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<StylizedSkyController>()).Single();
            yield return null;
        }
        [UnityTearDown]
        public IEnumerator Unload()
        {
            QualitySettings.SetQualityLevel(previousQuality,true);
            if(previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if(scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }
        [Test]
        public void SavedAssetsCompileAndUseASeparateRuntimeMaterial()
        {
            Assert.NotNull(sky.RuntimeMaterial);Assert.AreSame(sky.RuntimeMaterial,RenderSettings.skybox);
            Assert.IsFalse(EditorUtility.IsPersistent(sky.RuntimeMaterial));
            Assert.IsTrue(sky.RuntimeMaterial.shader.isSupported);Assert.IsFalse(ShaderUtil.ShaderHasError(sky.RuntimeMaterial.shader));
            var texture=sky.RuntimeMaterial.GetTexture("_CloudAtlas") as Texture2D;Assert.NotNull(texture);
            Assert.IsTrue(EditorUtility.IsPersistent(texture));Assert.AreEqual(2048,texture.width);Assert.AreEqual(1024,texture.height);Assert.IsFalse(texture.isReadable);
            var importer=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture));
            Assert.IsFalse(importer.sRGBTexture);Assert.AreEqual(TextureWrapMode.Repeat,importer.wrapModeU);
            Assert.AreEqual(TextureWrapMode.Clamp,importer.wrapModeV);Assert.IsTrue(importer.mipmapEnabled);
            foreach(var platform in new[]{"Android","iPhone"})Assert.AreEqual(TextureImporterFormat.ASTC_6x6,importer.GetPlatformTextureSettings(platform).format);
            foreach(var preset in new[]{"Daylight","GoldenHour","Moonrise"}) Assert.NotNull(AssetDatabase.LoadAssetAtPath<StylizedSkyProfile>("Assets/InsectSpace/Rendering/Sky/Profiles/"+preset+".asset"));
            var camera=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>()).Single();
            Assert.IsFalse(camera.orthographic);
            Assert.Less(Vector3.Distance(camera.transform.position,new Vector3(.35f,2.65f,-9.4f)),.001f);
            Assert.Less(Quaternion.Angle(camera.transform.rotation,Quaternion.Euler(-4,0,0)),.001f);
            var day=AssetDatabase.LoadAssetAtPath<StylizedSkyProfile>("Assets/InsectSpace/Rendering/Sky/Profiles/Daylight.asset");
            Assert.Less(Vector4.Distance(day.middle,sky.RuntimeMaterial.GetColor("_Middle")),.00001f);
        }
        [UnityTest]
        public IEnumerator InterruptedTransitionReachesTargetWithoutMutatingTemplate()
        {
            var template=AssetDatabase.LoadAssetAtPath<Material>("Assets/InsectSpace/Rendering/Sky/Materials/Daylight.mat");Color original=template.GetColor("_Zenith");
            sky.SetPreset(StylizedSkyController.Preset.Sunset,.2f);yield return new WaitForSecondsRealtime(.08f);
            sky.SetPreset(StylizedSkyController.Preset.Night,.15f);yield return new WaitForSecondsRealtime(.25f);
            Assert.IsFalse(sky.IsTransitioning);Assert.AreEqual(StylizedSkyController.Preset.Night,sky.CurrentPreset);
            Assert.That(sky.RuntimeMaterial.GetFloat("_Night"),Is.EqualTo(1).Within(.001f));Assert.AreEqual(original,template.GetColor("_Zenith"));
            sky.SetPreset(StylizedSkyController.Preset.Day,0);Assert.That(sky.RuntimeMaterial.GetFloat("_Night"),Is.EqualTo(0).Within(.001f));
        }
        [UnityTest]
        public IEnumerator CloudAnimationPausesAndQualityChangesShaderVariant()
        {
            sky.AnimateClouds=true;float before=sky.CloudPhase;yield return new WaitForSeconds(.12f);Assert.Greater(sky.CloudPhase,before);
            sky.AnimateClouds=false;before=sky.CloudPhase;yield return new WaitForSeconds(.08f);Assert.AreEqual(before,sky.CloudPhase);
            for(int q=0;q<3;q++) {QualitySettings.SetQualityLevel(q,true);yield return null;yield return null;Assert.AreEqual(q,sky.AppliedQuality);Assert.AreEqual(q>0,sky.RuntimeMaterial.IsKeywordEnabled("_SKY_DETAILS"));}
        }
        [UnityTest]
        public IEnumerator DisableRestoresEnvironmentAndReenableCreatesOneMaterial()
        {
            var runtime=sky.RuntimeMaterial;sky.enabled=false;yield return null;
            Assert.IsTrue(runtime==null);Assert.IsNull(sky.RuntimeMaterial);
            Assert.AreEqual("Daylight",RenderSettings.skybox.name);Assert.IsTrue(EditorUtility.IsPersistent(RenderSettings.skybox));
            sky.enabled=true;yield return null;Assert.NotNull(sky.RuntimeMaterial);Assert.AreSame(sky.RuntimeMaterial,RenderSettings.skybox);
            Assert.AreEqual(1,Resources.FindObjectsOfTypeAll<Material>().Count(m=>m.name=="Daylight (runtime)"));
        }
        [UnityTest]
        public IEnumerator InactiveSceneDisableRestoresBaselineOnReturn()
        {
            var other=SceneManager.CreateScene("Sky ownership test");var runtime=sky.RuntimeMaterial;
            try
            {
                SceneManager.SetActiveScene(other);yield return null;Assert.IsNull(sky.RuntimeMaterial);Assert.IsTrue(runtime==null);
                sky.enabled=false;SceneManager.SetActiveScene(scene);yield return null;
                Assert.NotNull(RenderSettings.skybox);Assert.AreEqual("Daylight",RenderSettings.skybox.name);Assert.IsTrue(EditorUtility.IsPersistent(RenderSettings.skybox));
                sky.enabled=true;yield return null;Assert.NotNull(sky.RuntimeMaterial);Assert.AreSame(sky.RuntimeMaterial,RenderSettings.skybox);
            }
            finally { if(scene.isLoaded)SceneManager.SetActiveScene(scene); }
            yield return SceneManager.UnloadSceneAsync(other);
        }
        [UnityTest]
        public IEnumerator BothCloudLayersWrapContinuously()
        {
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            typeof(StylizedSkyController).GetField("cloudPhase",flags).SetValue(sky,.99999f);
            typeof(StylizedSkyController).GetField("highCloudPhase",flags).SetValue(sky,.42f);
            sky.AnimateClouds=true;yield return new WaitForSeconds(.10f);
            var offset=sky.RuntimeMaterial.GetVector("_CloudOffset");Assert.Less(offset.x,.01f);Assert.That(offset.z,Is.InRange(.419f,.42f));
        }
    }
}
#endif
