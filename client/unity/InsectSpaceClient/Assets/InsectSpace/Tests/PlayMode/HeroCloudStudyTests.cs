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
    public sealed class HeroCloudStudyTests
    {
        private const string Path="Assets/InsectSpace/Scenes/HeroCloudStudy.unity";
        private Scene scene,previous;
        private int quality;
        private Animator actor;
        private StylizedSkyController sky;
        [UnitySetUp] public IEnumerator Load()
        {
            previous=SceneManager.GetActiveScene();quality=QualitySettings.GetQualityLevel();
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(Path,new LoadSceneParameters(LoadSceneMode.Additive));
            scene=SceneManager.GetSceneByPath(Path);SceneManager.SetActiveScene(scene);
            actor=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Animator>()).Single();
            sky=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<StylizedSkyController>()).Single();
            yield return null;
        }
        [UnityTearDown] public IEnumerator Unload()
        {
            QualitySettings.SetQualityLevel(quality,true);
            if(previous.IsValid() && previous.isLoaded)SceneManager.SetActiveScene(previous);
            if(scene.IsValid() && scene.isLoaded)yield return SceneManager.UnloadSceneAsync(scene);
        }
        [Test] public void CharacterHasValidHumanoidSkinAndLoopingMotion()
        {
            Assert.IsTrue(actor.isInitialized);Assert.IsTrue(actor.isHuman);Assert.IsTrue(actor.avatar.isValid);
            Assert.IsFalse(actor.applyRootMotion);
            var clip=actor.runtimeAnimatorController.animationClips.Single();
            Assert.IsTrue(clip.isLooping);Assert.That(clip.length,Is.InRange(9f,11f));
            var skins=actor.GetComponentsInChildren<SkinnedMeshRenderer>();Assert.AreEqual(7,skins.Length);
            foreach(var skin in skins)
            {
                Assert.Greater(skin.bones.Length,0);Assert.IsFalse(skin.bones.Any(b=>b==null));Assert.NotNull(skin.rootBone);
                Assert.Greater(skin.sharedMesh.vertexCount,0);
                foreach(var material in skin.sharedMaterials)
                {Assert.NotNull(material);Assert.AreEqual("InsectSpace/Painterly Hero",material.shader.name);Assert.IsFalse(ShaderUtil.ShaderHasError(material.shader));Assert.NotNull(material.GetTexture("_BaseMap"));}
            }
            var b=skins[0].bounds;foreach(var skin in skins)b.Encapsulate(skin.bounds);
            Assert.That(b.size.y,Is.InRange(1.7f,2.7f));
        }
        [UnityTest] public IEnumerator IdleActuallyDeformsSkinAndKeepsRootStationary()
        {
            var skin=actor.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s=>s.name=="body");
            var before=new Mesh();var after=new Mesh();
            try
            {
                var root=actor.transform.position;actor.Play("BreathingIdle",0,.1f);actor.Update(0);skin.BakeMesh(before);
                float time=actor.GetCurrentAnimatorStateInfo(0).normalizedTime;
                yield return new WaitForSeconds(.65f);
                skin.BakeMesh(after);Assert.Greater(actor.GetCurrentAnimatorStateInfo(0).normalizedTime,time);
                float delta=0;var a=before.vertices;var b=after.vertices;Assert.AreEqual(a.Length,b.Length);
                for(int i=0;i<a.Length;i++)delta=Mathf.Max(delta,(a[i]-b[i]).sqrMagnitude);
                Assert.Greater(delta,.0000001f,"Animator time advanced but skin did not deform.");
                Assert.Less(Vector3.Distance(root,actor.transform.position),.0001f);
                actor.Play("BreathingIdle",0,.99f);actor.Update(0);
                yield return new WaitForSeconds(.35f);
                Assert.IsTrue(actor.GetCurrentAnimatorStateInfo(0).loop);
                Assert.Greater(actor.GetCurrentAnimatorStateInfo(0).normalizedTime,1f);
            }
            finally {Object.Destroy(before);Object.Destroy(after);}
        }
        [UnityTest] public IEnumerator GrassDensityAndSkyPresetsRemainFunctional()
        {
            var lod=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeadowGrassLod>()).Single();
            Assert.AreEqual(36,lod.ChunkCount);long last=0;
            for(int q=0;q<3;q++)
            {
                QualitySettings.SetQualityLevel(q,true);yield return null;yield return null;
                long vertices=lod.GetComponentsInChildren<MeshFilter>().Where(f=>f.name.StartsWith("Blades_")).Sum(f=>(long)f.sharedMesh.vertexCount);
                Assert.Greater(vertices,last);last=vertices;Assert.AreEqual(q,lod.AppliedQuality);Assert.AreEqual(q,sky.AppliedQuality);
            }
            var atlas=(Texture2D)sky.RuntimeMaterial.GetTexture("_CloudAtlas");
            Assert.IsFalse(atlas.isReadable);Assert.AreEqual(4096,atlas.width);Assert.AreEqual(2048,atlas.height);
            Assert.IsFalse(ShaderUtil.ShaderHasError(sky.RuntimeMaterial.shader));
            foreach(var preset in new[]{StylizedSkyController.Preset.Sunset,StylizedSkyController.Preset.Night,StylizedSkyController.Preset.Day})
            {sky.SetPreset(preset,0);Assert.AreEqual(preset,sky.CurrentPreset);}
            Assert.NotNull(scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<HeroStudyControls>()).Single());
        }
        [UnityTest] public IEnumerator HeroAndExploreKeepFullCharacterVisibleAcrossAspectChanges()
        {
            var camera=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>()).Single();
            var framing=camera.GetComponent<HeroStudyFraming>();Assert.NotNull(framing);
            float originalAspect=camera.aspect;
            try
            {
                foreach(var mode in new[]{HeroStudyFraming.ViewMode.Hero,HeroStudyFraming.ViewMode.Explore})
                {
                    framing.SetMode(mode);
                    foreach(float aspect in new[]{16f/9f,9f/16f,3f/4f})
                    {
                        camera.aspect=aspect;yield return null;yield return null;
                        Assert.IsFalse(camera.orthographic);Assert.AreEqual(mode,framing.Mode);
                        var baked=new Mesh();
                        try
                        {
                            foreach(float pose in new[]{.1f,.5f,.9f})
                            {
                                actor.Play("BreathingIdle",0,pose);actor.Update(0);
                                foreach(var skin in actor.GetComponentsInChildren<SkinnedMeshRenderer>())
                                {
                                    // Renderer bounds include spare space for unrelated animations.
                                    skin.BakeMesh(baked);baked.RecalculateBounds();var bounds=baked.bounds;
                                    for(int i=0;i<8;i++)
                                    {
                                        var corner=bounds.center+Vector3.Scale(bounds.extents,
                                            new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                                        var point=camera.WorldToViewportPoint(skin.transform.TransformPoint(corner));
                                        Assert.Greater(point.z,camera.nearClipPlane);
                                        Assert.That(point.x,Is.InRange(.04f,.96f),mode+" horizontal clipping at "+aspect);
                                        Assert.That(point.y,Is.InRange(.07f,.94f),mode+" vertical clipping at "+aspect);
                                    }
                                }
                            }
                        }
                        finally {Object.Destroy(baked);}
                    }
                }
            }
            finally {camera.aspect=originalAspect;framing.SetMode(HeroStudyFraming.ViewMode.Hero);}
        }
        [UnityTest] public IEnumerator MistTransitionsAndRestoresFogWithoutChangingAssets()
        {
            var originalSky=AssetDatabase.LoadAssetAtPath<Material>("Assets/InsectSpace/Rendering/HeroStudy/Materials/HeroCloudSky.mat");
            var originalColor=originalSky.GetColor("_Horizon");
            var turf=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Renderer>()).Single(r=>r.name=="RollingTurf");
            var originalTint=turf.sharedMaterial.GetColor("_BaseColor");
            var camera=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>()).Single();
            Vector3 position=camera.transform.position;Quaternion rotation=camera.transform.rotation;
            sky.SetPreset(StylizedSkyController.Preset.Mist,.15f);yield return new WaitForSecondsRealtime(.22f);
            Assert.IsFalse(sky.IsTransitioning);Assert.That(sky.AtmosphereStrength,Is.EqualTo(1).Within(.001f));
            Assert.IsTrue(RenderSettings.fog);Assert.AreEqual(FogMode.Linear,RenderSettings.fogMode);
            Assert.That(RenderSettings.fogStartDistance,Is.EqualTo(7).Within(.01f));Assert.That(RenderSettings.fogEndDistance,Is.EqualTo(38).Within(.01f));
            Assert.That(sky.RuntimeMaterial.GetFloat("_Coverage"),Is.EqualTo(0).Within(.001f));
            var block=new MaterialPropertyBlock();turf.GetPropertyBlock(block);Assert.That(block.GetFloat("_MistIntensity"),Is.EqualTo(1).Within(.001f));
            Assert.AreEqual(originalColor,originalSky.GetColor("_Horizon"));Assert.AreEqual(originalTint,turf.sharedMaterial.GetColor("_BaseColor"));
            Assert.AreEqual(position,camera.transform.position);Assert.AreEqual(rotation,camera.transform.rotation);
            sky.SetPreset(StylizedSkyController.Preset.Day,.1f);yield return new WaitForSecondsRealtime(.18f);
            Assert.That(RenderSettings.fogEndDistance,Is.EqualTo(180).Within(.01f));Assert.That(sky.AtmosphereStrength,Is.EqualTo(0).Within(.001f));
            turf.GetPropertyBlock(block);Assert.That(block.GetFloat("_MistIntensity"),Is.EqualTo(0).Within(.001f));
            sky.SetPreset(StylizedSkyController.Preset.Mist,0);yield return null;
            sky.enabled=false;yield return null;
            Assert.That(RenderSettings.fogStartDistance,Is.EqualTo(58).Within(.01f));Assert.That(RenderSettings.fogEndDistance,Is.EqualTo(180).Within(.01f));
            sky.enabled=true;yield return null;
            Assert.AreEqual(StylizedSkyController.Preset.Day,sky.CurrentPreset);
            sky.SetPreset(StylizedSkyController.Preset.Mist,0);yield return null;
            var alternate=SceneManager.CreateScene("Hero fog ownership test");SceneManager.SetActiveScene(alternate);yield return null;
            sky.enabled=false;SceneManager.SetActiveScene(scene);yield return null;
            Assert.That(RenderSettings.fogEndDistance,Is.EqualTo(180).Within(.01f));
            sky.enabled=true;yield return null;
            yield return SceneManager.UnloadSceneAsync(alternate);
        }
    }
}
#endif
