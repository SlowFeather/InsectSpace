using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Newtonsoft.Json;

// Builds a local, non-release art study from the extracted AFK reference catalog.
// The source catalog stays under Assets/Temp/AFKStudy and is never added to gameplay content.
public static class BuildAfkEnvironment
{
    const string Root = "Assets/Temp/AFKStudy";
    const string Source = Root + "/Environment/Source";
    const string Generated = Root + "/Environment/Generated";
    const string Scene = Root + "/AFKCloudStudy.unity";

    [Serializable] sealed class Vec3Data { public float x, y, z; }
    [Serializable] sealed class Vec4Data { public float x, y, z, w; }
    [Serializable] sealed class SubmeshData { public int[] indices; }
    [Serializable] sealed class MeshData
    {
        public string name;
        public Vec3Data[] vertices;
        public Vec3Data[] normals;
        public Vec3Data[] uv;
        public Vec4Data[] colors;
        public SubmeshData[] submeshes;
    }
    [Serializable] sealed class MeshSummaryData { public string id, name; public int vertices, triangles; }
    [Serializable] sealed class TextureData { public string id, name; public int width, height; }
    [Serializable] sealed class TextureRefData { public string property, id; public Vec3Data scale, offset; }
    [Serializable] sealed class ColorData { public string property; public Vec4Data value; }
    [Serializable] sealed class FloatData { public string property; public float value; }
    [Serializable] sealed class MaterialData
    {
        public string id, name, shader, shader_id;
        public TextureRefData[] textures;
        public ColorData[] colors;
        public FloatData[] floats;
    }
    [Serializable] sealed class NodeData
    {
        public string id, parent, name, mesh;
        public bool active, enabled, shadow;
        public Vec3Data position, scale;
        public Vec4Data rotation;
        public float[] matrix;
        public string[] materials;
    }
    [Serializable] sealed class ReportData
    {
        public NodeData[] nodes;
        public MeshSummaryData[] meshes;
        public MaterialData[] materials;
        public TextureData[] textures;
    }

    static Color Hex(string value)
    {
        ColorUtility.TryParseHtmlString(value, out var color);
        return color;
    }

    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        Folder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    static Vector3 V(Vec3Data value) => value == null ? Vector3.zero : new Vector3(value.x, value.y, value.z);
    static Vector2 UV(Vec3Data value) => value == null ? Vector2.zero : new Vector2(value.x, value.y);
    static Vector4 V4(Vec4Data value) => value == null ? Vector4.zero : new Vector4(value.x, value.y, value.z, value.w);

    static Mesh BuildMesh(MeshData data)
    {
        Folder(Generated + "/Meshes");
        string path = Generated + "/Meshes/" + Sanitize(data.name) + ".asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        var generated = new Mesh { name = data.name + "_AFKStudy" };
        generated.indexFormat = IndexFormat.UInt32;
        generated.SetVertices(data.vertices.Select(V).ToList());
        if (data.normals != null && data.normals.Length == data.vertices.Length) generated.SetNormals(data.normals.Select(V).ToList());
        else generated.RecalculateNormals();
        if (data.uv != null && data.uv.Length == data.vertices.Length) generated.SetUVs(0, data.uv.Select(UV).ToList());
        if (data.colors != null && data.colors.Length == data.vertices.Length) generated.SetColors(data.colors.Select(value => (Color)V4(value)).ToList());
        generated.subMeshCount = Math.Max(1, data.submeshes == null ? 0 : data.submeshes.Length);
        if (data.submeshes == null || data.submeshes.Length == 0) generated.SetTriangles(Array.Empty<int>(), 0, true);
        else for (int i = 0; i < data.submeshes.Length; i++) generated.SetTriangles(data.submeshes[i].indices ?? Array.Empty<int>(), i, true);
        generated.RecalculateBounds();
        if (mesh)
        {
            EditorUtility.CopySerialized(generated, mesh);
            UnityEngine.Object.DestroyImmediate(generated);
        }
        else
        {
            AssetDatabase.CreateAsset(generated, path);
            mesh = generated;
        }
        return mesh;
    }

