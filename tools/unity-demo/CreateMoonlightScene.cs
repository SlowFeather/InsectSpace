// Run with Unity CLI eval_file. Creates the standalone LOCAL moonlight PvE scene.
if (UnityEditor.EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop Play before opening Moonlight battle.");
const string path = "Assets/InsectSpace/Demos/LocalMoonlightBattle.unity";
if (System.IO.File.Exists(path))
{
    UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path);
    UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene = null;
    return "Opened " + path;
}
if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/InsectSpace/Demos")) UnityEditor.AssetDatabase.CreateFolder("Assets/InsectSpace", "Demos");
var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
var panelObject = new UnityEngine.GameObject("Moonlight Battle UI - LOCAL PvE");
var panel = panelObject.AddComponent<InsectSpace.Gameplay.Moonlight.LocalMoonlightBattlePanel>();
panel.Port = 7779;
panel.ReturnSceneName = "LocalWorldNavigation";
var cameraObject = new UnityEngine.GameObject("Main Camera");
cameraObject.tag = "MainCamera";
var camera = cameraObject.AddComponent<UnityEngine.Camera>();
camera.transform.position = new UnityEngine.Vector3(0, 8.5f, -8.5f);
camera.transform.rotation = UnityEngine.Quaternion.Euler(42, 0, 0);
camera.fieldOfView = 48;
camera.clearFlags = UnityEngine.CameraClearFlags.SolidColor;
camera.backgroundColor = new UnityEngine.Color(.08f, .12f, .14f);
camera.nearClipPlane = .1f;
camera.farClipPlane = 100;
var light = new UnityEngine.GameObject("Moonlight Arena Light").AddComponent<UnityEngine.Light>();
light.type = UnityEngine.LightType.Directional;
light.transform.rotation = UnityEngine.Quaternion.Euler(48, -30, 0);
light.intensity = 1.2f;
light.color = new UnityEngine.Color(.72f, .82f, 1f);
UnityEngine.RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
UnityEngine.RenderSettings.ambientLight = new UnityEngine.Color(.22f, .28f, .34f);
UnityEditor.AssetDatabase.SaveAssets();
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, path);
UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene = null;
return "Created " + path;
