using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using InsectSpace.Rendering;

public static class VerifyAfkShoreWaves
{
    static string Folder=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../../../.artifacts/validation/afk-recovered/2026-10-10/shore-waves"));
    static void Require(bool state,string reason){if(!state)throw new InvalidOperationException(reason);}
    static Color[] Frame(Camera camera,string name,bool floating=false)
    {
        var target=camera.targetTexture;var active=RenderTexture.active;
        var rt=RenderTexture.GetTemporary(960,540,24,floating?RenderTextureFormat.ARGBFloat:RenderTextureFormat.ARGB32,floating?RenderTextureReadWrite.Linear:RenderTextureReadWrite.Default);
        var image=new Texture2D(960,540,floating?TextureFormat.RGBAFloat:TextureFormat.RGB24,false,floating);
        try {
            camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();
            if(name!=null)File.WriteAllBytes(Path.Combine(Folder,name+".png"),image.EncodeToPNG());
            return image.GetPixels();
        } finally {camera.targetTexture=target;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(image);}
    }
    static object NumericProbe(Camera camera,Renderer water,Material material,out float travel)
    {
        var mesh=water.GetComponent<MeshFilter>().sharedMesh;
        var origin=mesh.vertices.Where(v=>Mathf.Abs(v.z)<.08f).OrderByDescending(v=>v.x).First();
        origin=water.transform.TransformPoint(origin);
        var positions=new List<float>();var peakMasks=new List<float>();float farMask=0,periodError=0;
        Color[] first=null;
        float speed=material.GetFloat("_StudyShoreWaveSpeed"),range=material.GetFloat("_StudyShoreWaveRange");
        Require(speed>0,"Shore wave speed must be positive.");
        float period=material.GetFloat("_StudyShoreWaveSpacing")/speed;
        var times=new[]{period*.4f,period*.7f,period*1.4f};
        foreach(float time in times) {
            material.SetFloat("_StudyShoreWaveTime",time);
            var pixels=Frame(camera,null,true);
            if(first==null)first=pixels;
            else if(time==times[2])for(int i=0;i<pixels.Length;i++)periodError=Mathf.Max(periodError,Mathf.Abs(first[i].r-pixels[i].r));
            double weight=0,distance=0;float peak=0;
            for(int i=0;i<=480;i++) {
                var point=origin+Vector3.left*(.05f+i*.006f);
                var uv=camera.WorldToViewportPoint(point);
                Require(uv.x>0&&uv.x<1&&uv.y>0&&uv.y<1,"Wave probe outside viewport.");
                var value=pixels[Mathf.Clamp((int)(uv.y*540),0,539)*960+Mathf.Clamp((int)(uv.x*960),0,959)];
                if(value.g>.1f&&value.g<.65f&&value.b>.8f) {weight+=value.b;distance+=value.g*value.b;peak=Mathf.Max(peak,value.r);}
                if(value.g>range+.05f&&value.g<2.8f)farMask=Mathf.Max(farMask,value.r);
            }
            Require(weight>2,"No resolved moving crest.");positions.Add((float)(distance/weight));peakMasks.Add(peak);
        }
        travel=positions[1]-positions[0];
        float expected=-speed*(times[1]-times[0]);
        Require(travel<0&&Mathf.Abs(travel-expected)<.08f,"Shore crest did not move toward the bank at configured speed: "+travel);
        Require(farMask<.001f,"Shore crests do not fade before deep water.");
        Require(periodError<.002f,"Wave emission jumps at the two-second wrap: "+periodError);
        return new{times,crestDistances=positions,peakMasks,shorewardTravel=travel,expectedTravel=expected,farMask,periodError};
    }
    static object SwayProbe(Camera camera,Renderer water,Material material)
    {
        var origin=water.transform.TransformPoint(water.GetComponent<MeshFilter>().sharedMesh.vertices.Where(v=>Mathf.Abs(v.z)<.08f).OrderByDescending(v=>v.x).First());
        float period=material.GetFloat("_StudyShoreWaveSpacing")/material.GetFloat("_StudyShoreWaveSpeed");
        var samples=new List<object>();float low=float.MaxValue,high=float.MinValue,minimumWash=float.MaxValue,maximumWash=float.MinValue,deepMotion=0,repeatError=0;
        Color[] first=null;
        material.SetFloat("_StudyDebugView",5);
        for(int frame=0;frame<=20;frame++) {
            float time=frame*period/20;material.SetFloat("_StudyShoreWaveTime",time);
            var pixels=Frame(camera,null,true);
            if(first==null)first=pixels;
            else if(frame==20)for(int i=0;i<pixels.Length;i++)repeatError=Mathf.Max(repeatError,Mathf.Abs(first[i].r-pixels[i].r)+Mathf.Abs(first[i].b-pixels[i].b));
            var values=new List<Color>();
            for(int i=0;i<480;i++) {
                var uv=camera.WorldToViewportPoint(origin+Vector3.left*(.05f+i*.006f));
                var value=pixels[Mathf.Clamp((int)(uv.y*540),0,539)*960+Mathf.Clamp((int)(uv.x*960),0,959)];
                if(value.g>.04f&&value.g<.1f)values.Add(value);
                if(value.g>1.3f&&value.g<2.8f)deepMotion=Mathf.Max(deepMotion,Mathf.Abs(value.r-origin.y));
            }
            Require(values.Count>0,"No resolved shoreline fluctuation sample.");
            float height=values.Average(v=>v.r),wash=values.Average(v=>v.b-v.g);
            low=Mathf.Min(low,height);high=Mathf.Max(high,height);minimumWash=Mathf.Min(minimumWash,wash);maximumWash=Mathf.Max(maximumWash,wash);
            samples.Add(new{time,height,wash});
        }
        Require(high-low>.006f&&high-low<.025f,"Near-shore height is static or excessive: "+(high-low));
        Require(maximumWash-minimumWash>.025f&&maximumWash-minimumWash<.08f,"Waterline wash is static or excessive.");
        Require(deepMotion<.001f,"Shore displacement leaks into deep water.");
        Require(repeatError<.002f,"Shore fluctuation jumps at period wrap.");
        return new{samples,heightPeakToPeak=high-low,waterlinePeakToPeak=maximumWash-minimumWash,deepMotion,repeatError};
    }
    public static object Run()
    {
        Require(Application.isPlaying,"Enter Play in the recovered study.");
        var study=UnityEngine.Object.FindAnyObjectByType<AfkRecoveredStudy>();
        var camera=study.View;var env=study.Environment;var effects=camera.GetComponent<AfkStudyCameraEffects>();
        var data=camera.GetUniversalAdditionalCameraData();var water=effects.Water.sharedMaterial;
        float time=env.TimeOfDay,aspect=camera.aspect,size=camera.orthographicSize,speed=study.Actor.speed;
        float overrideTime=water.GetFloat("_StudyShoreWaveTime"),strength=water.GetFloat("_StudyShoreWaveStrength");
        float swayHeight=water.GetFloat("_StudyShoreSwayHeight"),swayWidth=water.GetFloat("_StudyShoreSwayWidth"),debug=water.GetFloat("_StudyDebugView");
        var position=camera.transform.position;
        bool cycle=env.Cycle,fairy=study.FairyVisible,fog=effects.VolumetricFog,post=data.renderPostProcessing,planar=effects.PlanarReflection,async=ShaderUtil.allowAsyncCompilation;
        object probe=null,sway=null;int visiblePixels=0,swayPixels=0;
        Directory.CreateDirectory(Folder);
        try {
            ShaderUtil.allowAsyncCompilation=false;study.Actor.speed=0;study.SetNight(false);study.SetFairyVisible(false);
            camera.aspect=16f/9;camera.orthographicSize=4.6f;camera.transform.position=new Vector3(-3.5f,0,4)-camera.transform.forward*48;
            effects.VolumetricFog=false;effects.PlanarReflection=false;data.renderPostProcessing=false;water.SetFloat("_StudyDebugView",4);
            probe=NumericProbe(camera,effects.Water,water,out _);
            sway=SwayProbe(camera,effects.Water,water);
            water.SetFloat("_StudyDebugView",0);effects.VolumetricFog=fog;effects.PlanarReflection=planar;data.renderPostProcessing=post;
            water.SetFloat("_StudyShoreWaveTime",.6f);var on=Frame(camera,"Day-Waves");water.SetFloat("_StudyShoreWaveStrength",0);
            var off=Frame(camera,"Day-Waves-Off");water.SetFloat("_StudyShoreWaveStrength",strength);
            for(int i=0;i<on.Length;i++)if(Mathf.Abs(on[i].r-off[i].r)+Mathf.Abs(on[i].g-off[i].g)+Mathf.Abs(on[i].b-off[i].b)>.018f)visiblePixels++;
            Require(visiblePixels>200,"Shoreward crests are not visible.");
            water.SetFloat("_StudyShoreWaveStrength",0);var moving=Frame(camera,"Day-Sway");
            water.SetFloat("_StudyShoreSwayHeight",0);water.SetFloat("_StudyShoreSwayWidth",0);var still=Frame(camera,"Day-Sway-Off");
            for(int i=0;i<moving.Length;i++)if(Mathf.Abs(moving[i].r-still[i].r)+Mathf.Abs(moving[i].g-still[i].g)+Mathf.Abs(moving[i].b-still[i].b)>.012f)swayPixels++;
            Require(swayPixels>100,"Shoreline fluctuation does not affect the composed image.");
            water.SetFloat("_StudyShoreSwayHeight",swayHeight);water.SetFloat("_StudyShoreSwayWidth",swayWidth);water.SetFloat("_StudyShoreWaveStrength",strength);
            for(int i=0;i<40;i++){water.SetFloat("_StudyShoreWaveTime",i*.1f);Frame(camera,"Frame-"+i.ToString("D2"));}
            study.SetNight(true);water.SetFloat("_StudyShoreWaveTime",.6f);Frame(camera,"Night-Waves");
        } finally {
            water.SetFloat("_StudyShoreWaveTime",overrideTime);water.SetFloat("_StudyShoreWaveStrength",strength);water.SetFloat("_StudyDebugView",debug);
            water.SetFloat("_StudyShoreSwayHeight",swayHeight);water.SetFloat("_StudyShoreSwayWidth",swayWidth);
            env.TimeOfDay=time;env.Cycle=cycle;env.Apply();study.SetFairyVisible(fairy);study.Actor.speed=speed;
            effects.VolumetricFog=fog;effects.PlanarReflection=planar;data.renderPostProcessing=post;
            camera.aspect=aspect;study.ApplyFraming();camera.orthographicSize=size;camera.transform.position=position;ShaderUtil.allowAsyncCompilation=async;
        }
        var result=new{probe,sway,visiblePixels,swayPixels,frames=40,seconds=4,restoredRealtime=water.GetFloat("_StudyShoreWaveTime")<0};
        File.WriteAllText(Path.Combine(Folder,"shore-waves-probe.json"),Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented));
        return result;
    }
}
