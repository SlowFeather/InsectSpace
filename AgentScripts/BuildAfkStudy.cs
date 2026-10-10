using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using InsectSpace.Rendering;

// Local reference assets remain in the ignored Temp folder.
public static class BuildAfkStudy
{
    const string Root="Assets/Temp/AFKStudy";
    const string Model=Root+"/Models/Faye/pbsc_char_hero_faye_show.fbx";
    const string Scene=Root+"/AFKCloudStudy.unity";
    [Serializable] public sealed class MeshData { public Vector3[] vertices,normals; public Vector2[] uv; public int[] triangles; }
    static Color C(string hex) { ColorUtility.TryParseHtmlString(hex,out var c);return c; }
    static void Folder(string p) {if(AssetDatabase.IsValidFolder(p))return;Folder(Path.GetDirectoryName(p).Replace('\\','/'));AssetDatabase.CreateFolder(Path.GetDirectoryName(p).Replace('\\','/'),Path.GetFileName(p));}
    static Material Mat(string name,Shader shader)
    {
        string p=Root+"/Materials/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(p);
        if(!m){m=new Material(shader);AssetDatabase.CreateAsset(m,p);}m.shader=shader;EditorUtility.SetDirty(m);return m;
    }
    static float Height(float x,float z)
    {
        float t=Mathf.SmoothStep(0,1,Mathf.InverseLerp(2,19,z));
        return t*(.90f+.50f*Mathf.Sin(x*.065f+z*.045f))+.13f*Mathf.Sin(x*.23f+z*.13f)*Mathf.SmoothStep(0,1,Mathf.InverseLerp(1,10,z));
    }
    public static object Run()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before authoring.");
        for(int s=0;s<UnityEngine.SceneManagement.SceneManager.sceneCount;s++)
            if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(s).isDirty)throw new InvalidOperationException("Save scenes before authoring.");
        Folder(Root+"/Materials");Folder(Root+"/Meshes");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var importer=(ModelImporter)AssetImporter.GetAtPath(Model);
        importer.globalScale=100;importer.animationType=ModelImporterAnimationType.Generic;
        importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;importer.importAnimation=true;
        importer.importNormals=ModelImporterNormals.Import;importer.importTangents=ModelImporterTangents.None;
        var takes=importer.defaultClipAnimations;
        foreach(var t in takes){t.loopTime=true;t.loopPose=true;t.lockRootRotation=true;t.lockRootHeightY=true;t.lockRootPositionXZ=true;}
        importer.clipAnimations=takes;importer.SaveAndReimport();
        var scene=EditorSceneManager.OpenScene("Assets/InsectSpace/Scenes/HeroCloudStudy.unity",OpenSceneMode.Single);
        EditorSceneManager.SaveScene(scene,Scene);
        UnityEngine.Object.DestroyImmediate(GameObject.Find("Valerya - Mixamo humanoid - breathing idle"));
        var model=AssetDatabase.LoadAssetAtPath<GameObject>(Model);
        var actor=(GameObject)PrefabUtility.InstantiatePrefab(model);
        actor.name="AFK Faye - local reference - original idle";
        var animator=actor.GetComponent<Animator>()??actor.AddComponent<Animator>();
        var clip=AssetDatabase.LoadAllAssetsAtPath(Model).OfType<AnimationClip>().Single(c=>c.name=="pbsc_char_hero_faye@idle");
        string cp=Root+"/FayeIdle.controller";
        var ac=AssetDatabase.LoadAssetAtPath<AnimatorController>(cp)??AnimatorController.CreateAnimatorControllerAtPath(cp);
        var sm=ac.layers[0].stateMachine;var state=sm.states.Select(x=>x.state).FirstOrDefault(x=>x.name=="OriginalIdle")??sm.AddState("OriginalIdle");
        state.motion=clip;sm.defaultState=state;EditorUtility.SetDirty(ac);
        animator.runtimeAnimatorController=ac;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        actor.transform.rotation=Quaternion.Euler(0,180,0);clip.SampleAnimation(actor,.3f);
        var heroShader=Shader.Find("InsectSpace/Painterly Hero");
        var body=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Models/Faye/pbsc_char_hero_faye.png");
        var weapon=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Models/Faye/pbsc_char_hero_faye_weapon.png");
        if(!body||!weapon)throw new InvalidOperationException("Missing exported character textures.");
        foreach(var r in actor.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            r.updateWhenOffscreen=true;
            r.sharedMaterials=r.sharedMaterials.Select(source=>
            {
                string n=source.name;bool invisible=n.Contains("shadow"),glass=n.Contains("glass");
                var m=Mat(n,glass||invisible?Shader.Find("Universal Render Pipeline/Lit"):heroShader);
                m.SetTexture("_BaseMap",n.Contains("weapon")?weapon:body);m.SetColor("_BaseColor",new Color(1,1,1,invisible?0:glass?.24f:1));
                if(glass||invisible)
                {
                    m.SetFloat("_Surface",1);m.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);m.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);
                    m.SetFloat("_ZWrite",0);m.SetFloat("_Cull",0);m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");m.renderQueue=3000;
                    m.SetShaderPassEnabled("ShadowCaster",false);m.SetFloat("_Smoothness",.35f);
                }
                else {m.SetColor("_ShadowTint",C("#A2B6BC"));m.SetFloat("_PaletteShift",0);m.SetFloat("_Softness",n.Contains("skin")?.72f:.35f);}
                return m;
            }).ToArray();
            r.shadowCastingMode=ShadowCastingMode.On;
        }
        var bounds=BoundsOf(actor);
        actor.transform.localScale*=2.25f/bounds.size.y;
        bounds=BoundsOf(actor);actor.transform.position+=new Vector3(.12f-bounds.center.x,-bounds.min.y,-bounds.center.z);
        var framing=UnityEngine.Object.FindAnyObjectByType<HeroStudyFraming>();framing.Configure(Camera.main,actor.transform);framing.SetMode(HeroStudyFraming.ViewMode.Explore);
        var sky=UnityEngine.Object.FindAnyObjectByType<StylizedSkyController>();
        UnityEngine.Object.FindAnyObjectByType<HeroStudyControls>().Configure(sky,animator,"AFK Journey / Lilith Games - local reference");
        AddFoliage(sky);
        AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene,Scene);
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(Scene);Selection.activeGameObject=actor;
        return new {scene=Scene,clip=clip.name,seconds=clip.length,avatar=animator.avatar&&animator.avatar.isValid,bounds=BoundsOf(actor).ToString(),renderers=actor.GetComponentsInChildren<SkinnedMeshRenderer>().Length};
    }
    static Bounds BoundsOf(GameObject actor)
    {
        var bounds=new Bounds();bool first=true;
        foreach(var r in actor.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var mesh=new Mesh();r.BakeMesh(mesh);
            foreach(var p in mesh.vertices){var w=r.transform.TransformPoint(p);if(first){bounds=new Bounds(w,Vector3.zero);first=false;}else bounds.Encapsulate(w);}
            UnityEngine.Object.DestroyImmediate(mesh);
        }
        return bounds;
    }
    static void AddFoliage(StylizedSkyController sky)
    {
        var root=new GameObject("AFK original grass and flower meshes").transform;
        var shader=Shader.Find("InsectSpace/Hero Meadow Atmosphere");
        var grass=Mat("OriginalFieldGrass",shader);grass.SetColor("_BaseColor",Color.white);grass.SetFloat("_VertexColor",1);grass.SetFloat("_Wind",.025f);grass.SetColor("_ShadowTint",C("#598888"));grass.SetFloat("_CloudShadow",.62f);
        var flowers=Mat("OriginalFlowers",shader);flowers.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Foliage/txsc_flower_01.png"));
        flowers.SetColor("_BaseColor",Color.white);flowers.SetFloat("_VertexColor",1);flowers.SetFloat("_Wind",.018f);flowers.SetColor("_ShadowTint",C("#598888"));flowers.SetFloat("_Cutoff",.4f);
        var random=new System.Random(1072026);
        foreach(string name in new[]{"mdsc_grass_desert","mdsc_flower_02"})
        {
            bool flower=name.Contains("flower");
            var d=JsonUtility.FromJson<MeshData>(AssetDatabase.LoadAssetAtPath<TextAsset>(Root+"/Foliage/"+name+".json").text);
            var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var colors=new List<Color>();var triangles=new List<int>();
            for(int i=0;i<(flower?230:1700);i++)
            {
                float x=(float)random.NextDouble()*26-13,z=(float)random.NextDouble()*29-3;
                float path=3.6f+Mathf.Sin(z*.13f)*3+z*.03f;
                if(new Vector2(x-.12f,z).sqrMagnitude<1.1f||(z>-1&&Mathf.Abs(x-path)<1.3f)||Mathf.PerlinNoise(x*.35f+18,z*.35f+8)<.48f)continue;
                float size=(flower?.58f:.43f)*(float)(.7+random.NextDouble()*.6);
                var rotation=Quaternion.Euler(0,(float)random.NextDouble()*360,0);int start=vertices.Count;
                var color=Color.Lerp(C("#719480"),C("#A9B779"),Mathf.PerlinNoise(x*.12f+43,z*.16f+32)).linear;
                for(int v=0;v<d.vertices.Length;v++)
                {
                    vertices.Add(rotation*(d.vertices[v]*size)+new Vector3(x,Height(x,z),z));
                    normals.Add(rotation*d.normals[v]);uv.Add(d.uv[v]);colors.Add(flower?Color.white:color);
                }
                foreach(int t in d.triangles)triangles.Add(start+t);
            }
            var mesh=new Mesh{name=name+"_Combined",indexFormat=IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();
            string mp=Root+"/Meshes/"+name+".asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(mp);
            if(existing){EditorUtility.CopySerialized(mesh,existing);UnityEngine.Object.DestroyImmediate(mesh);mesh=existing;}else AssetDatabase.CreateAsset(mesh,mp);
            var go=new GameObject(name);go.transform.SetParent(root,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=flower?flowers:grass;r.shadowCastingMode=ShadowCastingMode.Off;
        }
        root.gameObject.AddComponent<HeroStudyAtmosphere>().Configure(sky,root.GetComponentsInChildren<Renderer>());
    }
}
