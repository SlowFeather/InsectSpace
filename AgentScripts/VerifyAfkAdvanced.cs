using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using InsectSpace.Rendering;

public static class VerifyAfkAdvanced
{
    static string evidenceSet="advanced";
    static string evidenceDate="2026-10-09";
    static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath,"../../../../.artifacts/validation/afk-recovered",evidenceDate,evidenceSet));
    public static object RunShoreWaves()
    {
        evidenceDate="2026-10-10";evidenceSet="shore-waves/advanced";
        try {return Run();} finally {evidenceDate="2026-10-09";evidenceSet="advanced";}
    }
    public static object RunWaterFairy()
    {
        evidenceSet="water-fairy";
        try {return Run();} finally {evidenceSet="advanced";}
    }
    static Color32[] Read(RenderTexture texture, string name)
    {
        var active=RenderTexture.active;
        var image=new Texture2D(texture.width,texture.height,TextureFormat.RGB24,false);
        try {
            RenderTexture.active=texture;
            image.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);image.Apply();
            if(name!=null)File.WriteAllBytes(Path.Combine(Folder,name+".png"),image.EncodeToPNG());
            return image.GetPixels32();
        } finally {RenderTexture.active=active;UnityEngine.Object.DestroyImmediate(image);}
    }
    static Color32[] Frame(Camera camera,string name=null,int width=540,int height=960)
    {
        var rt=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32);
        var target=camera.targetTexture;float aspect=camera.aspect;
        bool asyncCompilation=ShaderUtil.allowAsyncCompilation;
        try {
            ShaderUtil.allowAsyncCompilation=false;camera.aspect=(float)width/height;camera.targetTexture=rt;
            camera.Render();return Read(rt,name);
        } finally {camera.targetTexture=target;camera.aspect=aspect;ShaderUtil.allowAsyncCompilation=asyncCompilation;RenderTexture.ReleaseTemporary(rt);}
    }
    static int Difference(Color32[] a,Color32[] b,int threshold=6)
    {
        int count=0;
        for(int i=0;i<a.Length;i++)if(Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b)>threshold)count++;
        return count;
    }
    static void Require(bool condition,string reason) {if(!condition)throw new InvalidOperationException(reason);}
    static object DepthProbe(AfkRecoveredStudy study,AfkStudyCameraEffects effects)
    {
        var camera=study.View;var data=camera.GetUniversalAdditionalCameraData();
        var target=camera.targetTexture;var active=RenderTexture.active;
        bool fog=effects.VolumetricFog,planar=effects.PlanarReflection,post=data.renderPostProcessing;
        var rt=RenderTexture.GetTemporary(540,960,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
        var image=new Texture2D(540,960,TextureFormat.RGBAFloat,false,true);
        var samples=new List<object>();
        try {
            effects.VolumetricFog=false;data.renderPostProcessing=false;
            effects.Water.sharedMaterial.SetFloat("_StudyDebugView",3);camera.targetTexture=rt;
            foreach(bool reflected in new[]{false,true}) {
                effects.PlanarReflection=reflected;camera.Render();RenderTexture.active=rt;
                image.ReadPixels(new Rect(0,0,540,960),0,0);image.Apply();
                foreach(float x in new[]{-6f,-5f,-4f}) {
                    var uv=camera.WorldToViewportPoint(new Vector3(x,effects.WaterHeight,4));
                    Require(uv.x>0&&uv.x<1&&uv.y>0&&uv.y<1,"Depth sample outside frame.");
                    var value=image.GetPixel((int)(uv.x*540),(int)(uv.y*960));
                    Require(value.r>.01f&&value.r<.99f&&value.b>.4f&&value.b<.8f,"Invalid scene depth at "+x+": "+value);
                    samples.Add(new {reflected,x,rawDepth=value.r,bedY=value.g,waterDepth=value.b});
                }
            }
            return samples;
        } finally {
            camera.targetTexture=target;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(image);
            effects.Water.sharedMaterial.SetFloat("_StudyDebugView",0);effects.VolumetricFog=fog;effects.PlanarReflection=planar;data.renderPostProcessing=post;
        }
    }
    public static object Run()
    {
        Require(Application.isPlaying,"Enter Play in the recovered study.");
        var study=UnityEngine.Object.FindAnyObjectByType<AfkRecoveredStudy>();
        Require(study,"Wrong study scene.");
        var camera=study.View;var environment=study.Environment;
        var effects=camera.GetComponent<AfkStudyCameraEffects>();
        Require(effects&&effects.Water&&effects.FogMaterial,"Missing composed water/fog effects.");
        var character=study.Actor.GetComponentsInChildren<Renderer>().SelectMany(r=>r.sharedMaterials)
            .Where(m=>m&&m.shader.name=="InsectSpace/Local AFK Character").Distinct().ToArray();
        var saved=character.ToDictionary(m=>m,m=>new[]{"_USESDF","_USESUBSURFACE","_IndirectSpecular","_CharDepthRimState"}.ToDictionary(p=>p,p=>m.GetFloat(p)));
        float time=environment.TimeOfDay,aspect=camera.aspect,speed=study.Actor.speed,cloud=environment.CloudStrength;
        bool cycle=environment.Cycle,fog=effects.VolumetricFog,planar=effects.PlanarReflection;
        float reflectionStrength=effects.ReflectionStrength;
        var cloudSpeed=environment.CloudSpeed;var cloudOffset=environment.CloudOffset;
        var water=effects.Water.sharedMaterial;float depthColor=water.GetFloat("_StudyDepthColor");
        var changes=new Dictionary<string,int>();var captures=new List<string>();
        object reflectionProbe=null,cycleProbe=null,depthProbe=null,cloudProbe=null;
        GameObject marker=null;Material markerMaterial=null;
        Directory.CreateDirectory(Folder);
        try {
            environment.Cycle=false;study.Actor.speed=0;camera.aspect=9f/16;study.ApplyFraming();
            study.SetNight(false);
            var baseline=Frame(camera,"All-On");
            changes["RepeatFrameNoise"]=Difference(baseline,Frame(camera));
            effects.VolumetricFog=false;
            changes["VolumetricFog"]=Difference(baseline,Frame(camera,"Fog-Off"));effects.VolumetricFog=fog;
            effects.PlanarReflection=false;
            changes["PlanarReflection"]=Difference(baseline,Frame(camera,"Planar-Off"));effects.PlanarReflection=planar;
            water.SetFloat("_StudyDepthColor",0);
            changes["DepthWaterComposition"]=Difference(baseline,Frame(camera,"Water-Depth-Off"));water.SetFloat("_StudyDepthColor",depthColor);
            water.SetFloat("_StudyDebugView",1);Frame(camera,"Water-Depth-Diagnostic");
            water.SetFloat("_StudyDebugView",2);Frame(camera,"Water-Color-Diagnostic");water.SetFloat("_StudyDebugView",0);
            depthProbe=DepthProbe(study,effects);
            // Sparse original clouds can leave the shot clear. Exercise a full tile of phases.
            environment.CloudSpeed=Vector2.zero;
            var bestOffset=Vector2.zero;int bestChange=0;
            for(int y=0;y<4;y++)for(int x=0;x<4;x++) {
                environment.CloudOffset=new Vector2(x*.25f,y*.25f);environment.CloudStrength=cloud;environment.Apply();
                var cloudy=Frame(camera);environment.CloudStrength=0;environment.Apply();
                int changed=Difference(cloudy,Frame(camera));
                if(changed>bestChange) {bestChange=changed;bestOffset=environment.CloudOffset;}
            }
            environment.CloudOffset=bestOffset;environment.CloudStrength=cloud;environment.Apply();
            var covered=Frame(camera,"Cloud-On");environment.CloudStrength=0;environment.Apply();
            changes["CloudShadows"]=Difference(covered,Frame(camera,"Cloud-Off"));
            environment.CloudStrength=cloud;environment.CloudOffset=bestOffset+new Vector2(.5f,.5f);environment.Apply();
            changes["CloudPhase"]=Difference(covered,Frame(camera,"Cloud-Phase"));
            cloudProbe=new {testedPhases=16,offset=new[]{bestOffset.x,bestOffset.y},strength=cloud};
            environment.CloudSpeed=cloudSpeed;environment.CloudOffset=cloudOffset;environment.Apply();
            // A close view makes subtle skin and two-pixel silhouette terms measurable.
            float size=camera.orthographicSize;var position=camera.transform.position;
            camera.orthographicSize=2.5f;
            camera.transform.position=study.Actor.transform.position+Vector3.up-camera.transform.forward*48;
            var actorBaseline=Frame(camera,"Character-On",1080,1920);
            foreach(var property in new[]{"_USESDF","_USESUBSURFACE","_IndirectSpecular","_CharDepthRimState"}) {
                foreach(var material in character)material.SetFloat(property,0);
                changes[property]=Difference(actorBaseline,Frame(camera,"Character-"+property+"-Off",1080,1920),3);
                foreach(var material in character)material.SetFloat(property,saved[material][property]);
            }
            camera.orthographicSize=size;camera.transform.position=position;
            effects.VolumetricFog=false;effects.ReflectionStrength=1;
            marker=GameObject.CreatePrimitive(PrimitiveType.Cube);marker.name="TEMP reflection orientation probe";
            marker.transform.position=new Vector3(-6,effects.WaterHeight+.8f,4);
            marker.transform.localScale=Vector3.one*.65f;
            markerMaterial=new Material(Shader.Find("Universal Render Pipeline/Unlit"));markerMaterial.SetColor("_BaseColor",new Color(4,0,0,1));
            marker.GetComponent<Renderer>().sharedMaterial=markerMaterial;
            var marked=Frame(camera,"Reflection-Marker-On");
            var reflection=Read(effects.ReflectionTexture,"Planar-Texture");
            int colors=reflection.Select(c=>(c.r/8<<10)+(c.g/8<<5)+c.b/8).Distinct().Count();
            var mirrored=marker.transform.position;mirrored.y=2*effects.WaterHeight-mirrored.y;
            var expected=camera.WorldToViewportPoint(mirrored);
            marker.SetActive(false);var clean=Frame(camera,"Reflection-Marker-Off");
            int markedPixels=0;
            int cx=Mathf.RoundToInt(expected.x*540),cy=Mathf.RoundToInt(expected.y*960);
            for(int y=Mathf.Max(0,cy-14);y<Mathf.Min(960,cy+15);y++)for(int x=Mathf.Max(0,cx-14);x<Mathf.Min(540,cx+15);x++) {
                int i=y*540+x;
                if(marked[i].r-clean[i].r>12&&marked[i].r-marked[i].g>12)markedPixels++;
            }
            reflectionProbe=new {colors,expectedX=expected.x,expectedY=expected.y,markedPixels,reflectionFrames=effects.ReflectionFrames};
            Require(colors>100&&markedPixels>10,"Planar reflection texture/orientation probe failed: "+colors+" colors, "+markedPixels+" marker pixels.");
            UnityEngine.Object.DestroyImmediate(marker);marker=null;
            effects.ReflectionStrength=reflectionStrength;effects.VolumetricFog=fog;
            environment.TimeOfDay=.995f;environment.Advance(environment.CycleSeconds*.01f);
            float wrapped=environment.TimeOfDay;Require(Mathf.Abs(wrapped-.005f)<.00001f,"Cycle failed to wrap.");
            var localLight=new List<float>();
            foreach(float t in new[]{.18f,.25f,.32f}) {environment.TimeOfDay=t;environment.Apply();study.ApplyActorLight();localLight.Add(study.ActorLight.intensity);}
            Require(localLight[0]>localLight[1]&&localLight[1]>localLight[2],"Warm light does not fade continuously.");
            environment.TimeOfDay=.9999f;environment.Apply();var endColor=environment.Sun.color;float endIntensity=environment.Sun.intensity;
            environment.TimeOfDay=.0001f;environment.Apply();float colorJump=Vector4.Distance(endColor,environment.Sun.color),intensityJump=Mathf.Abs(endIntensity-environment.Sun.intensity);
            Require(colorJump<.02f&&intensityJump<.02f,"Environment has a midnight discontinuity.");
            cycleProbe=new {wrapped,localLight,colorJump,intensityJump,cycleSeconds=environment.CycleSeconds};
            foreach(var preset in new[]{new {name="Day",time=.5f},new {name="Night",time=.05f},new {name="Dawn",time=.25f},new {name="Dusk",time=.75f}}) {
                environment.TimeOfDay=preset.time;environment.Apply();study.ApplyActorLight();study.ApplyCharacterFacing();
                foreach(var resolution in new[]{new Vector2Int(1600,900),new Vector2Int(1080,1920)}) {
                    camera.aspect=(float)resolution.x/resolution.y;study.ApplyFraming();
                    string name=preset.name+(resolution.x>resolution.y?"-Desktop":"-Portrait");
                    var pixels=Frame(camera,name,resolution.x,resolution.y);
                    Require(pixels.Where((c,i)=>i%23==0).Select(c=>(c.r/8<<10)+(c.g/8<<5)+c.b/8).Distinct().Count()>100,"Blank frame "+name);
                    captures.Add(Path.Combine(Folder,name+".png"));
                }
            }
            File.WriteAllText(Path.Combine(Folder,"probe-measurements.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new {changes,depthProbe,cloudProbe,reflectionProbe,cycleProbe},Newtonsoft.Json.Formatting.Indented));
            foreach(var change in changes.Where(c=>c.Key!="RepeatFrameNoise"))Require(change.Value>10,"No measurable effect for "+change.Key+": "+change.Value);
        } finally {
            if(marker)UnityEngine.Object.DestroyImmediate(marker);
            if(markerMaterial)UnityEngine.Object.DestroyImmediate(markerMaterial);
            foreach(var material in saved)foreach(var property in material.Value)material.Key.SetFloat(property.Key,property.Value);
            effects.VolumetricFog=fog;effects.PlanarReflection=planar;effects.ReflectionStrength=reflectionStrength;
            water.SetFloat("_StudyDepthColor",depthColor);
            water.SetFloat("_StudyDebugView",0);
            environment.CloudStrength=cloud;environment.CloudSpeed=cloudSpeed;environment.CloudOffset=cloudOffset;
            environment.TimeOfDay=time;environment.Cycle=cycle;environment.Apply();
            study.Actor.speed=speed;study.ApplyActorLight();camera.aspect=aspect;study.ApplyFraming();
        }
        var report=new {unity=Application.unityVersion,changes,depthProbe,cloudProbe,reflectionProbe,cycleProbe,fogFrames=effects.FogFrames,captures};
        File.WriteAllText(Path.Combine(Folder,"advanced-probe.json"),Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));
        return report;
    }
}
