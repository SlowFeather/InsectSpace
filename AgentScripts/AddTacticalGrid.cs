using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Adds the faint hex battle-board cue used by the local rendering showcase.
/// It is presentation-only: no gameplay or authoritative simulation state is written.
/// </summary>
public static class AddTacticalGrid
{
    private const string RootName = "Tactical hex grid - presentation only";
    private const string MaterialPath = "Assets/InsectSpace/Rendering/Showcase/TacticalGrid.mat";
    private const string ActiveMaterialPath = "Assets/InsectSpace/Rendering/Showcase/TacticalGridActive.mat";

    public static string Run()
    {
        var old = GameObject.Find(RootName);
        if (old != null) Object.DestroyImmediate(old);

        var root = new GameObject(RootName);
        root.transform.position = new Vector3(0f, 0.055f, 0.25f);
        var normal = LoadOrCreateMaterial(MaterialPath, "Tactical grid - soft green", new Color(0.30f, 0.95f, 0.65f, 0.34f));
        var active = LoadOrCreateMaterial(ActiveMaterialPath, "Tactical grid - active tile", new Color(1.0f, 0.76f, 0.24f, 0.82f));

        int count = 0;
        for (int row = -1; row <= 1; row++)
        {
            for (int column = -2; column <= 2; column++)
            {
                float x = column * 1.02f + (row & 1) * 0.51f;
                float z = row * 0.88f;
                bool isActive = row == 0 && column == 0;
                CreateHex(root.transform, new Vector3(x, 0f, z), 0.58f, isActive ? active : normal, isActive ? "Active hex tile" : "Hex tile");
                count++;
            }
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Assets/InsectSpace/Scenes/MeadowShowcase.unity");
        AssetDatabase.SaveAssets();
        return "Added presentation-only tactical grid tiles=" + count;
    }

    private static void CreateHex(Transform parent, Vector3 center, float radius, Material material, string name)
    {
        var tile = new GameObject(name);
        tile.transform.SetParent(parent, false);
        tile.transform.localPosition = center;
        var line = tile.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = 6;
        line.widthMultiplier = 0.026f;
        line.numCornerVertices = 1;
        line.numCapVertices = 1;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = material;
        for (int i = 0; i < 6; i++)
        {
            float angle = Mathf.Deg2Rad * (30f + i * 60f);
            line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
        }
    }

    private static Material LoadOrCreateMaterial(string path, string name, Color color)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        material.color = color;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        return material;
    }
}
