using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using InsectSpace.Rendering;

public static class CompareAfkLighting
{
    static void Capture(Camera camera, string path)
    {
        var target = RenderTexture.GetTemporary(540, 960, 24, RenderTextureFormat.ARGB32);
        var previous = camera.targetTexture; var active = RenderTexture.active; float aspect = camera.aspect;
        var texture = new Texture2D(540, 960, TextureFormat.RGB24, false);
        try {
            camera.aspect = 9f / 16; camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 540, 960), 0, 0); texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
        } finally {
            camera.targetTexture = previous; camera.aspect = aspect; RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(target); UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    public static object Run()
    {
        var study = UnityEngine.Object.FindAnyObjectByType<AfkRecoveredStudy>();
        if (!study) throw new InvalidOperationException("Open the recovered study.");
        var env = study.Environment; float time = env.TimeOfDay;
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../../.artifacts/validation/afk-recovered/2026-10-08/color-space"));
        Directory.CreateDirectory(folder); var paths = new List<string>();
        try {
            foreach (bool night in new[] { false, true }) {
                study.SetNight(night); study.View.aspect = 9f / 16; study.ApplyFraming();
                string prefix = night ? "Night" : "Day";
                string path = Path.Combine(folder, prefix + "-Baseline.png"); Capture(study.View, path); paths.Add(path);
                var ambient = env.AmbientColor.Evaluate(env.TimeOfDay); ambient.a *= env.ShotAmbientStrength;
                Shader.SetGlobalColor("_StudyAmbient", ambient);
                path = Path.Combine(folder, prefix + "-RawAmbient.png"); Capture(study.View, path); paths.Add(path);
                ambient.a = env.AmbientColor.Evaluate(env.TimeOfDay).a;
                Shader.SetGlobalColor("_StudyAmbient", ambient);
                path = Path.Combine(folder, prefix + "-FullAmbient.png"); Capture(study.View, path); paths.Add(path);
                // Inverse URP conversion tests the original custom zone-light values.
                env.Sun.color = env.LightColor.Evaluate(env.TimeOfDay).gamma;
                Shader.SetGlobalColor("_StudyWaterLight", env.WaterReflectLight.Evaluate(env.TimeOfDay));
                Shader.SetGlobalColor("_StudyWaterDark", env.WaterReflectDark.Evaluate(env.TimeOfDay));
                Shader.SetGlobalColor("_StudyWaterCloud", env.WaterReflectCloud.Evaluate(env.TimeOfDay));
                path = Path.Combine(folder, prefix + "-RawZone.png"); Capture(study.View, path); paths.Add(path);
            }
        } finally { env.TimeOfDay = time; env.Apply(); study.ApplyFraming(); }
        return new { folder, paths };
    }
}
