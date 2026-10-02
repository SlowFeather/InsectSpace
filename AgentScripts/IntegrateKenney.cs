using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class IntegrateKenney
{
    private const string ModelPath = "Assets/InsectSpace/Rendering/Showcase/KenneyAnimatedCharacters2/Model/characterMedium.fbx";
    private const string IdlePath = "Assets/InsectSpace/Rendering/Showcase/KenneyAnimatedCharacters2/Animations/idle.fbx";
    private const string TexturePath = "Assets/InsectSpace/Rendering/Showcase/KenneyAnimatedCharacters2/Skins/skaterMaleA.png";
    private const string ControllerPath = "Assets/InsectSpace/Rendering/Showcase/KenneyAnimatedCharacters2/KenneyIdle.controller";

    public static string Run()
    {
        var old = GameObject.Find("Showcase Character - Verdant Adept");
        if (old != null) Object.DestroyImmediate(old);
        var previous = GameObject.Find("Kenney Character - CC0 Rigged Showcase");
        if (previous != null) Object.DestroyImmediate(previous);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (prefab == null) return "Model asset did not import: " + ModelPath;
        var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, SceneManager.GetActiveScene());
        root.name = "Kenney Character - CC0 Rigged Showcase";
        root.transform.SetPositionAndRotation(new Vector3(0f, 0.04f, 0.3f), Quaternion.Euler(0f, 180f, 0f));
        // Kenney's FBX is authored in a larger unit scale; keep the character
        // readable in the same small meadow framing as the world actors.
        root.transform.localScale = Vector3.one * 0.85f;

        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/InsectSpace/Rendering/Showcase/KenneyAnimatedCharacters2/KenneySkin.mat");
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { name = "Kenney CC0 Skin - Skater Male" };
            material.SetColor("_BaseColor", Color.white);
            material.SetColor("_Color", Color.white);
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            AssetDatabase.CreateAsset(material, "Assets/InsectSpace/Rendering/Showcase/KenneyAnimatedCharacters2/KenneySkin.mat");
        }
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            var slots = renderer.sharedMaterials;
            for (int i = 0; i < slots.Length; i++) slots[i] = material;
            renderer.sharedMaterials = slots;
        }

        var idleAssets = AssetDatabase.LoadAllAssetsAtPath(IdlePath);
        AnimationClip idle = null;
        foreach (var asset in idleAssets)
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__")) { idle = clip; break; }
        if (idle != null)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var stateMachine = controller.layers[0].stateMachine;
            AnimatorState state = null;
            foreach (var candidate in stateMachine.states) if (candidate.state.name == "Idle") state = candidate.state;
            if (state == null) state = stateMachine.AddState("Idle");
            state.motion = idle;
            stateMachine.defaultState = state;
            var animator = root.GetComponent<Animator>();
            if (animator == null) animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            // The source idle FBX contains an extreme root translation curve when sampled by Unity 6.
            // Keep the CC0 rig and controller asset available, but use the stable presentation motion
            // until a retargeted clip is supplied; this keeps the character in the meadow at runtime.
            animator.enabled = false;
        }
        if (root.GetComponent<InsectSpace.Rendering.ShowcaseMotion>() == null) root.AddComponent<InsectSpace.Rendering.ShowcaseMotion>();
        var bounds = new Bounds(root.transform.position, Vector3.zero);
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) bounds.Encapsulate(renderer.bounds);
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Assets/InsectSpace/Scenes/MeadowShowcase.unity");
        AssetDatabase.SaveAssets();
        return "Integrated Kenney CC0 rigged character. Renderers=" + root.GetComponentsInChildren<Renderer>(true).Length + " bounds=" + bounds.size + " idle=" + (idle != null);
    }
}
