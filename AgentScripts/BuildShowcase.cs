using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class BuildShowcase
{
    private const string ScenePath = "Assets/InsectSpace/Scenes/MeadowShowcase.unity";
    private const string MaterialRoot = "Assets/InsectSpace/Rendering/Showcase";
    private static readonly Color Grass = new Color(0.18f, 0.42f, 0.20f);
    private static readonly Color GrassLight = new Color(0.34f, 0.62f, 0.25f);
    private static readonly Color GrassDark = new Color(0.08f, 0.24f, 0.13f);

    public static string Run()
    {
        EnsureFolder("Assets/InsectSpace/Scenes");
        EnsureFolder("Assets/InsectSpace/Rendering");
        EnsureFolder(MaterialRoot);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "MeadowShowcase";
        ConfigureLighting();
        var materials = CreateMaterials();

        CreateGround(materials["ground"], materials["path"]);
        CreateGrassField(materials["grass"], materials["grassLight"], materials["grassDark"]);
        CreateScenery(materials);
        var character = CreateCharacter(materials);
        CreateCamera(character.transform);
        CreateLightingRig();
        CreatePostProcessing();

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return "Created " + ScenePath + " with a stylized character meadow showcase.";
    }

    private static void ConfigureLighting()
    {
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.34f, 0.43f, 0.52f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.60f, 0.72f, 0.78f);
        RenderSettings.fogDensity = 0.012f;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.reflectionIntensity = 0.45f;
        RenderSettings.skybox = null;
    }

    private static Dictionary<string, Material> CreateMaterials()
    {
        var map = new Dictionary<string, Material>();
        map["ground"] = MakeMaterial("Meadow Ground", new Color(0.15f, 0.36f, 0.16f), 0f, 0.1f);
        map["ground2"] = MakeMaterial("Meadow Ground Light", new Color(0.24f, 0.48f, 0.19f), 0f, 0.05f);
        map["path"] = MakeMaterial("Warm Stone Path", new Color(0.53f, 0.42f, 0.27f), 0f, 0.22f);
        map["grass"] = MakeMaterial("Grass", Grass, 0f, 0f);
        map["grassLight"] = MakeMaterial("Grass Light", GrassLight, 0f, 0f);
        map["grassDark"] = MakeMaterial("Grass Dark", GrassDark, 0f, 0f);
        map["rock"] = MakeMaterial("River Rock", new Color(0.28f, 0.34f, 0.34f), 0f, 0.32f);
        map["rockLight"] = MakeMaterial("River Rock Light", new Color(0.43f, 0.47f, 0.43f), 0f, 0.4f);
        map["wood"] = MakeMaterial("Branch", new Color(0.26f, 0.13f, 0.07f), 0f, 0.2f);
        map["leaf"] = MakeMaterial("Leaf", new Color(0.12f, 0.34f, 0.15f), 0f, 0.08f);
        map["skin"] = MakeMaterial("Character Skin", new Color(0.93f, 0.60f, 0.40f), 0f, 0.28f);
        map["cloth"] = MakeMaterial("Character Teal Cloth", new Color(0.06f, 0.29f, 0.34f), 0f, 0.3f);
        map["clothLight"] = MakeMaterial("Character Sash", new Color(0.86f, 0.48f, 0.16f), 0f, 0.25f);
        map["clothDark"] = MakeMaterial("Character Trim", new Color(0.03f, 0.10f, 0.14f), 0f, 0.32f);
        map["hair"] = MakeMaterial("Character Hair", new Color(0.045f, 0.035f, 0.03f), 0f, 0.16f);
        map["eye"] = MakeMaterial("Character Eyes", new Color(0.01f, 0.012f, 0.01f), 0f, 0.55f);
        map["gold"] = MakeMaterial("Brass Detail", new Color(0.93f, 0.58f, 0.15f), 0.4f, 0.3f);
        map["orb"] = MakeMaterial("Spirit Orb", new Color(0.22f, 0.9f, 0.83f), 0.1f, 0.1f, new Color(0.03f, 0.34f, 0.30f));
        map["flowerPink"] = MakeMaterial("Flower Pink", new Color(0.96f, 0.30f, 0.41f), 0f, 0.12f);
        map["flowerYellow"] = MakeMaterial("Flower Yellow", new Color(1.0f, 0.70f, 0.18f), 0f, 0.12f);
        map["flowerWhite"] = MakeMaterial("Flower White", new Color(0.96f, 0.93f, 0.76f), 0f, 0.12f);
        foreach (var pair in map) SaveMaterial(pair.Value, pair.Key);
        return map;
    }

    private static Material MakeMaterial(string name, Color color, float metallic, float smoothness, Color? emission = null)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var material = new Material(shader) { name = name };
        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        if (emission.HasValue && material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission.Value);
        }
        return material;
    }

    private static void SaveMaterial(Material material, string fileName)
    {
        var path = MaterialRoot + "/" + fileName + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            EditorUtility.CopySerialized(material, existing);
            UnityEngine.Object.DestroyImmediate(material);
        }
        else AssetDatabase.CreateAsset(material, path);
    }

    private static void CreateGround(Material ground, Material path)
    {
        var meadow = GameObject.CreatePrimitive(PrimitiveType.Plane);
        meadow.name = "Meadow - playable presentation ground";
        meadow.transform.localScale = new Vector3(4.8f, 1f, 4.8f);
        meadow.GetComponent<Renderer>().sharedMaterial = ground;
        UnityEngine.Object.DestroyImmediate(meadow.GetComponent<Collider>());

        var pathObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pathObject.name = "Curving stone path";
        pathObject.transform.SetPositionAndRotation(new Vector3(0f, 0.028f, -2.65f), Quaternion.Euler(0f, -8f, 0f));
        pathObject.transform.localScale = new Vector3(8.7f, 0.055f, 1.02f);
        pathObject.GetComponent<Renderer>().sharedMaterial = path;
        UnityEngine.Object.DestroyImmediate(pathObject.GetComponent<Collider>());

        for (int i = 0; i < 5; i++)
        {
            var stone = CreatePrimitive(PrimitiveType.Cube, "Path stone " + i, path, new Vector3(-3.1f + i * 1.52f, 0.085f, -2.15f + Mathf.Sin(i * 1.7f) * 0.15f), new Vector3(1.15f, 0.08f, 0.52f));
            stone.transform.rotation = Quaternion.Euler(0f, -8f + i * 4f, i % 2 == 0 ? 2f : -2f);
        }
    }

    private static void CreateGrassField(Material grass, Material grassLight, Material grassDark)
    {
        var root = new GameObject("Grass meadow detail");
        var mesh = new Mesh { name = "Procedural grass clumps" };
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        var colors = new List<Color>();
        var random = new System.Random(42017);
        for (int i = 0; i < 265; i++)
        {
            float x = (float)(random.NextDouble() * 8.8 - 4.4);
            float z = (float)(random.NextDouble() * 7.1 - 1.3);
            if (Mathf.Abs(z + 2.65f) < 0.72f) continue;
            float h = 0.16f + (float)random.NextDouble() * 0.28f;
            float w = 0.07f + (float)random.NextDouble() * 0.07f;
            Color tint = i % 5 == 0 ? GrassLight : (i % 7 == 0 ? GrassDark : Grass);
            AddBlade(vertices, triangles, colors, new Vector3(x, 0.04f, z), h, w, tint, (float)random.NextDouble() * 6.28f);
        }
        mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.SetColors(colors); mesh.RecalculateNormals();
        var filter = root.AddComponent<MeshFilter>(); filter.sharedMesh = mesh;
        var renderer = root.AddComponent<MeshRenderer>(); renderer.sharedMaterial = grass;
    }

    private static void AddBlade(List<Vector3> vertices, List<int> triangles, List<Color> colors, Vector3 basePoint, float height, float width, Color color, float angle)
    {
        Vector3 side = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * width;
        Vector3 forward = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * width;
        int start = vertices.Count;
        vertices.Add(basePoint - side); vertices.Add(basePoint + side); vertices.Add(basePoint + Vector3.up * height + forward * 0.18f);
        vertices.Add(basePoint - forward); vertices.Add(basePoint + forward); vertices.Add(basePoint + Vector3.up * (height * 0.93f) - side * 0.18f);
        triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
        triangles.Add(start + 3); triangles.Add(start + 4); triangles.Add(start + 5);
        for (int i = 0; i < 6; i++) colors.Add(color);
    }

    private static void CreateScenery(Dictionary<string, Material> m)
    {
        var scenery = new GameObject("Meadow scenery");
        CreateBush(scenery.transform, m["leaf"], new Vector3(-3.8f, 0.1f, 1.9f), 1.0f);
        CreateBush(scenery.transform, m["leaf"], new Vector3(3.7f, 0.1f, 2.6f), 1.2f);
        CreateBush(scenery.transform, m["leaf"], new Vector3(3.8f, 0.1f, -0.6f), 0.72f);
        for (int i = 0; i < 15; i++)
        {
            float t = i / 14f;
            float x = -4.0f + t * 8.0f;
            float z = 2.8f + Mathf.Sin(t * 7f) * 0.35f;
            CreateFlower(scenery.transform, m[i % 2 == 0 ? "flowerPink" : "flowerYellow"], new Vector3(x, 0.06f, z), 0.75f + (i % 3) * 0.16f);
        }
        for (int i = 0; i < 11; i++)
        {
            float x = -4.2f + (i * 0.83f) % 7.9f;
            float z = -0.8f + (i * 1.37f) % 3.1f;
            CreateRock(scenery.transform, m[i % 3 == 0 ? "rockLight" : "rock"], new Vector3(x, 0.12f, z), 0.22f + (i % 4) * 0.06f);
        }
        CreateTree(scenery.transform, m["wood"], m["leaf"], new Vector3(-4.0f, 0f, 3.0f));
        CreateTree(scenery.transform, m["wood"], m["leaf"], new Vector3(4.15f, 0f, 3.35f), 1.15f);
    }

    private static void CreateBush(Transform parent, Material leaf, Vector3 position, float scale)
    {
        var root = new GameObject("Rounded bush"); root.transform.SetParent(parent); root.transform.position = position;
        for (int i = 0; i < 5; i++)
        {
            var puff = CreatePrimitive(PrimitiveType.Sphere, "Bush leaf", leaf, new Vector3((i - 2) * 0.34f, 0.35f + (i % 2) * 0.18f, (i % 2) * 0.22f), Vector3.one * (0.62f + (i % 3) * 0.12f));
            puff.transform.SetParent(root.transform, true); puff.transform.localScale *= scale;
        }
    }

    private static void CreateFlower(Transform parent, Material flower, Vector3 position, float scale)
    {
        var root = new GameObject("Wildflower"); root.transform.SetParent(parent); root.transform.position = position;
        var stem = CreatePrimitive(PrimitiveType.Cylinder, "Flower stem", parent.GetComponentInParent<Transform>() == null ? flower : flower, position + Vector3.up * 0.17f, new Vector3(0.018f, 0.17f, 0.018f));
        stem.GetComponent<Renderer>().sharedMaterial = parent == null ? flower : flower;
        var center = CreatePrimitive(PrimitiveType.Sphere, "Flower center", flower, position + Vector3.up * 0.38f, Vector3.one * (0.08f * scale));
        center.transform.SetParent(root.transform, true);
        for (int i = 0; i < 4; i++)
        {
            var petal = CreatePrimitive(PrimitiveType.Sphere, "Flower petal", flower, position + Vector3.up * 0.38f + new Vector3(Mathf.Cos(i * 1.57f), 0f, Mathf.Sin(i * 1.57f)) * 0.105f * scale, new Vector3(0.10f, 0.045f, 0.16f) * scale);
            petal.transform.SetParent(root.transform, true);
        }
    }

    private static void CreateRock(Transform parent, Material material, Vector3 position, float scale)
    {
        var rock = CreatePrimitive(PrimitiveType.Sphere, "Low poly rock", material, position, new Vector3(scale * 1.3f, scale * 0.65f, scale));
        rock.transform.SetParent(parent, true);
        rock.transform.rotation = Quaternion.Euler(0f, position.x * 19f, position.z * 13f);
    }

    private static void CreateTree(Transform parent, Material wood, Material leaf, Vector3 position, float scale = 1f)
    {
        var root = new GameObject("Meadow tree"); root.transform.SetParent(parent); root.transform.position = position; root.transform.localScale = Vector3.one * scale;
        var trunk = CreatePrimitive(PrimitiveType.Cylinder, "Tree trunk", wood, new Vector3(0f, 1.2f, 0f), new Vector3(0.22f, 1.2f, 0.22f)); trunk.transform.SetParent(root.transform, true);
        for (int i = 0; i < 5; i++)
        {
            var crown = CreatePrimitive(PrimitiveType.Sphere, "Tree crown", leaf, new Vector3((i - 2) * 0.42f, 2.55f + (i % 2) * 0.26f, (i % 2) * 0.3f), Vector3.one * (0.78f + (i % 3) * 0.1f));
            crown.transform.SetParent(root.transform, true);
        }
    }

    private static GameObject CreateCharacter(Dictionary<string, Material> m)
    {
        var root = new GameObject("Showcase Character - Verdant Adept"); root.transform.position = new Vector3(0f, 0.04f, 0.3f);
        root.AddComponent<InsectSpace.Rendering.ShowcaseMotion>();
        var shadow = CreatePrimitive(PrimitiveType.Cylinder, "Soft character shadow", m["clothDark"], new Vector3(0f, 0.035f, 0f), new Vector3(0.72f, 0.018f, 0.48f)); shadow.transform.SetParent(root.transform, true);
        var body = CreatePrimitive(PrimitiveType.Capsule, "Character body", m["cloth"], new Vector3(0f, 1.25f, 0f), new Vector3(0.72f, 0.95f, 0.52f)); body.transform.SetParent(root.transform, true);
        var sash = CreatePrimitive(PrimitiveType.Cylinder, "Orange sash", m["clothLight"], new Vector3(0f, 1.35f, 0f), new Vector3(0.50f, 0.09f, 0.50f)); sash.transform.SetParent(root.transform, true);
        var head = CreatePrimitive(PrimitiveType.Sphere, "Character head", m["skin"], new Vector3(0f, 2.45f, 0f), new Vector3(0.48f, 0.55f, 0.43f)); head.transform.SetParent(root.transform, true);
        var hair = CreatePrimitive(PrimitiveType.Sphere, "Character hair", m["hair"], new Vector3(0f, 2.76f, -0.02f), new Vector3(0.48f, 0.27f, 0.44f)); hair.transform.SetParent(root.transform, true);
        var hat = CreatePrimitive(PrimitiveType.Cylinder, "Traveler hat", m["clothDark"], new Vector3(0f, 2.98f, 0f), new Vector3(0.72f, 0.08f, 0.72f)); hat.transform.SetParent(root.transform, true);
        var hatTop = CreateCone("Traveler hat crown", m["cloth"], new Vector3(0f, 3.35f, 0f), 0.49f, 0.78f, 8); hatTop.transform.SetParent(root.transform, true);
        var eyeL = CreatePrimitive(PrimitiveType.Sphere, "Eye L", m["eye"], new Vector3(-0.17f, 2.49f, -0.40f), Vector3.one * 0.065f); eyeL.transform.SetParent(root.transform, true);
        var eyeR = CreatePrimitive(PrimitiveType.Sphere, "Eye R", m["eye"], new Vector3(0.17f, 2.49f, -0.40f), Vector3.one * 0.065f); eyeR.transform.SetParent(root.transform, true);
        var legL = CreatePrimitive(PrimitiveType.Capsule, "Boot L", m["clothDark"], new Vector3(-0.23f, 0.55f, 0f), new Vector3(0.24f, 0.52f, 0.26f)); legL.transform.SetParent(root.transform, true);
        var legR = CreatePrimitive(PrimitiveType.Capsule, "Boot R", m["clothDark"], new Vector3(0.23f, 0.55f, 0f), new Vector3(0.24f, 0.52f, 0.26f)); legR.transform.SetParent(root.transform, true);
        var armL = CreatePrimitive(PrimitiveType.Capsule, "Sleeve L", m["cloth"], new Vector3(-0.62f, 1.46f, 0f), new Vector3(0.18f, 0.62f, 0.18f)); armL.transform.SetParent(root.transform, true); armL.transform.localRotation = Quaternion.Euler(0f, 0f, 28f);
        var armR = CreatePrimitive(PrimitiveType.Capsule, "Sleeve R", m["cloth"], new Vector3(0.62f, 1.46f, 0f), new Vector3(0.18f, 0.62f, 0.18f)); armR.transform.SetParent(root.transform, true); armR.transform.localRotation = Quaternion.Euler(0f, 0f, -28f);
        var staff = CreatePrimitive(PrimitiveType.Cylinder, "Adept staff", m["wood"], new Vector3(0.84f, 1.58f, -0.05f), new Vector3(0.06f, 1.15f, 0.06f)); staff.transform.SetParent(root.transform, true); staff.transform.localRotation = Quaternion.Euler(0f, 0f, -8f);
        var orb = CreatePrimitive(PrimitiveType.Sphere, "Spirit orb", m["orb"], new Vector3(0.70f, 2.75f, -0.07f), Vector3.one * 0.16f); orb.transform.SetParent(root.transform, true);
        var orbLight = new GameObject("Spirit orb glow"); orbLight.transform.SetParent(orb.transform, false); var light = orbLight.AddComponent<Light>(); light.type = LightType.Point; light.color = new Color(0.20f, 0.95f, 0.82f); light.intensity = 1.6f; light.range = 2.4f;
        return root;
    }

    private static void CreateCamera(Transform target)
    {
        var go = new GameObject("Showcase Camera"); go.tag = "MainCamera";
        var camera = go.AddComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 4.8f; camera.nearClipPlane = 0.05f; camera.farClipPlane = 100f; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.48f, 0.64f, 0.68f); camera.allowHDR = true; camera.allowMSAA = true;
        go.transform.position = new Vector3(8.6f, 7.8f, -11.4f); go.transform.LookAt(new Vector3(0f, 1.15f, 0.5f));
        var rig = go.AddComponent<InsectSpace.Rendering.IsometricCameraRig>(); rig.Follow(target);
    }

    private static void CreateLightingRig()
    {
        var sunObject = new GameObject("Warm afternoon sun"); var sun = sunObject.AddComponent<Light>(); sun.type = LightType.Directional; sun.color = new Color(1.0f, 0.79f, 0.56f); sun.intensity = 2.2f; sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.72f; sun.shadowBias = 0.04f; sunObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
        var fillObject = new GameObject("Cool sky fill"); var fill = fillObject.AddComponent<Light>(); fill.type = LightType.Directional; fill.color = new Color(0.44f, 0.65f, 1.0f); fill.intensity = 0.42f; fill.shadows = LightShadows.None; fillObject.transform.rotation = Quaternion.Euler(26f, 142f, 0f);
    }

    private static void CreatePostProcessing()
    {
        var volume = new GameObject("Showcase color grade"); var v = volume.AddComponent<Volume>(); v.isGlobal = true; v.priority = 10;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>(); profile.name = "Meadow Showcase Volume";
        var bloom = profile.Add<Bloom>(); bloom.intensity.Override(0.16f); bloom.threshold.Override(1.15f); bloom.scatter.Override(0.72f);
        var color = profile.Add<ColorAdjustments>(); color.postExposure.Override(0.18f); color.contrast.Override(8f); color.saturation.Override(7f); color.colorFilter.Override(new Color(1.02f, 0.98f, 0.92f));
        var vignette = profile.Add<Vignette>(); vignette.intensity.Override(0.16f); vignette.smoothness.Override(0.72f);
        v.profile = profile;
        AssetDatabase.CreateAsset(profile, MaterialRoot + "/MeadowShowcaseVolumeProfile.asset");
    }

    private static GameObject CreatePrimitive(PrimitiveType type, string name, Material material, Vector3 position, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.position = position; go.transform.localScale = scale; go.GetComponent<Renderer>().sharedMaterial = material; var collider = go.GetComponent<Collider>(); if (collider != null) UnityEngine.Object.DestroyImmediate(collider); return go;
    }

    private static GameObject CreateCone(string name, Material material, Vector3 position, float radius, float height, int sides)
    {
        var go = new GameObject(name); go.transform.position = position; var mesh = new Mesh(); var vertices = new Vector3[sides + 1]; var triangles = new int[sides * 6]; vertices[0] = Vector3.up * height * 0.5f;
        for (int i = 0; i < sides; i++) vertices[i + 1] = new Vector3(Mathf.Cos(i * Mathf.PI * 2f / sides) * radius, -height * 0.5f, Mathf.Sin(i * Mathf.PI * 2f / sides) * radius);
        for (int i = 0; i < sides; i++) { int n = (i + 1) % sides; triangles[i * 3] = 0; triangles[i * 3 + 1] = i + 1; triangles[i * 3 + 2] = n + 1; int o = sides * 3 + i * 3; triangles[o] = i + 1; triangles[o + 1] = n + 1; triangles[o + 2] = 0; }
        mesh.vertices = vertices; mesh.triangles = triangles; mesh.RecalculateNormals(); var filter = go.AddComponent<MeshFilter>(); filter.sharedMesh = mesh; var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material; return go;
    }

    private static void EnsureFolder(string path)
    {
        var pieces = path.Split('/'); var current = pieces[0];
        for (int i = 1; i < pieces.Length; i++) { var next = current + "/" + pieces[i]; if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, pieces[i]); current = next; }
    }
}

