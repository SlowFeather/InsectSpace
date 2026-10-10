using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using InsectSpace.Rendering;

public static class VerifyAfkStudy
{
    public static object Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Run the AFK study in Play mode.");
        var actor = UnityEngine.Object.FindAnyObjectByType<Animator>();
        var framing = UnityEngine.Object.FindAnyObjectByType<HeroStudyFraming>();
        var camera = Camera.main;
        if (!actor || !actor.name.StartsWith("AFK Faye")) throw new InvalidOperationException("Open AFKCloudStudy.");
        if (!actor.avatar || !actor.avatar.isValid || actor.applyRootMotion) throw new InvalidOperationException("Invalid rig.");
        var clip = actor.runtimeAnimatorController.animationClips.Single();
        if (!clip.isLooping) throw new InvalidOperationException("Idle must loop.");
        var skins = actor.GetComponentsInChildren<SkinnedMeshRenderer>()
            .Where(s => !s.sharedMaterials.All(m => m.GetColor("_BaseColor").a == 0)).ToArray();
        foreach (var skin in skins)
        {
            if (skin.bones.Any(b => !b)) throw new InvalidOperationException("Missing skin bone.");
            foreach (var material in skin.sharedMaterials)
                if (ShaderUtil.ShaderHasError(material.shader) || !material.GetTexture("_BaseMap"))
                    throw new InvalidOperationException("Invalid material: " + material.name);
        }
        var root = actor.transform.position;
        float aspect = camera.aspect, speed = actor.speed;
        var mode = framing.Mode;
        var state = actor.GetCurrentAnimatorStateInfo(0);
        var mesh = new Mesh();
        var samples = new List<object>();
        float deformation = 0;
        try
        {
            actor.speed = 0;
            foreach (var view in new[] { HeroStudyFraming.ViewMode.Hero, HeroStudyFraming.ViewMode.Explore })
                foreach (float ratio in new[] { 16f / 9f, 9f / 16f, 3f / 4f })
                {
                    camera.aspect = ratio; framing.SetMode(view);
                    var min = Vector2.one; var max = Vector2.zero;
                    foreach (float time in new[] { .1f, .5f, .9f })
                    {
                        actor.Play("OriginalIdle", 0, time); actor.Update(0);
                        foreach (var skin in skins)
                        {
                            skin.BakeMesh(mesh);
                            foreach (var p in mesh.vertices)
                            {
                                var v = camera.WorldToViewportPoint(skin.transform.TransformPoint(p));
                                min = Vector2.Min(min, new Vector2(v.x, v.y));
                                max = Vector2.Max(max, new Vector2(v.x, v.y));
                                if (v.z <= camera.nearClipPlane || v.x < .02f || v.x > .98f || v.y < .03f || v.y > .97f)
                                    throw new InvalidOperationException("Character clipped: " + view + " / " + ratio + " / " + time);
                            }
                        }
                    }
                    samples.Add(new { view = view.ToString(), aspect = ratio, min = min.ToString(), max = max.ToString() });
                }
            foreach (var skin in skins)
            {
                actor.Play("OriginalIdle", 0, .1f); actor.Update(0); skin.BakeMesh(mesh); var before = mesh.vertices;
                actor.Play("OriginalIdle", 0, .6f); actor.Update(0); skin.BakeMesh(mesh); var after = mesh.vertices;
                for (int i = 0; i < before.Length; i++) deformation = Mathf.Max(deformation, (after[i] - before[i]).sqrMagnitude);
            }
            if (deformation <= .0000001f || Vector3.Distance(root, actor.transform.position) > .0001f)
                throw new InvalidOperationException("Idle skin deformation or stationary root check failed.");
            actor.Play("OriginalIdle", 0, 1.15f); actor.Update(0);
            if (!actor.GetCurrentAnimatorStateInfo(0).loop) throw new InvalidOperationException("Idle did not loop.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(mesh);
            actor.Play(state.fullPathHash, 0, state.normalizedTime); actor.Update(0); actor.speed = speed;
            camera.aspect = aspect; framing.SetMode(mode);
        }
        return new { avatar = actor.avatar.isValid, clip = clip.name, seconds = clip.length,
                     renderers = skins.Length, maxDeformationSquared = deformation, stationaryRoot = true, samples };
    }
}
