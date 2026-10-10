using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using InsectSpace.Rendering;

// Reversible source-basis and sun-angle comparisons in the connected Editor.
public static class CompareAfkSurface
{
    static void Capture(Camera camera, string path)
    {
        var target=RenderTexture.GetTemporary(540,960,24,RenderTextureFormat.ARGB32);
        var previous=camera.targetTexture;var active=RenderTexture.active;
        var image=new Texture2D(540,960,TextureFormat.RGB24,false);
        try {
            camera.targetTexture=target;camera.Render();RenderTexture.active=target;
            image.ReadPixels(new Rect(0,0,540,960),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());
        } finally {
            camera.targetTexture=previous;RenderTexture.active=active;
            RenderTexture.ReleaseTemporary(target);UnityEngine.Object.DestroyImmediate(image);
        }
    }
    public static object Run()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play first.");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var study=UnityEngine.Object.FindAnyObjectByType<AfkRecoveredStudy>();
        if(!study)throw new InvalidOperationException("Open the recovered lakeside study.");
        var renderers=study.GetComponentsInChildren<MeshRenderer>();
        var originals=renderers.Select(r=>r.sharedMaterials).ToArray();
        var copies=originals.SelectMany(ms=>ms).Where(m=>m&&m.HasProperty("_SourceTangentBasis")).Distinct().ToDictionary(m=>m,m=>new Material(m));
        float time=study.Environment.TimeOfDay,aspect=study.View.aspect;
        var offset=study.Environment.ShotRotationOffset;
        float dayYaw=study.Environment.ShotDayYawOffset;
        bool asyncCompilation=ShaderUtil.allowAsyncCompilation;
        string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../../.artifacts/validation/afk-recovered/2026-10-09/surface-comparison"));
        Directory.CreateDirectory(folder);var paths=new List<string>();var lights=new List<object>();
        try {
            ShaderUtil.allowAsyncCompilation=false;
            for(int i=0;i<renderers.Length;i++)renderers[i].sharedMaterials=originals[i].Select(m=>copies.TryGetValue(m,out var copy)?copy:m).ToArray();
            foreach(bool night in new[]{false,true}) {
                study.SetNight(night);study.View.aspect=9f/16;study.ApplyFraming();
                foreach(int basis in new[]{0,1}) {
                    foreach(var material in copies.Values)material.SetFloat("_SourceTangentBasis",basis);
                    string path=Path.Combine(folder,(night?"Night":"Day")+"-Basis"+basis+".png");
                    Capture(study.View,path);paths.Add(path);
                }
                lights.Add(new{night,rotation=study.Environment.Sun.transform.eulerAngles.ToString(),direction=(-study.Environment.Sun.transform.forward).ToString()});
            }
            study.SetNight(false);
            study.Environment.ShotDayYawOffset=0;
            foreach(int yaw in new[]{-80,-40,0,40,80,120}) {
                study.Environment.ShotRotationOffset=new Vector3(offset.x,yaw,offset.z);study.Environment.Apply();
                string path=Path.Combine(folder,"Day-Yaw"+yaw+".png");Capture(study.View,path);paths.Add(path);
            }
        } finally {
            for(int i=0;i<renderers.Length;i++)renderers[i].sharedMaterials=originals[i];
            foreach(var material in copies.Values)UnityEngine.Object.DestroyImmediate(material);
            study.Environment.TimeOfDay=time;study.Environment.ShotRotationOffset=offset;
            study.Environment.ShotDayYawOffset=dayYaw;
            study.Environment.Apply();study.ApplyActorLight();study.View.aspect=aspect;study.ApplyFraming();
            ShaderUtil.allowAsyncCompilation=asyncCompilation;
        }
        return new{folder,paths,lights};
    }
}
