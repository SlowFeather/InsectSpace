using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class PolishShowcase
{
    public static string Run()
    {
        var bushes = GameObject.FindObjectsByType<GameObject>();
        int bushIndex = 0;
        int treeIndex = 0;
        foreach (var bush in bushes)
        {
            if (bush.name == "Rounded bush")
            {
                Vector3[] positions = { new Vector3(-5.25f, 0.1f, 4.55f), new Vector3(5.25f, 0.1f, 4.75f), new Vector3(5.0f, 0.1f, -2.0f) };
                MoveRootWithWorldChildren(bush.transform, positions[Mathf.Min(bushIndex, positions.Length - 1)]);
                bush.transform.localScale = Vector3.one * 0.64f;
                bushIndex++;
            }
            else if (bush.name == "Meadow tree")
            {
                Vector3[] positions = { new Vector3(-5.9f, 0f, 4.9f), new Vector3(5.9f, 0f, 5.1f) };
                MoveRootWithWorldChildren(bush.transform, positions[Mathf.Min(treeIndex, positions.Length - 1)]);
                bush.transform.localScale = Vector3.one * 0.82f;
                treeIndex++;
            }
        }

        var cameraObject = GameObject.Find("Showcase Camera");
        var camera = cameraObject != null ? cameraObject.GetComponent<Camera>() : null;
        if (camera != null)
        {
            camera.orthographicSize = 4.15f;
            camera.backgroundColor = new Color(0.42f, 0.60f, 0.66f);
            cameraObject.transform.position = new Vector3(7.6f, 7.3f, -10.6f);
            cameraObject.transform.LookAt(new Vector3(0f, 1.25f, 0.45f));
        }

        var sun = GameObject.Find("Warm afternoon sun")?.GetComponent<Light>();
        if (sun != null) { sun.intensity = 2.6f; sun.shadowStrength = 0.64f; }
        var fill = GameObject.Find("Cool sky fill")?.GetComponent<Light>();
        if (fill != null) fill.intensity = 0.52f;

        if (GameObject.Find("Character rim light") == null)
        {
            var rimObject = new GameObject("Character rim light");
            rimObject.transform.position = new Vector3(-2.7f, 4.4f, -3.1f);
            var rim = rimObject.AddComponent<Light>();
            rim.type = LightType.Point; rim.color = new Color(0.35f, 0.83f, 0.97f); rim.intensity = 3.2f; rim.range = 5.5f;
        }

        if (GameObject.Find("Character face key") == null)
        {
            var faceObject = new GameObject("Character face key");
            faceObject.transform.position = new Vector3(0f, 2.6f, -2.25f);
            var face = faceObject.AddComponent<Light>();
            face.type = LightType.Point; face.color = new Color(1.0f, 0.52f, 0.28f); face.intensity = 1.7f; face.range = 3.8f;
        }

        var volume = GameObject.Find("Showcase color grade")?.GetComponent<Volume>();
        if (volume != null && volume.profile != null)
        {
            var bloom = volume.profile.components.Find(c => c is UnityEngine.Rendering.Universal.Bloom) as UnityEngine.Rendering.Universal.Bloom;
            if (bloom != null) bloom.intensity.Override(0.22f);
            var color = volume.profile.components.Find(c => c is UnityEngine.Rendering.Universal.ColorAdjustments) as UnityEngine.Rendering.Universal.ColorAdjustments;
            if (color != null) { color.postExposure.Override(0.28f); color.contrast.Override(11f); color.saturation.Override(12f); }
        }

        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/InsectSpace/Scenes/MeadowShowcase.unity");
        AssetDatabase.SaveAssets();
        return "Polished AFK Journey-inspired isometric framing and color lighting.";
    }

    private static void MoveRootWithWorldChildren(Transform root, Vector3 target)
    {
        Vector3 delta = target - root.position;
        root.position = target;
        foreach (Transform child in root) child.position += delta;
    }
}
