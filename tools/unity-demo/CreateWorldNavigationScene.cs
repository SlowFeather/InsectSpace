// Run with Unity CLI eval_file. All scene and material writes use Editor APIs.
if (UnityEditor.EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop Play before opening WorldNavigation.");
for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
    if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) throw new System.InvalidOperationException("Save current scene edits first.");
const string path = "Assets/InsectSpace/Demos/LocalWorldNavigation.unity";
if (System.IO.File.Exists(path))
{
    UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path);
    var existing = UnityEngine.Object.FindObjectOfType<InsectSpace.Gameplay.Demo.LocalWorldNavigationPanel>();
    if (existing != null && existing.Motor != null) { UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene = null; return "Opened " + path; }
}
const string art = "Assets/InsectSpace/Demos/NavigationArt";
if (!UnityEditor.AssetDatabase.IsValidFolder(art)) UnityEditor.AssetDatabase.CreateFolder("Assets/InsectSpace/Demos", "NavigationArt");
var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
UnityEngine.Material Mat(string name, UnityEngine.Color color)
{
    var p = art + "/" + name + ".mat";
    var m = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(p);
    if (m == null) { m = new UnityEngine.Material(UnityEngine.Shader.Find("Universal Render Pipeline/Lit")); m.color = color; m.SetFloat("_Smoothness", .1f); UnityEditor.AssetDatabase.CreateAsset(m,p); }
    return m;
}
var grass = Mat("Grass", new UnityEngine.Color(.25f,.42f,.31f));
var stone = Mat("Stone", new UnityEngine.Color(.5f,.52f,.45f));
var wood = Mat("Wood", new UnityEngine.Color(.3f,.21f,.15f));
var wall = Mat("Plaster", new UnityEngine.Color(.77f,.70f,.51f));
var roof = Mat("Roof", new UnityEngine.Color(.13f,.29f,.29f));
var leaves = Mat("Leaves", new UnityEngine.Color(.19f,.35f,.23f));
var gold = Mat("Marker", new UnityEngine.Color(.96f,.71f,.22f));
var pathMat = Mat("Path", new UnityEngine.Color(.62f,.56f,.4f));
var geometry = new UnityEngine.GameObject("Navigation Geometry").transform;
var decorations = new UnityEngine.GameObject("Decoration").transform;
UnityEngine.GameObject Shape(string name, UnityEngine.PrimitiveType type, UnityEngine.Vector3 p, UnityEngine.Vector3 size, UnityEngine.Material mat, bool blocks)
{
    var o=UnityEngine.GameObject.CreatePrimitive(type); o.name=name; o.transform.SetParent(blocks?geometry:decorations); o.transform.position=p; o.transform.localScale=size; o.GetComponent<UnityEngine.Renderer>().sharedMaterial=mat;
    if (!blocks) UnityEngine.Object.DestroyImmediate(o.GetComponent<UnityEngine.Collider>()); return o;
}
Shape("Ground", UnityEngine.PrimitiveType.Cube, new UnityEngine.Vector3(0,-.3f,0), new UnityEngine.Vector3(36,.6f,36),grass,true);
Shape("Crossing", UnityEngine.PrimitiveType.Cube,new UnityEngine.Vector3(0,.015f,0),new UnityEngine.Vector3(2.5f,.03f,26),pathMat,false);
Shape("Crossing east",UnityEngine.PrimitiveType.Cube,new UnityEngine.Vector3(0,.016f,0),new UnityEngine.Vector3(26,.03f,2.5f),pathMat,false);
var positions=new[]{new UnityEngine.Vector3(-5,0,5),new UnityEngine.Vector3(5,0,4),new UnityEngine.Vector3(3,0,-6)};
var stations=new UnityEngine.Transform[3];
for(int i=0;i<3;i++)
{
    var p=positions[i]; var target=new UnityEngine.GameObject("NPC approach "+i); target.transform.position=p; stations[i]=target.transform;
    Shape("Approach marker "+i,UnityEngine.PrimitiveType.Cylinder,p+new UnityEngine.Vector3(0,.025f,0),new UnityEngine.Vector3(1.1f,.025f,1.1f),gold,false);
    Shape("House "+i,UnityEngine.PrimitiveType.Cube,p+new UnityEngine.Vector3(0,1.25f,2.8f),new UnityEngine.Vector3(3.4f,2.5f,2.6f),wall,true);
    Shape("Roof "+i,UnityEngine.PrimitiveType.Cube,p+new UnityEngine.Vector3(0,2.7f,2.8f),new UnityEngine.Vector3(4.1f,.6f,3.2f),roof,false);
    Shape("Counter "+i,UnityEngine.PrimitiveType.Cube,p+new UnityEngine.Vector3(0,.55f,1.2f),new UnityEngine.Vector3(2.8f,1.1f,.6f),wood,true);
    Shape("Merchant "+i,UnityEngine.PrimitiveType.Capsule,p+new UnityEngine.Vector3(0,.8f,1.9f),new UnityEngine.Vector3(.65f,.8f,.65f),gold,false);
}
Shape("Garden obstruction",UnityEngine.PrimitiveType.Cube,new UnityEngine.Vector3(-2,.75f,1.8f),new UnityEngine.Vector3(3,1.5f,1.3f),stone,true);
Shape("Herb planter",UnityEngine.PrimitiveType.Cube,new UnityEngine.Vector3(-2,1.55f,1.8f),new UnityEngine.Vector3(2.7f,.2f,1),leaves,false);
for(int i=0;i<18;i++)
{
    float angle=i*UnityEngine.Mathf.PI*2/18; var p=new UnityEngine.Vector3(UnityEngine.Mathf.Cos(angle)*13,0,UnityEngine.Mathf.Sin(angle)*13);
    Shape("Tree trunk "+i,UnityEngine.PrimitiveType.Cylinder,p+UnityEngine.Vector3.up,new UnityEngine.Vector3(.5f,1,.5f),wood,true);
    Shape("Tree crown "+i,UnityEngine.PrimitiveType.Sphere,p+UnityEngine.Vector3.up*3,new UnityEngine.Vector3(3,3.4f,2.6f),leaves,false);
}
var actor=new UnityEngine.GameObject("Player - LOCAL navigation"); actor.transform.position=new UnityEngine.Vector3(-3,0,-5);
var mage=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/InsectSpace/Rendering/Showcase/KayKitAdventurers/Mage.fbx");
if(mage!=null)
{
    var model=(UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(mage); model.transform.SetParent(actor.transform,false); model.transform.localScale=UnityEngine.Vector3.one;
    var mageMat=Mat("Traveller",new UnityEngine.Color(.9f,.9f,.85f)); var texture=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>("Assets/InsectSpace/Rendering/Showcase/KayKitAdventurers/mage_texture.png"); mageMat.mainTexture=texture; UnityEditor.EditorUtility.SetDirty(mageMat);
    foreach(var renderer in model.GetComponentsInChildren<UnityEngine.Renderer>()) renderer.sharedMaterial=mageMat;
}
else { var model=Shape("Traveller",UnityEngine.PrimitiveType.Capsule,actor.transform.position+UnityEngine.Vector3.up,new UnityEngine.Vector3(.7f,1,.7f),gold,false); model.transform.SetParent(actor.transform,true); }
var agent=actor.AddComponent<UnityEngine.AI.NavMeshAgent>(); agent.enabled=false; agent.radius=.5f; agent.height=2; agent.baseOffset=0;
var motor=actor.AddComponent<InsectSpace.Gameplay.Demo.LocalNavigationMotor>(); motor.Geometry=geometry; motor.Actor=agent;
var panel=new UnityEngine.GameObject("Navigation UI").AddComponent<InsectSpace.Gameplay.Demo.LocalWorldNavigationPanel>(); panel.Motor=motor; panel.Stations=stations;
var cameraObject=new UnityEngine.GameObject("Main Camera"); cameraObject.tag="MainCamera"; var camera=cameraObject.AddComponent<UnityEngine.Camera>(); camera.transform.position=actor.transform.position+new UnityEngine.Vector3(13,16,-15); camera.transform.LookAt(actor.transform.position+new UnityEngine.Vector3(0,.65f,0)); camera.orthographic=false; camera.fieldOfView=50; camera.nearClipPlane=.1f; camera.farClipPlane=100; camera.backgroundColor=new UnityEngine.Color(.12f,.21f,.2f); camera.clearFlags=UnityEngine.CameraClearFlags.SolidColor; panel.View=camera;
var sun=new UnityEngine.GameObject("Afternoon sun").AddComponent<UnityEngine.Light>(); sun.type=UnityEngine.LightType.Directional; sun.transform.rotation=UnityEngine.Quaternion.Euler(48,-28,0); sun.intensity=1.5f; sun.color=new UnityEngine.Color(1,.92f,.76f); sun.shadows=UnityEngine.LightShadows.Soft;
UnityEngine.RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat; UnityEngine.RenderSettings.ambientLight=new UnityEngine.Color(.55f,.64f,.63f);
UnityEditor.AssetDatabase.SaveAssets(); UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene,path); UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene=null; return "Created "+path;
