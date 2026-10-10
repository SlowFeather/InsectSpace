// Unity CLI eval_file: create native scene/assets through the running Editor.
if (UnityEditor.EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop Play before opening LocalEconomy.");
for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
    if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) throw new System.InvalidOperationException("Save current scene edits first.");
const string path = "Assets/InsectSpace/Demos/LocalEconomy.unity";
if (System.IO.File.Exists(path)) UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path);
else
{
    if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/InsectSpace/Demos")) UnityEditor.AssetDatabase.CreateFolder("Assets/InsectSpace", "Demos");
    var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
    new UnityEngine.GameObject("Economy Lab - LOCAL TCP / SIMULATED RECHARGE").AddComponent<InsectSpace.Gameplay.Economy.LocalEconomyPanel>();
    var cameraObject = new UnityEngine.GameObject("Main Camera"); cameraObject.tag = "MainCamera";
    var camera = cameraObject.AddComponent<UnityEngine.Camera>();
    camera.clearFlags = UnityEngine.CameraClearFlags.SolidColor; camera.backgroundColor = new UnityEngine.Color(.045f, .065f, .085f);
    UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, path);
}
UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene = null;
return "Opened " + path;
