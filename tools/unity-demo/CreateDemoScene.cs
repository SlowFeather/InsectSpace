// Executed by Unity CLI eval_file inside the connected Unity 6 Editor.
// No Editor assembly or protected platform tooling is changed.
if (UnityEditor.EditorApplication.isPlaying)
    throw new System.InvalidOperationException("Stop Play Mode before opening the demo.");
for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
    if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
        throw new System.InvalidOperationException("Save your current scene changes before opening ClientDemoHub.");
const string path = "Assets/InsectSpace/Demos/ClientDemoHub.unity";
if (System.IO.File.Exists(path))
{
    UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path);
    // Foundation setup may pin Bootstrap as the Play Mode start scene. Play the scene we opened.
    UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene = null;
    return "Opened " + path;
}
if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/InsectSpace/Demos"))
    UnityEditor.AssetDatabase.CreateFolder("Assets/InsectSpace", "Demos");
var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
    UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
var root = new UnityEngine.GameObject("Client Demo Hub - LOCAL TEACHING ONLY");
root.AddComponent<InsectSpace.Gameplay.Demo.ClientDemoHub>();
var cameraObject = new UnityEngine.GameObject("Main Camera");
cameraObject.tag = "MainCamera";
var camera = cameraObject.AddComponent<UnityEngine.Camera>();
camera.orthographic = true; camera.orthographicSize = 7;
camera.clearFlags = UnityEngine.CameraClearFlags.SolidColor;
camera.backgroundColor = new UnityEngine.Color(0.065f, 0.10f, 0.13f);
camera.nearClipPlane = 0.1f; camera.farClipPlane = 100;
cameraObject.transform.position = new UnityEngine.Vector3(10, 13, -10);
cameraObject.transform.LookAt(UnityEngine.Vector3.zero);
cameraObject.AddComponent<InsectSpace.Rendering.IsometricCameraRig>();
var lightObject = new UnityEngine.GameObject("Soft key light");
var light = lightObject.AddComponent<UnityEngine.Light>();
light.type = UnityEngine.LightType.Directional; light.intensity = 1.5f;
lightObject.transform.rotation = UnityEngine.Quaternion.Euler(50, -35, 0);
UnityEngine.RenderSettings.ambientLight = new UnityEngine.Color(0.45f, 0.52f, 0.60f);
var material = new UnityEngine.Material(UnityEngine.Shader.Find("Universal Render Pipeline/Lit"));
material.color = new UnityEngine.Color(0.13f, 0.23f, 0.25f);
UnityEditor.AssetDatabase.CreateAsset(material, "Assets/InsectSpace/Demos/DemoGround.mat");
var ground = UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Cube);
ground.name = "Teaching stage";
ground.transform.position = new UnityEngine.Vector3(0, -1.2f, 0);
ground.transform.localScale = new UnityEngine.Vector3(11, 0.4f, 9);
UnityEngine.Object.DestroyImmediate(ground.GetComponent<UnityEngine.Collider>());
ground.GetComponent<UnityEngine.Renderer>().sharedMaterial = material;
var markerMaterial = new UnityEngine.Material(material);
markerMaterial.color = new UnityEngine.Color(0.20f, 0.37f, 0.37f);
UnityEditor.AssetDatabase.CreateAsset(markerMaterial, "Assets/InsectSpace/Demos/DemoMarkers.mat");
for (int i = -4; i <= 4; i += 2)
{
    var marker = UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Cube);
    marker.name = "Stage grid " + i;
    marker.transform.position = new UnityEngine.Vector3(i, -0.99f, 0);
    marker.transform.localScale = new UnityEngine.Vector3(0.025f, 0.015f, 8);
    UnityEngine.Object.DestroyImmediate(marker.GetComponent<UnityEngine.Collider>());
    marker.GetComponent<UnityEngine.Renderer>().sharedMaterial = markerMaterial;
}
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, path);
UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene = null;
UnityEditor.AssetDatabase.SaveAssets();
return "Created and opened " + path;
