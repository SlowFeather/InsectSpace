using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using InsectSpace.Rendering;

public static class VerifyAfkRecovered
{
    public static object CaptureCurrent()
    {
        var study=UnityEngine.Object.FindAnyObjectByType<AfkRecoveredStudy>();
        if(!study)throw new InvalidOperationException("Open the recovered lakeside study.");
        var camera=study.View;float aspect=camera.aspect;
        string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../../.artifacts/validation/afk-recovered/2026-10-09/calibration"));
        Directory.CreateDirectory(folder);
        string path=Path.Combine(folder,"Current-Portrait.png");
        try {camera.aspect=9f/16;study.ApplyFraming();Frame(camera,1080,1920,path);}
        finally {camera.aspect=aspect;study.ApplyFraming();}
        return new {path,timeOfDay=study.Environment.TimeOfDay};
    }
    static Color32[] Frame(Camera camera,int width,int height,string path=null)
    {
        var rt=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32);
        var previous=camera.targetTexture;var active=RenderTexture.active;float aspect=camera.aspect;
        bool asyncCompilation=ShaderUtil.allowAsyncCompilation;
        var image=new Texture2D(width,height,TextureFormat.RGB24,false);
        try {
            ShaderUtil.allowAsyncCompilation=false;
            camera.aspect=(float)width/height;camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
            if(path!=null)File.WriteAllBytes(path,image.EncodeToPNG());return image.GetPixels32();
        } finally {ShaderUtil.allowAsyncCompilation=asyncCompilation;camera.targetTexture=previous;camera.aspect=aspect;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(image);}
    }
    static int LightProbe(AfkRecoveredStudy study,Renderer[] renderers,bool onlyActor,string folder)
    {
        var enabled=renderers.Select(r=>r.enabled).ToArray();
        try {
            for(int i=0;i<renderers.Length;i++)
                renderers[i].enabled=enabled[i]&&(renderers[i].transform.IsChildOf(study.Actor.transform)==onlyActor);
            study.ActorLight.enabled=true;
            var lit=Frame(study.View,540,960,Path.Combine(folder,onlyActor?"Actor-Light-On.png":"Ground-Light-On.png"));
            study.ActorLight.enabled=false;
            var unlit=Frame(study.View,540,960,Path.Combine(folder,onlyActor?"Actor-Light-Off.png":"Ground-Light-Off.png"));
            int changed=0;
            for(int i=0;i<lit.Length;i++)
                if(Math.Abs(lit[i].r-unlit[i].r)+Math.Abs(lit[i].g-unlit[i].g)+Math.Abs(lit[i].b-unlit[i].b)>25)changed++;
            return changed;
        } finally {
            for(int i=0;i<renderers.Length;i++)renderers[i].enabled=enabled[i];
            study.ApplyActorLight();
        }
    }
    public static object Run()
    {
        if(!Application.isPlaying)throw new InvalidOperationException("Enter Play in AFKRecoveredLakeside.");
        var study=UnityEngine.Object.FindAnyObjectByType<AfkRecoveredStudy>();
        if(!study)throw new InvalidOperationException("Wrong study scene.");
        var actor=study.Actor;var camera=study.View;var env=study.Environment;
        if(!actor||!actor.avatar||!actor.avatar.isValid||actor.applyRootMotion)throw new InvalidOperationException("Invalid original rig.");
        var clip=actor.runtimeAnimatorController.animationClips.Single();
        if(!clip.isLooping)throw new InvalidOperationException("Original idle must loop.");
        var renderers=study.GetComponentsInChildren<Renderer>();
        foreach(var renderer in renderers.Where(r=>r.enabled))
            foreach(var material in renderer.sharedMaterials)
                if(!material||!material.shader||ShaderUtil.ShaderHasError(material.shader))throw new InvalidOperationException("Missing/failed material on "+renderer.name);
        var skins=actor.GetComponentsInChildren<SkinnedMeshRenderer>().Where(s=>!s.sharedMaterials.All(m=>m.HasProperty("_BaseColor")&&m.GetColor("_BaseColor").a==0)).ToArray();
        if(skins.Any(s=>s.bones.Any(b=>!b)))throw new InvalidOperationException("Missing rig bones.");
        var opaque=skins.SelectMany(s=>s.sharedMaterials).Where(m=>!m.name.Contains("glass")&&!m.name.Contains("shadow")).Distinct().ToArray();
        if(opaque.Length!=3||opaque.Any(m=>m.shader.name!="InsectSpace/Local AFK Character"))throw new InvalidOperationException("Original character material reconstruction missing.");
        float speed=actor.speed,aspect=camera.aspect,time=env.TimeOfDay;bool cycle=env.Cycle;var position=actor.transform.position;
        var state=actor.GetCurrentAnimatorStateInfo(0);var mesh=new Mesh();var captures=new List<object>();
        float deformation=0;int shadowPixels=0,actorLightPixels=0,groundLightPixels=0,pointShadowPixels=0;var oldShadows=env.Sun.shadows;
        var oldPointShadows=study.ActorLight.shadows;
        string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../../.artifacts/validation/afk-recovered/2026-10-09"));Directory.CreateDirectory(folder);
        try {
            actor.speed=0;
            foreach(var skin in skins) {
                actor.Play("OriginalIdle",0,.1f);actor.Update(0);skin.BakeMesh(mesh);var before=mesh.vertices;
                actor.Play("OriginalIdle",0,.7f);actor.Update(0);skin.BakeMesh(mesh);var after=mesh.vertices;
                for(int i=0;i<before.Length;i++)deformation=Mathf.Max(deformation,(after[i]-before[i]).sqrMagnitude);
            }
            if(deformation<.0000001f||Vector3.Distance(position,actor.transform.position)>.0001f)throw new InvalidOperationException("Idle deformation/root test failed.");
            foreach(bool night in new[]{false,true}) {
                study.SetNight(night);
                if(!study.ActorLight||study.ActorLight.enabled!=night)throw new InvalidOperationException("Actor light day/night state incorrect.");
                foreach(var size in new[]{new Vector2Int(1600,900),new Vector2Int(1080,1920)}) {
                    camera.aspect=(float)size.x/size.y;study.ApplyFraming();
                    foreach(var skin in skins) {
                        skin.BakeMesh(mesh);
                        foreach(var point in mesh.vertices) {
                            var p=camera.WorldToViewportPoint(skin.transform.TransformPoint(point));
                            if(p.z<camera.nearClipPlane||p.x<.02f||p.x>.98f||p.y<.03f||p.y>.97f)throw new InvalidOperationException("Character clipped.");
                        }
                    }
                    string path=Path.Combine(folder,(night?"Night":"Day")+"-"+(size.x>size.y?"Desktop":"Portrait")+".png");
                    var pixels=Frame(camera,size.x,size.y,path);var colors=new HashSet<int>();int magenta=0;
                    for(int i=0;i<pixels.Length;i+=19) {var c=pixels[i];colors.Add((c.r/8<<10)+(c.g/8<<5)+c.b/8);if(c.r>240&&c.b>240&&c.g<20)magenta++;}
                    if(colors.Count<100||magenta>20)throw new InvalidOperationException("Blank or magenta frame.");
                    captures.Add(new {night,width=size.x,height=size.y,quantizedColors=colors.Count,magentaSamples=magenta,path});
                }
            }
            study.SetNight(true);camera.aspect=9f/16;study.ApplyFraming();
            actorLightPixels=LightProbe(study,renderers,true,folder);
            groundLightPixels=LightProbe(study,renderers,false,folder);
            if(actorLightPixels<50||groundLightPixels<500)throw new InvalidOperationException("Actor/ground local light has no meaningful pixel effect: "+actorLightPixels+" / "+groundLightPixels);
            // Exercise the punctual caster variant without changing the composed lighting preset.
            study.ActorLight.shadows=LightShadows.None;
            var pointFlat=Frame(camera,540,960);
            study.ActorLight.shadows=LightShadows.Soft;
            var pointShaded=Frame(camera,540,960);
            for(int i=0;i<pointFlat.Length;i++)if(Math.Abs(pointFlat[i].r-pointShaded[i].r)+Math.Abs(pointFlat[i].g-pointShaded[i].g)+Math.Abs(pointFlat[i].b-pointShaded[i].b)>30)pointShadowPixels++;
            if(pointShadowPixels<100)throw new InvalidOperationException("Punctual shadows have no visible effect.");
            study.ActorLight.shadows=oldPointShadows;
            study.SetNight(false);camera.aspect=9f/16;study.ApplyFraming();
            var shaded=Frame(camera,320,568);env.Sun.shadows=LightShadows.None;var flat=Frame(camera,320,568);
            for(int i=0;i<shaded.Length;i++)if(Math.Abs(shaded[i].r-flat[i].r)+Math.Abs(shaded[i].g-flat[i].g)+Math.Abs(shaded[i].b-flat[i].b)>30)shadowPixels++;
            if(shadowPixels<100)throw new InvalidOperationException("Realtime shadows have no visible effect.");
        } finally {
            env.Sun.shadows=oldShadows;env.TimeOfDay=time;env.Cycle=cycle;env.Apply();study.ApplyActorLight();
            study.ActorLight.shadows=oldPointShadows;
            actor.Play(state.fullPathHash,0,state.normalizedTime);actor.Update(0);actor.speed=speed;
            camera.aspect=aspect;study.ApplyFraming();UnityEngine.Object.DestroyImmediate(mesh);
        }
        var report=new {unity=Application.unityVersion,scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().path,
            renderers=renderers.Length,skins=skins.Length,avatarValid=actor.avatar.isValid,clip=clip.name,seconds=clip.length,
            deformationSquared=deformation,stationaryRoot=true,realtimeShadowPixels=shadowPixels,
            opaqueCharacterMaterials=opaque.Length,actorLightDisabledInDay=true,actorLightPixels,groundLightPixels,pointShadowPixels,captures};
        File.WriteAllText(Path.Combine(folder,"render-probe.json"),Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));return report;
    }
}
