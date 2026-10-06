// Run with Unity CLI eval_file. Adds the configured Moonlight encounter to the existing map.
if (UnityEditor.EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop Play before configuring WorldNavigation.");
const string scenePath = "Assets/InsectSpace/Demos/LocalWorldNavigation.unity";
UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath);
var panel = UnityEngine.Object.FindObjectOfType<InsectSpace.Gameplay.Demo.LocalWorldNavigationPanel>();
if (panel == null) throw new System.InvalidOperationException("LocalWorldNavigationPanel not found.");
var existing = UnityEngine.GameObject.Find("Moonlight Encounter - Wild Monster");
if (existing == null)
{
    existing = new UnityEngine.GameObject("Moonlight Encounter - Wild Monster");
    existing.transform.position = new UnityEngine.Vector3(7f, 0f, -6f);
    var marker = UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Cylinder);
    marker.name = "Moonlight Encounter Marker";
    marker.transform.SetParent(existing.transform, false);
    marker.transform.localPosition = new UnityEngine.Vector3(0, .04f, 0);
    marker.transform.localScale = new UnityEngine.Vector3(1.35f, .05f, 1.35f);
    var markerMaterial = new UnityEngine.Material(UnityEngine.Shader.Find("Universal Render Pipeline/Lit"));
    markerMaterial.color = new UnityEngine.Color(.78f, .25f, .25f);
    marker.GetComponent<UnityEngine.Renderer>().sharedMaterial = markerMaterial;
    UnityEngine.Object.DestroyImmediate(marker.GetComponent<UnityEngine.Collider>());
    var monster = UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Sphere);
    monster.name = "Wild Monster Encounter Visual";
    monster.transform.SetParent(existing.transform, false);
    monster.transform.localPosition = new UnityEngine.Vector3(0, .75f, 0);
    monster.transform.localScale = new UnityEngine.Vector3(1.1f, 1.1f, 1.1f);
    var monsterMaterial = new UnityEngine.Material(UnityEngine.Shader.Find("Universal Render Pipeline/Lit"));
    monsterMaterial.color = new UnityEngine.Color(.8f, .2f, .16f);
    monster.GetComponent<UnityEngine.Renderer>().sharedMaterial = monsterMaterial;
    UnityEngine.Object.DestroyImmediate(monster.GetComponent<UnityEngine.Collider>());
}
panel.Encounters = new[] { existing.transform };
panel.MoonlightSceneName = "LocalMoonlightBattle";
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(panel.gameObject.scene, scenePath);
return "Configured Moonlight encounter at " + existing.transform.position;
