using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

// A temporary, reversible inspection in the connected Editor; no scene is saved.
public static class InspectAfkCliffs
{
    const string Root="Assets/Temp/AFKStudy/Homestead";
    public static object Run()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before inspecting modules.");
        var camera=Camera.main;
        var world=GameObject.Find("LOCAL recovered assets - composed reference view");
        if(!world)throw new InvalidOperationException("Open the recovered lakeside study first.");
        var position=camera.transform.position;var rotation=camera.transform.rotation;
        float size=camera.orthographicSize,aspect=camera.aspect;
        var active=RenderTexture.active;var previous=camera.targetTexture;
        var rt=RenderTexture.GetTemporary(480,480,24,RenderTextureFormat.ARGB32);
        var texture=new Texture2D(480,480,TextureFormat.RGB24,false);
        var temporary=new GameObject("Temporary cliff inspection");
        string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../../.artifacts/reference/afk-journey/cliff-inspection"));
        Directory.CreateDirectory(folder);var results=new List<object>();
        bool wasActive=world.activeSelf;world.SetActive(false);
        try {
            foreach(string variant in new[]{"01_2","02_2","03_2","01_1","02_1","03_1"}) {
                string name="pbsc_cliff_homesteadbroadleaf_"+variant+"_hd";
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Generated/Prefabs/"+name+".prefab");
                if(!prefab)throw new InvalidOperationException("Missing "+name);
                var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab);go.transform.SetParent(temporary.transform,false);
                go.transform.position=Vector3.zero;go.SetActive(true);
                var renderers=go.GetComponentsInChildren<Renderer>().Where(r=>r.enabled).ToArray();
                var bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
                var q=Quaternion.Euler(40,0,0);camera.transform.SetPositionAndRotation(bounds.center-q*Vector3.forward*64,q);
                camera.orthographicSize=5;camera.aspect=1;camera.targetTexture=rt;
                for(int angle=0;angle<6;angle++) {
                    go.transform.rotation=Quaternion.Euler(0,angle*60,0);
                    camera.Render();RenderTexture.active=rt;
                    texture.ReadPixels(new Rect(0,0,480,480),0,0);texture.Apply();
                    File.WriteAllBytes(Path.Combine(folder,variant+"-"+angle*60+".png"),texture.EncodeToPNG());
                }
                results.Add(new{name,min=bounds.min.ToString("F4"),max=bounds.max.ToString("F4")});
                UnityEngine.Object.DestroyImmediate(go);
            }
        } finally {
            UnityEngine.Object.DestroyImmediate(temporary);world.SetActive(wasActive);
            camera.transform.SetPositionAndRotation(position,rotation);camera.orthographicSize=size;camera.aspect=aspect;
            camera.targetTexture=previous;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(texture);
        }
        File.WriteAllText(Path.Combine(folder,"bounds.json"),JArray.FromObject(results).ToString());
        return new{folder,modules=results};
    }
}
