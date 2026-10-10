using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using InsectSpace.Rendering;

// Captures reversible material comparisons without saving scenes or assets.
public static class CompareAfkRecovered
{
    const string Root = "Assets/Temp/AFKStudy/Homestead";
    static void Capture(Camera camera, string path, int width=540, int height=960)
    {
        var target=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32);
        var old=camera.targetTexture;var active=RenderTexture.active;
        bool asyncCompilation=UnityEditor.ShaderUtil.allowAsyncCompilation;
        var image=new Texture2D(width,height,TextureFormat.RGB24,false);
        try {
            UnityEditor.ShaderUtil.allowAsyncCompilation=false;
            camera.targetTexture=target;camera.Render();RenderTexture.active=target;
            image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());
        } finally {UnityEditor.ShaderUtil.allowAsyncCompilation=asyncCompilation;camera.targetTexture=old;RenderTexture.active=active;RenderTexture.ReleaseTemporary(target);UnityEngine.Object.DestroyImmediate(image);}
    }
    public static object ComparePlateau()
    {
        var study=UnityEngine.Object.FindAnyObjectByType<AfkRecoveredStudy>();
        if(!study)throw new InvalidOperationException("Open the recovered study first.");
        var renderers=study.GetComponentsInChildren<MeshRenderer>();
        var originals=renderers.Select(r=>r.sharedMaterials).ToArray();
        var materials=originals.SelectMany(ms=>ms).Where(m=>m&&m.HasProperty("_Kind")).Distinct().ToArray();
        var cliff=materials.First(m=>m.GetFloat("_Kind")==2);
        var plateau=materials.Single(m=>m.GetFloat("_Kind")==1&&m.name!="Recovered VT meadow");
        var copy=new Material(plateau);
        float time=study.Environment.TimeOfDay,aspect=study.View.aspect,speed=study.Actor.speed;
        bool cycle=study.Environment.Cycle;
        string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../../.artifacts/validation/afk-recovered/2026-10-09/advanced/plateau"));
        Directory.CreateDirectory(folder);var paths=new List<string>();
        try {
            for(int i=0;i<renderers.Length;i++)renderers[i].sharedMaterials=originals[i].Select(m=>m==plateau?copy:m).ToArray();
            study.Actor.speed=0;
            foreach(bool night in new[]{false,true}) {
                study.SetNight(night);study.View.aspect=16f/9;study.ApplyFraming();
                foreach(bool matched in new[]{false,true}) {
                    copy.CopyPropertiesFromMaterial(plateau);
                    if(matched) {
                        foreach(string p in new[]{"_AOIntensity","_DarkThreshold","_ShadowThreshold","_ShadowFeather","_ShadowIntensity","_InShadowIntensity","_StudyLocalLightWeight"})copy.SetFloat(p,cliff.GetFloat(p));
                        foreach(string p in new[]{"_DarkTint","_MidTint","_StudyNightTint","_StudyNightTopTint","_StudyNightShadowTint"})copy.SetColor(p,cliff.GetColor(p));
                        copy.SetColor("_StudyDayTint",(cliff.GetColor("_StudyDayTint").linear*cliff.GetColor("_StudyDayTopTint").linear).gamma);
                        copy.SetColor("_StudyDayTopTint",Color.white);
                        copy.SetFloat("_StudyNightExposure",cliff.GetFloat("_StudyNightExposure")*cliff.GetFloat("_StudyNightTopExposure"));
                        copy.SetFloat("_StudyNightTopExposure",1);
                        copy.SetFloat("_StudyNightTopSaturation",cliff.GetFloat("_StudyNightTopSaturation"));
                    }
                    string path=Path.Combine(folder,(night?"Night":"Day")+(matched?"-Matched":"-Baseline")+".png");
                    Capture(study.View,path,1600,900);paths.Add(path);
                }
            }
        } finally {
            for(int i=0;i<renderers.Length;i++)renderers[i].sharedMaterials=originals[i];
            UnityEngine.Object.DestroyImmediate(copy);
            study.Environment.TimeOfDay=time;study.Environment.Cycle=cycle;study.Environment.Apply();study.ApplyActorLight();
            study.Actor.speed=speed;study.View.aspect=aspect;study.ApplyFraming();
        }
        return new{folder,paths};
    }
    public static object CompareShoreGrass()
    {
        var study=UnityEngine.Object.FindAnyObjectByType<AfkRecoveredStudy>();
        if(!study)throw new InvalidOperationException("Open the recovered study first.");
        var renderers=study.GetComponentsInChildren<MeshRenderer>();
        var originals=renderers.Select(r=>r.sharedMaterials).ToArray();
        var copies=new Dictionary<Material,Material>();
        foreach(var m in originals.SelectMany(ms=>ms).Distinct())
            if(m&&(m.HasProperty("_FirstLayerWidth")||m.HasProperty("_Kind")&&m.GetFloat("_Kind")==4))copies[m]=new Material(m);
        float time=study.Environment.TimeOfDay,aspect=study.View.aspect;
        string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../../.artifacts/validation/afk-recovered/2026-10-09/shore-grass"));
        Directory.CreateDirectory(folder);var paths=new List<string>();
        try {
            for(int i=0;i<renderers.Length;i++)renderers[i].sharedMaterials=originals[i].Select(m=>copies.TryGetValue(m,out var copy)?copy:m).ToArray();
            foreach(bool night in new[]{false,true}) {
                study.SetNight(night);study.View.aspect=9f/16;study.ApplyFraming();
                foreach(string variant in new[]{"Calibrated","OriginalShore","SourceGrass","LowGrassLight"}) {
                    foreach(var pair in copies) {
                        var m=pair.Value;m.CopyPropertiesFromMaterial(pair.Key);
                        if(variant=="OriginalShore"&&m.HasProperty("_FirstLayerWidth")) {
                            m.SetFloat("_FirstLayerWidth",.204f);m.SetFloat("_StudyShoreStrength",1);
                        }
                        if(variant=="SourceGrass"&&m.HasProperty("_Kind")&&m.GetFloat("_Kind")==4&&m.GetFloat("_TYPE_FLOWER")<.5f&&m.GetFloat("_TYPE_SLIVER")<.5f)
                            m.SetFloat("_GrassHeight",m.GetFloat("_GrassHeight")/.8f);
                        if(variant=="LowGrassLight"&&m.HasProperty("_Kind")&&m.GetFloat("_Kind")==4&&m.GetFloat("_TYPE_FLOWER")<.5f&&m.GetFloat("_TYPE_SLIVER")<.5f)
                            m.SetFloat("_StudyLocalLightWeight",.4f);
                    }
                    string path=Path.Combine(folder,(night?"Night":"Day")+"-"+variant+".png");Capture(study.View,path);paths.Add(path);
                }
            }
        } finally {
            for(int i=0;i<renderers.Length;i++)renderers[i].sharedMaterials=originals[i];
            foreach(var m in copies.Values)UnityEngine.Object.DestroyImmediate(m);
            study.Environment.TimeOfDay=time;study.Environment.Apply();study.ApplyActorLight();
            study.View.aspect=aspect;study.ApplyFraming();
        }
        return new{folder,paths};
    }
    public static object Run()
    {
        var study=UnityEngine.Object.FindAnyObjectByType<AfkRecoveredStudy>();
        if(!study)throw new InvalidOperationException("Open the recovered study first.");
        var renderers=study.GetComponentsInChildren<MeshRenderer>();
        var originals=renderers.Select(r=>r.sharedMaterials).ToArray();
        var blocks=renderers.Select(r=>{var block=new MaterialPropertyBlock();r.GetPropertyBlock(block);return block;}).ToArray();
        var copies=new Dictionary<Material,Material>();
        foreach(var m in originals.SelectMany(ms=>ms).Distinct())
            if(m&&m.HasProperty("_Kind"))copies[m]=new Material(m);
        float time=study.Environment.TimeOfDay,aspect=study.View.aspect;
        float actorIntensity=study.ActorLight.intensity;
        Color actorColor=study.ActorLight.color;
        var rotationOffset=study.Environment.ShotRotationOffset;
        string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../../.artifacts/validation/afk-recovered/2026-10-09/source-comparison"));
        Directory.CreateDirectory(folder);var paths=new List<string>();
        var small=UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Source/VT/homestead_01/small_diffuse-world.png");
        try {
            for(int i=0;i<renderers.Length;i++)renderers[i].sharedMaterials=originals[i].Select(m=>copies.TryGetValue(m,out var copy)?copy:m).ToArray();
            foreach(bool night in new[]{false,true}) {
                study.SetNight(night);study.View.aspect=9f/16;study.ApplyFraming();
                foreach(string variant in new[]{"Baseline","CalibratedCaps","MutedCaps","GreenCaps","AmberPool","GreenCapsAmberPool","SourceDirection","SourceCaps","SourceCliffLight","SourceAllLight"}) {
                    study.ActorLight.intensity=variant=="MutedCaps"?22:actorIntensity;
                    study.ActorLight.color=variant.Contains("AmberPool")?new Color(1,.92f,.2f):actorColor;
                    study.Environment.ShotRotationOffset=variant=="SourceDirection"?Vector3.zero:rotationOffset;
                    study.Environment.Apply();
                    foreach(var pair in copies) {
                        var m=pair.Value;m.CopyPropertiesFromMaterial(pair.Key);
                        float kind=m.GetFloat("_Kind");
                        if((variant=="CalibratedCaps"||variant=="SourceDirection")&&kind==2) {
                            m.SetFloat("_StudyNightTopExposure",.45f);m.SetFloat("_StudyNightTopSaturation",1);
                            m.SetColor("_StudyNightTopTint",Color.white);
                        }
                        if(variant=="MutedCaps"&&kind==2) {
                            m.SetFloat("_StudyNightExposure",.8f);m.SetFloat("_StudyNightTopExposure",.4f);
                            m.SetFloat("_StudyNightTopSaturation",.6f);m.SetColor("_StudyNightTopTint",Color.white);
                        }
                        if(variant.StartsWith("GreenCaps")&&(kind==2||kind==1&&pair.Key.name!="Recovered VT meadow")) {
                            m.SetColor("_StudyNightTopTint",new Color(.9f,1.05f,.9f));
                        }
                        if(variant.StartsWith("Source")&&variant!="SourceDirection"&&kind==2) {
                            m.SetFloat("_StudyNightTopExposure",1);m.SetFloat("_StudyNightTopSaturation",1);
                            m.SetColor("_StudyNightTopTint",Color.white);
                            m.SetTexture("_WorldMap",small);m.SetFloat("_TerrainDetailStrength",0);
                        }
                        if(variant=="SourceCliffLight"&&kind==2||variant=="SourceAllLight")m.SetFloat("_UseSourceZoneLight",1);
                    }
                    for(int i=0;i<renderers.Length;i++) {
                        var block=blocks[i];
                        if(variant.StartsWith("Source")&&variant!="SourceDirection"&&renderers[i].transform.parent.name.StartsWith("pbsc_cliff_")) {
                            block=new MaterialPropertyBlock();
                            float x=renderers[i].transform.parent.position.y>5?197:209;
                            block.SetVector("_WorldMapRect",new Vector4(-x,-95,408,544));
                        }
                        // An empty override changes the batching path even when it has no values.
                        renderers[i].SetPropertyBlock(block.isEmpty?null:block);
                    }
                    string path=Path.Combine(folder,(night?"Night":"Day")+"-"+variant+".png");Capture(study.View,path);paths.Add(path);
                }
            }
        } finally {
            for(int i=0;i<renderers.Length;i++){renderers[i].sharedMaterials=originals[i];renderers[i].SetPropertyBlock(blocks[i].isEmpty?null:blocks[i]);}
            foreach(var m in copies.Values)UnityEngine.Object.DestroyImmediate(m);
            study.Environment.TimeOfDay=time;study.Environment.ShotRotationOffset=rotationOffset;study.Environment.Apply();study.ApplyActorLight();
            study.ActorLight.intensity=actorIntensity;
            study.ActorLight.color=actorColor;
            study.View.aspect=aspect;study.ApplyFraming();
        }
        return new{folder,paths};
    }
}
