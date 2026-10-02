using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

// Ephemeral editor authoring through Unity CLI. LOCAL ART STUDY, no gameplay/routing.
public static class BuildQingMaoMapStudy
{
    const string ScenePath = "Assets/InsectSpace/Scenes/MapStudies/QingMaoLayoutStudy.unity";
    const string ArtPath = "Assets/InsectSpace/Rendering/MapStudies/QingMaoLayout";
    static Material soil, turf, wood, bamboo, roof, rock, water, lamp, dark, leaves;
    static Transform root;
    static int meshId;

    public static object Run()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling)
            throw new InvalidOperationException("Stop Play and wait for compilation before authoring.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save modified scenes before creating the study.");
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null ||
            AssetDatabase.IsValidFolder(ArtPath))
            throw new InvalidOperationException("Study already exists; inspect it instead of replacing it.");
        var shader = Shader.Find("InsectSpace/Meadow Painterly");
        if (shader == null || ShaderUtil.ShaderHasError(shader))
            throw new InvalidOperationException("Existing painterly shader must compile.");
        Folder(ArtPath); Folder("Assets/InsectSpace/Scenes/MapStudies");
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        root = new GameObject("LOCAL ART STUDY - QingMao layout, proposed geography").transform;
        soil = Mat("OchrePath", "#D1B883", shader);
        turf = Mat("JadeGround", "#567E63", shader);
        wood = Mat("WarmTimber", "#80563D", shader);
        bamboo = Mat("Bamboo", "#638B65", shader);
        roof = Mat("SlateJadeRoof", "#405B57", shader);
        rock = Mat("WetStone", "#748D86", shader);
        water = Mat("Creek", "#68AEB5", shader);
        lamp = Mat("WarmWindows", "#E3B663", shader);
        dark = Mat("DeepForest", "#174B49", shader);
        leaves = Mat("LightCanopy", "#88A976", shader);
        meshId = 0;

        Box("Ground - compact playable block, not the entire settlement", new Vector3(0,-.45f,1), new Vector3(53,.9f,43), turf);
        Box("Family hall terrace", new Vector3(0,.25f,9), new Vector3(17,.5f,15), rock);
        Box("Main square - open interaction space", new Vector3(0,.54f,6), new Vector3(14,.09f,9), soil);
        Path("East gate to square", new [] { new Vector3(23,0,-11),new Vector3(16,0,-11),new Vector3(10,0,-8),new Vector3(7,0,-3),new Vector3(4,0,2),new Vector3(0,0,4) },3.2f,soil);
        Path("School branch",new [] {new Vector3(1,0,1),new Vector3(-5,0,-2),new Vector3(-11,0,-2)},2.8f,soil);
        Path("Residential branch",new [] {new Vector3(13,0,-10),new Vector3(8,0,-14),new Vector3(-4,0,-14),new Vector3(-10,0,-10)},2.5f,soil);
        Path("North exit branch",new [] {new Vector3(7,0,2),new Vector3(11,0,6),new Vector3(11,0,18)},2.6f,soil);
        Path("Small peripheral creek - design addition",new [] {new Vector3(-23,0,19),new Vector3(-21,0,10),new Vector3(-22,0,0),new Vector3(-21,0,-10),new Vector3(-17,0,-19)},1.4f,water);
        for(int i=0;i<5;i++) Box("Broad square approach step",new Vector3(0,.045f+i*.05f,1.0f+i*.24f),new Vector3(5,.09f+i*.1f,.3f),rock);

        // Five explicit storeys; the hall is the central landmark, not a palace complex.
        var hall = new GameObject("GY-02 Family hall - five storeys"); hall.transform.SetParent(root);
        for(int level=0;level<5;level++)
        {
            float width=6.4f-level*.55f, y=.6f+level*1.65f;
            var body=Box("Hall storey "+(level+1),new Vector3(0,y+.65f,10),new Vector3(width,1.3f,width*.72f),wood);body.transform.SetParent(hall.transform);
            var r=Roof("Hall eave "+(level+1),new Vector3(0,y+1.3f,10),width+1.25f,width*.72f+1.2f,.9f);r.transform.SetParent(hall.transform);
            for(int n=-1;n<=1;n++) Box("Hall window",new Vector3(n*1.35f,y+.7f,10-width*.36f-.012f),new Vector3(.55f,.65f,.05f),lamp).transform.SetParent(hall.transform);
        }
        Box("Hall door - underground route remains a separate scene",new Vector3(0,1.15f,7.65f),new Vector3(1.35f,1.45f,.08f),dark);

        Box("GY-04 Academy stone practice yard",new Vector3(-12,.12f,1),new Vector3(10,.22f,9),rock);
        House("GY-04 Academy",-12,7,6,4,1);
        for(int i=0;i<14;i++)
        {
            Pole("Academy bamboo boundary",new Vector3(-17,.05f,-3+i*.75f),2.2f,.065f,bamboo);
            Pole("Academy bamboo boundary",new Vector3(-7,.05f,-3+i*.75f),2.2f,.065f,bamboo);
        }
        for(int i=0;i<3;i++){Pole("Practice target",new Vector3(-15+i*2.7f,0,3),1.35f,.09f,wood);Box("Practice target arm",new Vector3(-15+i*2.7f,.95f,3),new Vector3(.9f,.12f,.13f),wood);}
        House("GY-03 East gate wine house - single storey",16,-5.5f,5,3.8f,1);
        House("GY-09 Stilt dwelling A",5,-17,4.0f,3.2f,1);
        House("GY-09 Stilt dwelling B",-2,-18,4.0f,3.4f,1);
        House("GY-09 Stilt dwelling C",-10,-13,4.0f,3.0f,1);
        House("GY-09 Stilt dwelling D",-16,-7,4.2f,3.0f,1);
        House("Service house - proposed location",17,5,4.2f,3.4f,1);
        House("Stilt dwelling E",18,12,4.5f,3.2f,1);
        Gate("GY-03 East gate",new Vector3(23,0,-11),90);
        Gate("GY-12 North gate",new Vector3(11,0,18),0);
        for(int i=0;i<5;i++)Box("Footbridge plank",new Vector3(-21+i*.55f,.2f,-8),new Vector3(.52f,.18f,1.8f),wood);
        for(int i=0;i<7;i++)
        {
            float a=i*2.399f; float x=15.3f+Mathf.Cos(a)*1.4f,z=-8.2f+Mathf.Sin(a)*.55f;
            var pot=Primitive("Wine jar",PrimitiveType.Sphere,new Vector3(x,.36f,z),new Vector3(.45f,.7f,.45f),wood);
            Box("Jar lid",new Vector3(x,.68f,z),new Vector3(.22f,.06f,.22f),lamp);
        }
        // Deterministic geometric placement for a visual study, outside simulation assemblies.
        for(int i=0;i<40;i++)
        {
            float angle=i*Mathf.PI*2/40;
            float x=Mathf.Cos(angle)*24,z=1+Mathf.Sin(angle)*19;
            if((x>19&&z>-15&&z<-7)||(x>7&&x<15&&z>15))continue;
            Cluster(x,z,i%3);
        }
        for(int i=0;i<7;i++){Cluster(-18+i*5.5f,20.5f,i%3);Boulder(-22+i*7,23,3.5f+i%2);}
        var scale=Primitive("1.7m human scale marker - no character asset",PrimitiveType.Capsule,new Vector3(3,.85f,-.5f),new Vector3(.5f,.85f,.5f),lamp);

        var cameraObject=new GameObject("Map Study Camera - 45 degree orthographic");cameraObject.transform.SetParent(root);
        var camera=cameraObject.AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=20.5f;
        camera.transform.position=new Vector3(0,42,-41);camera.transform.LookAt(new Vector3(0,0,1));
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=ColorHex("#C8D3BD");camera.nearClipPlane=.1f;camera.farClipPlane=140;
        // Separate culling from any previously open scene while preserving all its objects.
        foreach(var transform in root.GetComponentsInChildren<Transform>(true))transform.gameObject.layer=30;
        camera.cullingMask=1<<30;camera.tag="Untagged";
        var lightObject=new GameObject("Map Study Warm Sun");lightObject.transform.SetParent(root);lightObject.layer=30;
        var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.1f;light.color=new Color(1,.95f,.82f);light.shadows=LightShadows.Soft;light.cullingMask=1<<30;light.transform.rotation=Quaternion.Euler(48,-35,0);
        AssetDatabase.SaveAssets();
        if(!EditorSceneManager.SaveScene(scene,ScenePath))throw new InvalidOperationException("Could not save study.");
        SceneManager.SetActiveScene(previous);
        Selection.activeGameObject=hall;
        return new {scene=ScenePath,previousScene=previous.path,loadedAdditively=true,renderers=root.GetComponentsInChildren<Renderer>().Length,storeys=5,cameraPitch=camera.transform.eulerAngles.x,localArtStudy=true};
    }

    public static object Validate()
    {
        var scene=SceneManager.GetSceneByPath(ScenePath);
        bool opened=!scene.IsValid()||!scene.isLoaded;
        if(opened)scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
        try
        {
            var transforms=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
            var renderers=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Renderer>(true)).ToArray();
            var cameras=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>(true)).ToArray();
            var missing=renderers.Count(r=>r.sharedMaterials.Any(m=>m==null||m.shader==null||ShaderUtil.ShaderHasError(m.shader)));
            var storeys=transforms.Count(t=>t.name.StartsWith("Hall storey "));
            if(cameras.Length!=1||!cameras[0].orthographic||storeys!=5||missing!=0)throw new InvalidOperationException("Map study structural check failed.");
            var colliders=transforms.Count(t=>t.GetComponent<Collider>()!=null);
            return new {scene=scene.path,storeys,cameras=cameras.Length,orthographic=cameras[0].orthographic,pitch=cameras[0].transform.eulerAngles.x,renderers=renderers.Length,missingMaterials=missing,colliders,localArtStudy=true};
        }
        finally {if(opened)EditorSceneManager.CloseScene(scene,true);}
    }

    static void Folder(string path){var bits=path.Split('/');string current=bits[0];for(int i=1;i<bits.Length;i++){string next=current+"/"+bits[i];if(!AssetDatabase.IsValidFolder(next))AssetDatabase.CreateFolder(current,bits[i]);current=next;}}
    static Color ColorHex(string hex){ColorUtility.TryParseHtmlString(hex,out var color);return color;}
    static Material Mat(string name,string hex,Shader shader){var m=new Material(shader);m.SetColor("_BaseColor",ColorHex(hex));m.SetFloat("_VertexColor",0);m.SetFloat("_Wind",0);m.SetColor("_ShadowTint",ColorHex("#557F88"));AssetDatabase.CreateAsset(m,ArtPath+"/"+name+".mat");return m;}
    static GameObject Primitive(string name,PrimitiveType type,Vector3 position,Vector3 scale,Material material){var g=GameObject.CreatePrimitive(type);g.name=name;g.transform.SetParent(root);g.transform.position=position;g.transform.localScale=scale;g.GetComponent<Renderer>().sharedMaterial=material;var c=g.GetComponent<Collider>();if(c!=null)UnityEngine.Object.DestroyImmediate(c);return g;}
    static GameObject Box(string name,Vector3 p,Vector3 s,Material m){return Primitive(name,PrimitiveType.Cube,p,s,m);}
    static void Pole(string name,Vector3 ground,float height,float radius,Material m){Primitive(name,PrimitiveType.Cylinder,ground+Vector3.up*height*.5f,new Vector3(radius*2,height*.5f,radius*2),m);}
    static void Path(string name,Vector3[] points,float width,Material m){for(int i=1;i<points.Length;i++){var delta=points[i]-points[i-1];var g=Box(name+" "+i,(points[i]+points[i-1])*.5f+Vector3.up*.045f,new Vector3(width,.075f,delta.magnitude+.4f),m);g.transform.rotation=Quaternion.LookRotation(delta);}}
    static GameObject Roof(string name,Vector3 p,float w,float d,float h)
    {
        var g=new GameObject(name);g.transform.SetParent(root);g.transform.position=p;
        var mesh=new Mesh();mesh.name="Study roof "+(meshId++);
        mesh.vertices=new[]{new Vector3(-w/2,0,-d/2),new Vector3(w/2,0,-d/2),new Vector3(-w/2,h,0),new Vector3(w/2,h,0),new Vector3(-w/2,0,d/2),new Vector3(w/2,0,d/2)};
        mesh.triangles=new[]{0,2,1,1,2,3,2,4,3,3,4,5,0,4,2,1,3,5};mesh.RecalculateNormals();mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh,ArtPath+"/Roof_"+meshId+".asset");g.AddComponent<MeshFilter>().sharedMesh=mesh;g.AddComponent<MeshRenderer>().sharedMaterial=roof;return g;
    }
    static void House(string name,float x,float z,float w,float d,int floors)
    {
        var house=new GameObject(name);house.transform.SetParent(root);
        foreach(float dx in new[]{-.4f,.4f})foreach(float dz in new[]{-.37f,.37f})Pole("Stilt support",new Vector3(x+dx*w,0,z+dz*d),1.5f,.12f,wood);
        Box(name+" body",new Vector3(x,1.65f,z),new Vector3(w,1.9f,d),wood).transform.SetParent(house.transform);
        Roof(name+" roof",new Vector3(x,2.6f,z),w+.7f,d+.7f,1.05f).transform.SetParent(house.transform);
        Box(name+" porch",new Vector3(x,.72f,z-d*.5f-.5f),new Vector3(w+.2f,.18f,1),bamboo);
        for(int s=0;s<4;s++)Box(name+" stair",new Vector3(x,.08f+s*.085f,z-d*.5f-1.7f+s*.28f),new Vector3(1.2f,.16f+s*.17f,.32f),rock);
        Box(name+" doorway",new Vector3(x,1.35f,z-d*.5f-.012f),new Vector3(.75f,1.25f,.06f),dark);
        foreach(float dx in new[]{-.3f,.3f})Box(name+" window",new Vector3(x+dx*w,1.8f,z-d*.5f-.02f),new Vector3(.55f,.6f,.06f),lamp);
    }
    static void Gate(string name,Vector3 p,float yaw){var gate=new GameObject(name);gate.transform.SetParent(root);gate.transform.position=p;foreach(float side in new[]{-1f,1f}){var post=Box(name+" post",p+new Vector3(side*2,1.65f,0),new Vector3(.65f,3.3f,.65f),wood);post.transform.SetParent(gate.transform);}var lintel=Box(name+" lintel",p+Vector3.up*3.25f,new Vector3(5.2f,.55f,.75f),wood);lintel.transform.SetParent(gate.transform);var r=Roof(name+" roof",p+Vector3.up*3.55f,5.7f,1.8f,.6f);r.transform.SetParent(gate.transform);gate.transform.rotation=Quaternion.Euler(0,yaw,0);}
    static void Cluster(float x,float z,int variant){for(int i=0;i<4;i++){float dx=Mathf.Sin(i*2.1f)*.8f,dz=Mathf.Cos(i*2.1f)*.8f;Pole("QingMao bamboo stem",new Vector3(x+dx,0,z+dz),3.1f+i*.5f,.07f,bamboo);Primitive("Bamboo leaf mass",PrimitiveType.Sphere,new Vector3(x+dx,2.7f+i*.5f,z+dz),new Vector3(1.2f,.6f,1),variant==0?dark:bamboo);}if(variant==2){Pole("Tree trunk",new Vector3(x,0,z),3,.2f,wood);Primitive("Tree canopy",PrimitiveType.Sphere,new Vector3(x,3.6f,z),new Vector3(3,2,2.7f),leaves);}}
    static void Boulder(float x,float z,float size){Primitive("Peripheral mountain rock",PrimitiveType.Sphere,new Vector3(x,size*.3f,z),new Vector3(size,size*.8f,size*.75f),rock);}
}
