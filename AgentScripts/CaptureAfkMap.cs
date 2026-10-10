using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static class CaptureAfkMap
{
    public static object Run()
    {
        var camera=Camera.main;
        var position=camera.transform.position;var rotation=camera.transform.rotation;
        float size=camera.orthographicSize,aspect=camera.aspect;
        var previous=camera.targetTexture;var active=RenderTexture.active;
        var rt=RenderTexture.GetTemporary(320,568,24,RenderTextureFormat.ARGB32);
        var texture=new Texture2D(320,568,TextureFormat.RGB24,false);
        string configPath="D:/Project/InsectSpace/.artifacts/reference/afk-journey/capture-config.json";
        var config=JObject.Parse(File.ReadAllText(configPath));
        string folder=(string)config["folder"];
        Directory.CreateDirectory(folder);var paths=new List<string>();
        try {
            foreach (float yaw in config["yaw"])
            foreach (float z in config["z"])
            foreach (float x in config["x"]) {
                var q=Quaternion.Euler((float?)config["pitch"]??45,yaw,0);
                camera.transform.SetPositionAndRotation(new Vector3(x,(float?)config["height"]??.1f,z)-q*Vector3.forward*64,q);
                camera.orthographicSize=(float?)config["size"]??12;camera.aspect=320f/568;
                camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
                texture.ReadPixels(new Rect(0,0,320,568),0,0);texture.Apply();
                string path=Path.Combine(folder,yaw+"-"+x+"-"+z+".png");
                File.WriteAllBytes(path,texture.EncodeToPNG());paths.Add(path);
            }
        } finally {
            camera.transform.SetPositionAndRotation(position,rotation);camera.orthographicSize=size;
            camera.aspect=aspect;camera.targetTexture=previous;RenderTexture.active=active;
            RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(texture);
        }
        return new {captures=paths.Count,folder};
    }
}