    static string Sanitize(string value)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
        return value.Replace('/', '_').Replace(' ', '_');
    }

    static Texture2D LoadTexture(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        return AssetDatabase.LoadAssetAtPath<Texture2D>(Source + "/" + id + ".png");
    }

    static Material BuildMaterial(MaterialData data, Shader shader)
    {
        Folder(Generated + "/Materials");
        string path = Generated + "/Materials/" + Sanitize(data.name) + "_" + data.id.GetHashCode().ToString("x8") + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            material = new Material(shader) { name = data.name + " (AFK study)" };
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        material.SetColor("_BaseColor", Color.white);
        material.SetColor("_ShadowTint", Hex("#5E7880"));
        material.SetFloat("_Wind", data.shader != null && data.shader.Contains("Grass") ? .035f : .012f);
        // Most extracted environment meshes do not carry a COLOR vertex stream.
        // Sampling that missing stream with _VertexColor=1 turns the textured
        // surface black, so use the albedo texture as the authoritative palette.
        material.SetFloat("_VertexColor", 0f);
        material.SetFloat("_CloudShadow", .48f);
        material.SetFloat("_MistIntensity", .08f);
        // The extracted PNGs keep transparent pixels with black RGB values.  The
        // original shaders use alpha testing for both billboard foliage and some
        // atlas-backed world meshes, so leaving the cutoff at zero produces large
        // black silhouettes in the local study.
        float cutoff = .025f;
        if (data.shader != null && (data.shader.Contains("Foliage") || data.shader.Contains("Grass"))) cutoff = .34f;
        material.SetFloat("_Cutoff", cutoff);
        // Clear a previous build's albedo before resolving the current catalog;
        // otherwise an old noise map can survive when the source has no albedo.
        material.SetTexture("_BaseMap", Texture2D.whiteTexture);
        // Prefer the source material's albedo slot.  Falling back to the first
        // available image can bind a noise/normal map to the albedo (notably the
        // GPU grass material), which produces bright blue spikes in the study.
        var texture = (data.textures ?? Array.Empty<TextureRefData>())
            .FirstOrDefault(value => value.property == "_MainTex" && !string.IsNullOrEmpty(value.id));
        if (texture == null)
            texture = (data.textures ?? Array.Empty<TextureRefData>())
                .FirstOrDefault(value => (value.property == "_GrassTexture" || value.property == "_DetailTex") && !string.IsNullOrEmpty(value.id));
        bool hasAlbedo = texture != null;
        if (texture != null)
        {
            var source = LoadTexture(texture.id);
            if (source) material.SetTexture("_BaseMap", source);
            if (texture.scale != null) material.SetTextureScale("_BaseMap", new Vector2(texture.scale.x, texture.scale.y));
            if (texture.offset != null) material.SetTextureOffset("_BaseMap", new Vector2(texture.offset.x, texture.offset.y));
        }
        // The extracted GPU-instanced grass proxies contain placement data but
        // no albedo image.  A white placeholder makes their broad blades read
        // as blown-out spikes in the study, so use the meadow palette instead.
        if (!hasAlbedo && data.name != null && data.name.Contains("gpuinstancegrass"))
        {
            material.SetColor("_BaseColor", data.name.Contains("_field")
                ? new Color(.28f, .48f, .24f, 1f)
                : new Color(.42f, .62f, .27f, 1f));
            material.SetFloat("_Wind", .006f);
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    static Matrix4x4 Matrix(float[] values)
    {
        var result = Matrix4x4.identity;
        if (values == null || values.Length != 16) return result;
        for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++) result[row, column] = values[row * 4 + column];
        return result;
    }

    static void ApplyTransform(Transform target, NodeData data)
    {
        target.localPosition = V(data.position);
        target.localRotation = data.rotation == null ? Quaternion.identity : new Quaternion(data.rotation.x, data.rotation.y, data.rotation.z, data.rotation.w);
        target.localScale = data.scale == null ? Vector3.one : V(data.scale);
        if (data.matrix != null && data.matrix.Length == 16)
        {
            var matrix = Matrix(data.matrix);
            target.localRotation = matrix.rotation;
            target.localScale = matrix.lossyScale;
        }
    }

    static void Place(Dictionary<string, GameObject> roots, string name, Vector3 position, Vector3 scale, Vector3 euler)
    {
        if (!roots.TryGetValue(name, out var root)) return;
        root.transform.localPosition = position;
        root.transform.localScale = scale;
        root.transform.localRotation = Quaternion.Euler(euler);
    }

    public static object Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before authoring.");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        string reportPath = Source + "/prefabs.json";
        var report = JsonConvert.DeserializeObject<ReportData>(File.ReadAllText(reportPath));
        if (report == null || report.nodes == null) throw new InvalidOperationException("Environment catalog is missing or invalid.");
        var shader = Shader.Find("InsectSpace/Hero Meadow Atmosphere");
        if (!shader) throw new InvalidOperationException("Missing InsectSpace/Hero Meadow Atmosphere shader.");

        var scene = EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
        var previous = GameObject.Find("AFK extracted environment - local study");
        if (previous) UnityEngine.Object.DestroyImmediate(previous);
        var environment = new GameObject("AFK extracted environment - local study");
        var nodeObjects = new Dictionary<string, GameObject>();
        var meshById = new Dictionary<string, Mesh>();
        var materialById = new Dictionary<string, Material>();

        // MeshData is keyed by the catalog id in prefabs.json, so load each JSON beside the report.
        foreach (var node in report.nodes)
        {
            if (string.IsNullOrEmpty(node.mesh) || meshById.ContainsKey(node.mesh)) continue;
            string meshPath = Source + "/" + node.mesh + ".json";
            if (!File.Exists(meshPath)) continue;
            var data = JsonConvert.DeserializeObject<MeshData>(File.ReadAllText(meshPath));
            meshById[node.mesh] = BuildMesh(data);
        }
        foreach (var material in report.materials ?? Array.Empty<MaterialData>()) materialById[material.id] = BuildMaterial(material, shader);

        foreach (var node in report.nodes)
        {
            var gameObject = new GameObject(node.name);
            gameObject.SetActive(node.active);
            ApplyTransform(gameObject.transform, node);
            nodeObjects[node.id] = gameObject;
            if (!string.IsNullOrEmpty(node.mesh) && meshById.TryGetValue(node.mesh, out var mesh))
            {
                var filter = gameObject.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                var renderer = gameObject.AddComponent<MeshRenderer>();
                var materials = (node.materials ?? Array.Empty<string>()).Where(materialById.ContainsKey).Select(id => materialById[id]).ToArray();
                if (materials.Length > 0) renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = node.shadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
                renderer.receiveShadows = true;
                renderer.allowOcclusionWhenDynamic = false;
            }
        }
        foreach (var node in report.nodes)
        {
            if (!nodeObjects.TryGetValue(node.id, out var child)) continue;
            var parent = string.IsNullOrEmpty(node.parent) ? environment.transform : nodeObjects.TryGetValue(node.parent, out var value) ? value.transform : environment.transform;
            child.transform.SetParent(parent, false);
        }

        var roots = nodeObjects.Values.Where(value => value.transform.parent == environment.transform).ToDictionary(value => value.name, value => value);
        Place(roots, "pbsc_cliff_chapter00_01_2_hd", new Vector3(.4f, 8.0f, 15.0f), new Vector3(1.0f, 1.0f, 1.0f), new Vector3(0, 180, 0));
        Place(roots, "pbsc_cliff_chapter00_01_1_hd", new Vector3(-5.1f, 5.2f, 14.0f), new Vector3(1.15f, 1.15f, 1.15f), new Vector3(0, 175, 0));
        Place(roots, "pbsc_chapter01_lakebeach_01_hd", new Vector3(-5.9f, 2.0f, 4.6f), new Vector3(2.15f, 2.15f, 2.15f), new Vector3(0, 12, 0));
        Place(roots, "pbsc_bio_westfall_reed_01_hd", new Vector3(-6.9f, 0, 4.0f), new Vector3(2.45f, 2.45f, 2.45f), new Vector3(0, 15, 0));
        Place(roots, "pbsc_bio_westfall_tree_fir_01_hd", new Vector3(-4.5f, 0, 10.5f), new Vector3(1.45f, 1.45f, 1.45f), new Vector3(0, 0, 0));
        Place(roots, "pbsc_bio_westfall_tree_arborvitae_hd", new Vector3(-1.3f, 0, 12.0f), new Vector3(1.55f, 1.55f, 1.55f), new Vector3(0, 0, 0));
        Place(roots, "pbsc_bio_westfall_tree_oak_01_hd", new Vector3(5.3f, 0, 12.0f), new Vector3(.62f, .62f, .62f), new Vector3(0, 0, 0));
        Place(roots, "pbsc_bio_chapter02_bluecypress_01_hd", new Vector3(7.0f, 0, 11.0f), new Vector3(2.8f, 2.8f, 2.8f), new Vector3(0, 0, 0));
        Place(roots, "pdsc_bio_westfall_shrub_03_hd", new Vector3(3.2f, 0, 9.0f), new Vector3(1.5f, 1.5f, 1.5f), new Vector3(0, 18, 0));
        Place(roots, "pbsc_grass_field_hd", new Vector3(0, .08f, 3.2f), new Vector3(3.8f, 3.8f, 3.8f), new Vector3(0, 0, 0));
        Place(roots, "pbsc_grass_detail_hd", new Vector3(1.9f, .06f, 5.4f), new Vector3(2.7f, 2.7f, 2.7f), new Vector3(0, 30, 0));
        // These two GPU-instanced source meshes are placement proxies rather
        // than complete blade batches.  Without the original instancing data
        // they become oversized geometric spikes, so keep them in the study
        // catalog but let the procedural meadow cover the ground.
        if (roots.TryGetValue("pbsc_grass_field_hd", out var grassField)) grassField.SetActive(false);
        if (roots.TryGetValue("pbsc_grass_detail_hd", out var grassDetail)) grassDetail.SetActive(false);
        Place(roots, "pbsc_flower_01_hd", new Vector3(.3f, .05f, 4.7f), new Vector3(2.8f, 2.8f, 2.8f), new Vector3(0, 0, 0));
        Place(roots, "pbsc_building_human_mercenarycamp_01_hd", new Vector3(6.2f, .18f, 8.2f), new Vector3(.25f, .25f, .25f), new Vector3(0, -28, 0));

        environment.AddComponent<AfkEnvironmentMarker>();
        EditorUtility.SetDirty(environment);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene, Scene);
        Selection.activeGameObject = environment;
        return new { nodes = report.nodes.Length, meshes = meshById.Count, materials = materialById.Count, scene = Scene };
    }

    sealed class AfkEnvironmentMarker : MonoBehaviour { }
}
