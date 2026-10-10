using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using InsectSpace.Rendering;

public static class VerifyAfkWaterFairy
{
    static string evidenceSet="2026-10-09/water-fairy";
    static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath,"../../../../.artifacts/validation/afk-recovered",evidenceSet));
    public static object RunShoreWaves()
    {
        evidenceSet="2026-10-10/shore-waves/water-fairy";
        try {return Run();} finally {evidenceSet="2026-10-09/water-fairy";}
    }
    static void Require(bool condition,string message) {if(!condition)throw new InvalidOperationException(message);}
    static Color32[] Capture(Camera camera,string name,int width=1600,int height=900)
    {
        var rt=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32);
        var target=camera.targetTexture;var active=RenderTexture.active;float aspect=camera.aspect;
        bool asyncCompilation=ShaderUtil.allowAsyncCompilation;
        var image=new Texture2D(width,height,TextureFormat.RGB24,false);
        try {
            ShaderUtil.allowAsyncCompilation=false;camera.aspect=(float)width/height;camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
            File.WriteAllBytes(Path.Combine(Folder,name+".png"),image.EncodeToPNG());return image.GetPixels32();
        } finally {
            camera.targetTexture=target;camera.aspect=aspect;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);
            UnityEngine.Object.DestroyImmediate(image);ShaderUtil.allowAsyncCompilation=asyncCompilation;
        }
    }
    static int Difference(Color32[] a,Color32[] b)
    {
        return a.Zip(b,(x,y)=>Math.Abs(x.r-y.r)+Math.Abs(x.g-y.g)+Math.Abs(x.b-y.b)).Count(d=>d>6);
    }
    public static object Run()
    {
        Require(Application.isPlaying,"Enter Play in the recovered lakeside scene.");
        var study=UnityEngine.Object.FindAnyObjectByType<AfkRecoveredStudy>();
        Require(study&&study.FairyVisual&&study.ActorLight,"Missing fairy source or light.");
        var camera=study.View;var env=study.Environment;
        var water=camera.GetComponent<AfkStudyCameraEffects>().Water.sharedMaterial;
        bool visible=study.FairyVisible,cycle=env.Cycle;
        float time=env.TimeOfDay,speed=study.Actor.speed,aspect=camera.aspect,size=camera.orthographicSize;
        float shore=water.GetFloat("_StudySoftShore"),ripple=water.GetFloat("_StudyRippleStrength");
        var position=camera.transform.position;
        Directory.CreateDirectory(Folder);var differences=new Dictionary<string,int>();var states=new List<object>();
        try {
            study.Actor.speed=0;env.Cycle=false;camera.aspect=16f/9;study.ApplyFraming();
            foreach(bool night in new[]{false,true}) {
                study.SetNight(night);study.SetFairyVisible(true);
                Require(study.FairyVisual.activeSelf,"Fairy visual did not show.");
                var on=Capture(camera,(night?"Night":"Day")+"-Fairy-On");
                study.SetFairyVisible(false);
                Require(!study.FairyVisual.activeSelf&&!study.ActorLight.enabled&&study.ActorLight.intensity==0,"Hiding leaves fairy illumination enabled.");
                var off=Capture(camera,(night?"Night":"Day")+"-Fairy-Off");
                differences[night?"NightFairy":"DayFairy"]=Difference(on,off);
                Capture(camera,(night?"Night":"Day")+"-Portrait",1080,1920);
            }
            // Both preset changes and a cycle wrap must respect the user's hidden state.
            foreach(float t in new[]{.05f,.25f,.5f,.75f,.999f}) {
                env.TimeOfDay=t;env.Apply();study.ApplyActorLight();
                Require(!study.FairyVisual.activeSelf&&!study.ActorLight.enabled&&study.ActorLight.intensity==0,"Time change restored a hidden fairy.");
                states.Add(new{time=t,visible=study.FairyVisual.activeSelf,light=study.ActorLight.enabled,intensity=study.ActorLight.intensity});
            }
            env.Advance(env.CycleSeconds*.01f);study.ApplyActorLight();
            Require(env.TimeOfDay<.02f&&!study.ActorLight.enabled&&!study.FairyVisual.activeSelf,"Cycle wrap restored a hidden fairy.");
            study.SetNight(true);study.SetFairyVisible(true);
            Require(study.ActorLight.enabled&&study.ActorLight.intensity>0&&study.FairyVisual.activeSelf,"Fairy could not be restored.");
            study.SetNight(false);study.SetFairyVisible(false);
            camera.orthographicSize=4.6f;
            camera.transform.position=new Vector3(-3.5f,0,4)-camera.transform.forward*48;
            var detail=Capture(camera,"Water-Detail");
            water.SetFloat("_StudySoftShore",0);
            differences["SoftShore"]=Difference(detail,Capture(camera,"Water-Hard-Edge"));water.SetFloat("_StudySoftShore",shore);
            water.SetFloat("_StudyRippleStrength",0);
            differences["FineRipples"]=Difference(detail,Capture(camera,"Water-Ripples-Off"));water.SetFloat("_StudyRippleStrength",ripple);
            study.SetNight(true);Capture(camera,"Water-Night-Detail");
            foreach(var pair in differences)Require(pair.Value>10,"No visible change: "+pair.Key);
        } finally {
            water.SetFloat("_StudySoftShore",shore);water.SetFloat("_StudyRippleStrength",ripple);
            env.TimeOfDay=time;env.Cycle=cycle;env.Apply();study.SetFairyVisible(visible);
            study.Actor.speed=speed;camera.aspect=aspect;study.ApplyFraming();camera.orthographicSize=size;camera.transform.position=position;
        }
        var report=new{differences,hiddenStates=states,cycleWrapPreservedHidden=true,restoredSuccessfully=true};
        File.WriteAllText(Path.Combine(Folder,"water-fairy-probe.json"),Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));
        return report;
    }
}
