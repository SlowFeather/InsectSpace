using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using InsectSpace.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class BuildAfkReplica
{
    const string Root = "Assets/Temp/AFKStudy/Replica";
    const string ScenePath = Root + "/AFKLakeside.unity";
    static readonly string Evidence = Path.GetFullPath(Path.Combine(Application.dataPath,"../../../../.artifacts/validation/afk-render-replica/2026-10-08"));
    [Serializable] public sealed class Foliage { public Vector3[] vertices, normals; public Vector2[] uv; public int[] triangles; }
    [Serializable] public sealed class Submesh { public int[] indices; }
    [Serializable] public sealed class ExtractedMesh { public string name; public Vector3[] vertices,normals; public Vector2[] uv; public Vector4[] tangents; public Submesh[] submeshes; }
    static void Folder(string p)
    {
        if (AssetDatabase.IsValidFolder(p)) return;
        string parent = Path.GetDirectoryName(p).Replace('\\','/');
        Folder(parent); AssetDatabase.CreateFolder(parent,Path.GetFileName(p));
    }
    static T Save<T>(T asset,string path) where T : Object
    {
        var previous = AssetDatabase.LoadAssetAtPath<T>(path);
        if (previous)
        {
            if(asset is Mesh source && previous is Mesh destination)
            {
                // CopySerialized changes CPU data but can leave a stale render
                // buffer on a mesh already loaded by the source scene.
                destination.Clear();destination.indexFormat=source.indexFormat;
                destination.vertices=source.vertices;destination.normals=source.normals;
                destination.uv=source.uv;destination.colors=source.colors;destination.tangents=source.tangents;
                destination.subMeshCount=source.subMeshCount;
                for(int i=0;i<source.subMeshCount;i++)destination.SetTriangles(source.GetTriangles(i),i);
                destination.bounds=source.bounds;destination.UploadMeshData(false);
            }
            else EditorUtility.CopySerialized(asset,previous);
            EditorUtility.SetDirty(previous);Object.DestroyImmediate(asset);return previous;
        }
        AssetDatabase.CreateAsset(asset,path); return asset;
    }
    static Material Material(string name,Shader shader,Color color)
    {
        var result = Save(new Material(shader) { name = name },Root+"/Materials/"+name+".mat");
        result.SetColor("_BaseColor",color); EditorUtility.SetDirty(result); return result;
    }
    static Mesh Mesh(string name,List<Vector3> positions,List<int> indices,List<Vector2> uv,List<Color> colors=null)
    {
        var mesh = new Mesh { name = name,indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(positions);mesh.SetTriangles(indices,0);mesh.SetUVs(0,uv);
        if (colors != null) mesh.SetColors(colors);
        mesh.RecalculateNormals();mesh.RecalculateBounds();
        return Save(mesh,Root+"/Meshes/"+name+".asset");
    }
    static GameObject Draw(string name,Mesh mesh,Material material,Transform parent,bool shadows=true)
    {
        var go = new GameObject(name);go.transform.SetParent(parent,false);
        go.AddComponent<MeshFilter>().sharedMesh=mesh;
        var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
        renderer.shadowCastingMode=shadows?ShadowCastingMode.On:ShadowCastingMode.Off;
        return go;
    }

    static Mesh OriginalCliff(string name)
    {
        string path="Assets/Temp/AFKStudy/RenderSource/"+name+".json";
        if(!File.Exists(path))throw new FileNotFoundException("Run AgentScripts/ExtractAfkRenderReference.py first.",path);
        var data=Newtonsoft.Json.JsonConvert.DeserializeObject<ExtractedMesh>(File.ReadAllText(path));
        var mesh=new Mesh {name=data.name,vertices=data.vertices,normals=data.normals,uv=data.uv,tangents=data.tangents};
        mesh.subMeshCount=data.submeshes.Length;
        for(int i=0;i<data.submeshes.Length;i++)mesh.SetTriangles(data.submeshes[i].indices,i);
        mesh.RecalculateBounds();
        return Save(mesh,Root+"/Meshes/"+name+".asset");
    }
    static Bounds BoundsOf(GameObject go)
    {
        var renderers=go.GetComponentsInChildren<Renderer>();
        var bounds=renderers[0].bounds;foreach(var r in renderers.Skip(1))bounds.Encapsulate(r.bounds);return bounds;
    }
    static void TintCliff(GameObject go)
    {
        foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
        {
            var material = renderer.sharedMaterial;
            if (!material) continue;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", new Color(.48f, .62f, .68f, 1));
            if (material.HasProperty("_ShadowTint")) material.SetColor("_ShadowTint", new Color(.18f, .32f, .40f, 1));
            if (material.HasProperty("_Rock")) material.SetFloat("_Rock", 1);
            if (material.HasProperty("_RockColor")) material.SetColor("_RockColor", new Color(.30f, .48f, .56f, 1));
            if (material.HasProperty("_MistIntensity")) material.SetFloat("_MistIntensity", .03f);
            EditorUtility.SetDirty(material);
        }
    }
    // A closed, curved inlet; the previous strip had a visible rectangular end.
    static float Shore(float z) => -9.8f+6.5f*Mathf.Sqrt(Mathf.Clamp01(1-Mathf.Pow((z-4.2f)/8.2f,2)));
    static GameObject Prop(Dictionary<string,GameObject> templates,string key,string name,Vector3 position,
        Vector3 scale,Vector3 rotation,Transform parent,Dictionary<Material,Material> materials,Shader shader)
    {
        var go=Object.Instantiate(templates[key],parent);go.name=name;go.SetActive(true);
        go.transform.localPosition=position;go.transform.localScale=scale;go.transform.localRotation=Quaternion.Euler(rotation);
        if(key.Contains("cliff_chapter00"))
            go.GetComponentInChildren<MeshFilter>().sharedMesh=OriginalCliff(key.Contains("01_2")?"mdsc_cliff_chapter00_01_2":"mdsc_cliff_chapter00_01_1");
        foreach(var renderer in go.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled=true;
            renderer.sharedMaterials=renderer.sharedMaterials.Select(source=>
            {
                if(materials.TryGetValue(source,out var material))return material;
                material=Save(new Material(source){name=source.name+" Replica"},Root+"/Materials/"+source.name.Replace('/','_')+".mat");
                material.shader=shader;material.SetColor("_BaseColor",Color.white);
                if (source.name.Contains("cliff_chapter00_01"))
                    material.SetTexture("_NormalMap",RockNormal("CAB-a454c7c16c8bc10810aff3224a9ce133_-9099449694886411752"));
                else if (source.name.Contains("cliff_chapter00_02"))
                    material.SetTexture("_NormalMap",RockNormal("CAB-8c25ae91be0f22d1394b8b17ab021ba0_8397530957177502003"));
                material.SetColor("_ShadowTint",new Color(.14f,.36f,.37f));
                material.SetFloat("_VertexColor",0);material.SetFloat("_Wind",key.Contains("bio")?.025f:0);
                material.SetFloat("_Foliage",key.Contains("tree")||key.Contains("shrub")?1:0);
                // Mountain/lake alpha stores surface data, not opacity. Source
                // materials have _ALPHATEST=0; clipping it removes the walls.
                material.SetFloat("_Cutoff",key.Contains("bio")?.32f:0);
                EditorUtility.SetDirty(material);materials[source]=material;return material;
            }).ToArray();
            renderer.shadowCastingMode=ShadowCastingMode.On;renderer.receiveShadows=true;
        }
        // Ground props use their extracted bounds as a stable contact point.
        // Elevated mesas and trees use the source pivot; re-aligning their
        // bounds here would move the entire highland out of the camera frame.
        if (Mathf.Abs(position.y) < .01f) go.transform.position += Vector3.up*(position.y-BoundsOf(go).min.y);
        return go;
    }

    static Texture2D RockNormal(string id)
    {
        string path="Assets/Temp/AFKStudy/Environment/Source/"+id+".png";
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        // Original GLES programs read the raw linear RG channels directly.
        if(importer.sRGBTexture || importer.textureType!=TextureImporterType.Default)
        {importer.textureType=TextureImporterType.Default;importer.sRGBTexture=false;importer.SaveAndReimport();}
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    public static object Run()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before building.");
        for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
            if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save current scenes first.");
        Folder(Root+"/Materials");Folder(Root+"/Meshes");
        var shader=Shader.Find("InsectSpace/Local Reference Surface");
        var waterShader=Shader.Find("InsectSpace/Local Reference Water");
        if(!shader||!waterShader)throw new InvalidOperationException("Import reference shaders first.");
        var scene=EditorSceneManager.OpenScene("Assets/Temp/AFKStudy/AFKCloudStudy.unity",OpenSceneMode.Single);
        EditorSceneManager.SaveScene(scene,ScenePath);
        var source=GameObject.Find("AFK extracted environment - local study");
        if(!source)throw new InvalidOperationException("BuildAfkEnvironment source scene is required.");
        var templates=source.transform.Cast<Transform>().ToDictionary(t=>t.name,t=>t.gameObject);
        var root=new GameObject("LOCAL AFK lakeside rendering reference");
        var study=root.AddComponent<AfkReferenceStudy>();
        var camera=Camera.main;
        Object.DestroyImmediate(camera.GetComponent<HeroStudyFraming>());
        camera.clearFlags=CameraClearFlags.SolidColor;camera.nearClipPlane=.1f;camera.farClipPlane=150;
        var cameraData=camera.GetUniversalAdditionalCameraData();cameraData.renderPostProcessing=false;
        cameraData.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        var actor=Object.FindAnyObjectByType<Animator>();actor.transform.SetParent(root.transform,true);
        actor.transform.position=new Vector3(.15f,0,0);actor.transform.rotation=Quaternion.Euler(0,160,0);actor.transform.localScale=Vector3.one*1.15f;
        var sun=Object.FindObjectsByType<Light>().Single(l=>l.type==LightType.Directional);
        sun.shadows=LightShadows.Soft;sun.shadowStrength=1;sun.shadowBias=.035f;sun.shadowNormalBias=.15f;
        var materials=new Dictionary<Material,Material>();
        // The extracted cliff meshes are modular mesas.  Overlap them in three
        // staggered tiers so their authored ledges read as one continuous wall
        // from the isometric camera instead of three floating islands.
        var cliffLayout = new[]
        {
            new { key = "pbsc_cliff_chapter00_01_2_hd", name = "Cliff rear high", position = new Vector3(-1.0f, 10f, 16.2f), scale = new Vector3(1.4f, 1.4f, 1.2f), rotation = new Vector3(0, 205, 0) },
            new { key = "pbsc_cliff_chapter00_01_2_hd", name = "Cliff left continuation", position = new Vector3(-7f, 8f, 12.4f), scale = new Vector3(1.25f, 1.2f, 1.2f), rotation = new Vector3(0, 164, 0) },
            new { key = "pbsc_cliff_chapter00_01_2_hd", name = "Cliff tall central face", position = new Vector3(-2.0f, 7.1f, 12f), scale = new Vector3(1.4f, 1.1f, 1.25f), rotation = new Vector3(0, 177, 0) },
            new { key = "pbsc_cliff_chapter00_01_1_hd", name = "Cliff lower right terrace", position = new Vector3(4.3f, 3.8f, 9.8f), scale = new Vector3(1.22f, 1.03f, 1.20f), rotation = new Vector3(0, 197, 0) },
            new { key = "pbsc_cliff_chapter00_01_2_hd", name = "Cliff right continuation", position = new Vector3(8.3f, 6.3f, 13.3f), scale = new Vector3(1.2f, 1.05f, 1.15f), rotation = new Vector3(0, 158, 0) }
        };
        foreach (var item in cliffLayout)
        {
            var cliff = Prop(templates, item.key, item.name, item.position, item.scale, item.rotation, root.transform, materials, shader);
            TintCliff(cliff);
        }
        for(int i=0;i<6;i++)
        {
            float z=3.3f+i*.75f;
            Prop(templates,"pbsc_bio_westfall_reed_01_hd","Shore reeds "+i,new Vector3(Shore(z)-.12f,0,z),Vector3.one*(1.2f+i%3*.18f),new Vector3(0,-30+i*23,0),root.transform,materials,shader);
        }
        if (templates.ContainsKey("pbsc_chapter01_lakebeach_01_hd"))
            Prop(templates,"pbsc_chapter01_lakebeach_01_hd","Extracted lake beach",new Vector3(-5.4f,.02f,4.4f),Vector3.one*1.35f,new Vector3(0,12,0),root.transform,materials,shader);
        var random=new System.Random(8102026);
        var shrubs=new[]{new Vector3(-1.6f,0,6.5f),new Vector3(.8f,0,5.9f),new Vector3(2.5f,0,5.2f),new Vector3(-2.5f,0,2),new Vector3(.3f,0,-2.9f),new Vector3(6,0,4),new Vector3(-2,7.12f,11.7f),new Vector3(5.1f,3.85f,9.4f)};
        for(int i=0;i<shrubs.Length;i++)
        {
            Prop(templates,"pdsc_bio_westfall_shrub_03_hd","Extracted round shrub "+i,shrubs[i],Vector3.one*(.72f+i%3*.12f),new Vector3(35,0,0),root.transform,materials,shader);
        }
        string[] trees={"pbsc_bio_westfall_tree_fir_01_hd","pbsc_bio_westfall_tree_arborvitae_hd","pbsc_bio_westfall_tree_oak_01_hd"};
        var treetops=new[]{new Vector3(-3.2f,7.2f,12.3f),new Vector3(-.7f,7.2f,12.6f),new Vector3(4.6f,3.85f,10.2f),new Vector3(6.5f,6.4f,13.1f),new Vector3(-6.7f,8.1f,12.5f)};
        for(int i=0;i<treetops.Length;i++)
        {
            var tree=Prop(templates,trees[i%3],"Clifftop tree "+i,treetops[i],Vector3.one*(i%3==2?.48f:.70f),new Vector3(25,0,0),root.transform,materials,shader);
            // Tint the warm atlas trees toward the reference's cool grove palette.
            foreach(var r in tree.GetComponentsInChildren<Renderer>())r.sharedMaterial.SetColor("_BaseColor",new Color(.58f,.83f,.75f));
        }
        Prop(templates,"pbsc_building_human_mercenarycamp_01_hd","Camp at right foreground",new Vector3(9.6f,.18f,-6.5f),Vector3.one*.34f,new Vector3(0,-28,0),root.transform,materials,shader);
        foreach(var go in scene.GetRootGameObjects())
            if(go!=root&&go!=camera.gameObject&&go!=sun.gameObject)Object.DestroyImmediate(go);

        var ground=Material("Painted meadow",shader,Color.white);ground.SetFloat("_Ground",1);ground.SetFloat("_Cutoff",0);
        Draw("Painted ground",Mesh("Ground",new List<Vector3>{new Vector3(-45,-.035f,-35),new Vector3(-45,-.035f,45),new Vector3(45,-.035f,45),new Vector3(45,-.035f,-35)},new List<int>{0,1,2,0,2,3},new List<Vector2>{Vector2.zero,Vector2.up,Vector2.one,Vector2.right}),ground,root.transform,false);
        var water=Material("Lake",waterShader,new Color(.22f,.40f,.46f));
        var v=new List<Vector3>();var uv=new List<Vector2>();var tris=new List<int>();
        for(int i=0;i<=72;i++)
        {
            float z=Mathf.Lerp(-4f,12.4f,i/72f);float bank=Shore(z);
            v.Add(new Vector3(-30,.012f,z));v.Add(new Vector3(bank,.012f,z));
            uv.Add(new Vector2(bank+30,z));uv.Add(new Vector2(0,z));
            if(i>0){int s=i*2;tris.AddRange(new[]{s-2,s,s+1,s-2,s+1,s-1});}
        }
        Draw("Lake and narrow shoreline",Mesh("Lake",v,tris,uv),water,root.transform,false);
        BuildFoliage(root.transform,shader,random);
        var particles=new GameObject("Rain - visual only").AddComponent<ParticleSystem>();particles.transform.SetParent(root.transform,false);particles.transform.position=new Vector3(0,14,3);
        var main=particles.main;main.loop=true;main.playOnAwake=false;main.startLifetime=1.3f;main.startSpeed=0;main.startSize=.025f;main.startColor=new Color(.6f,.76f,.82f,.25f);main.maxParticles=1800;main.simulationSpace=ParticleSystemSimulationSpace.World;
        var shape=particles.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(35,4,42);
        var emission=particles.emission;emission.rateOverTime=850;
        var velocity=particles.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.World;velocity.x=-2.5f;velocity.y=-19;velocity.z=0;
        var pr=particles.GetComponent<ParticleSystemRenderer>();pr.renderMode=ParticleSystemRenderMode.Stretch;pr.lengthScale=12;pr.velocityScale=.02f;
        var rainMat=Material("Rain",Shader.Find("Universal Render Pipeline/Particles/Unlit"),Color.white);
        rainMat.SetFloat("_Surface",1);rainMat.SetFloat("_Blend",0);rainMat.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);rainMat.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);rainMat.SetFloat("_ZWrite",0);rainMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");rainMat.renderQueue=3000;pr.sharedMaterial=rainMat;
        particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        study.Configure(camera,sun,actor,particles,water,materials.Values.Concat(new[]{ground}).ToArray());
        study.SetWeather(AfkReferenceStudy.Weather.Day);
        RenderSettings.sun=sun;
        EditorUtility.SetDirty(study);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene,ScenePath);
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        Selection.activeGameObject=root;
        return new {scene=ScenePath,renderers=root.GetComponentsInChildren<Renderer>().Length,source="AFK Journey / Lilith Games - local reference",shaderErrors=ShaderUtil.GetShaderMessages(shader).Select(m=>m.message).ToArray()};
    }

    static void BuildFoliage(Transform root,Shader shader,System.Random random)
    {
        foreach(bool flowers in new[]{false,true})
        {
            var data=JsonUtility.FromJson<Foliage>(File.ReadAllText("Assets/Temp/AFKStudy/Foliage/"+(flowers?"mdsc_flower_02":"mdsc_grass_desert")+".json"));
            var material=Material(flowers?"Yellow field flowers":"Fine field grass",shader,Color.white);
            material.SetFloat("_VertexColor",1);material.SetFloat("_Wind",flowers?.014f:.018f);material.SetFloat("_Cutoff",flowers?.4f:0);
            if(flowers)material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Temp/AFKStudy/Foliage/txsc_flower_01.png"));
            var p=new List<Vector3>();var n=new List<Vector3>();var t=new List<int>();var uv=new List<Vector2>();var colors=new List<Color>();
            float minY=data.vertices.Min(a=>a.y);
            var flowerCenters=new[]{new Vector2(-2.1f,3.1f),new Vector2(.3f,4.2f),new Vector2(3.2f,6),new Vector2(-2.4f,-4),new Vector2(-3,-7),new Vector2(1.5f,-8),new Vector2(0,-12),new Vector2(5,3)};
            for(int i=0;i<(flowers?120:6500);i++)
            {
                float x=(float)random.NextDouble()*43-21.5f,z=(float)random.NextDouble()*45-20;
                if(flowers)
                {
                    var center=flowerCenters[i%flowerCenters.Length];
                    x=center.x+(float)(random.NextDouble()+random.NextDouble()-1)*1.25f;
                    z=center.y+(float)(random.NextDouble()+random.NextDouble()-1)*1.6f;
                }
                if(x<Shore(z)+.35f&&z>-4&&z<12.4f)continue;
                if(z>8.4f&&Mathf.Abs(x)<10)continue;
                if(x*x+z*z<1.05f)continue;
                float patch=Mathf.PerlinNoise(x*.32f+17,z*.29f+8);
                if(!flowers && patch<.36f)continue;
                float size=(flowers?.90f:.30f)*(float)(.75+random.NextDouble()*.5);
                var rotation=Quaternion.Euler(0,(float)random.NextDouble()*360,0);int first=p.Count;
                var color=flowers?Color.white:Color.Lerp(new Color(.32f,.45f,.29f),new Color(.60f,.65f,.37f),patch).linear;
                for(int j=0;j<data.vertices.Length;j++)
                {
                    var point=data.vertices[j]-Vector3.up*(flowers?0:minY);
                    if(!flowers){point.x*=.42f;point.z*=.42f;}
                    p.Add(rotation*(point*size)+new Vector3(x,.02f,z));
                    n.Add(rotation*data.normals[j]);
                    // Source flower quads occupy blue/white atlas rows. Move
                    // each complete quad to the white row without wrapping its
                    // corners independently (which collapses the UV rectangle).
                    uv.Add(flowers?new Vector2(data.uv[j].x,data.uv[j].y-data.uv[(j/4)*4].y+.27f):data.uv[j]);colors.Add(color);
                }
                foreach(int index in data.triangles)t.Add(first+index);
            }
            var mesh=Mesh(flowers?"Flower patches":"Fine grass",p,t,uv,colors);mesh.SetNormals(n);EditorUtility.SetDirty(mesh);
            Draw(mesh.name,mesh,material,root,flowers);
        }
    }

    public static object VerifyAndCapture()
    {
        if(!Application.isPlaying)throw new InvalidOperationException("Run lakeside scene in Play.");
        var study=Object.FindAnyObjectByType<AfkReferenceStudy>();if(!study)throw new InvalidOperationException("Wrong scene.");
        var actor=study.Actor;var camera=study.View;
        if(!actor.avatar||!actor.avatar.isValid||actor.applyRootMotion)throw new InvalidOperationException("Invalid original rig.");
        foreach(var r in study.GetComponentsInChildren<Renderer>())
            foreach(var m in r.sharedMaterials)
                if(!m||ShaderUtil.ShaderHasError(m.shader))throw new InvalidOperationException("Missing or broken material on "+r.name);
        var skin=actor.GetComponentsInChildren<SkinnedMeshRenderer>().First();var baked=new Mesh();
        float speed=actor.speed,aspect=camera.aspect;var weather=study.CurrentWeather;var state=actor.GetCurrentAnimatorStateInfo(0);var position=actor.transform.position;
        var captures=new List<object>();float deformation=0;
        Directory.CreateDirectory(Evidence);
        try
        {
            actor.speed=0;actor.Play("OriginalIdle",0,.1f);actor.Update(0);skin.BakeMesh(baked);var before=baked.vertices;
            actor.Play("OriginalIdle",0,.7f);actor.Update(0);skin.BakeMesh(baked);var after=baked.vertices;
            for(int i=0;i<before.Length;i++)deformation=Mathf.Max(deformation,(after[i]-before[i]).sqrMagnitude);
            if(deformation<.0000001f||Vector3.Distance(position,actor.transform.position)>.0001f)throw new InvalidOperationException("Original idle did not deform in place.");
            foreach(var preset in new[]{AfkReferenceStudy.Weather.Day,AfkReferenceStudy.Weather.RainNight})
                foreach(var size in new[]{new Vector2Int(1600,900),new Vector2Int(1080,1920)})
                {
                    camera.aspect=(float)size.x/size.y;study.SetWeather(preset);study.ApplyFraming();
                    var rain=study.GetComponentInChildren<ParticleSystem>();if(preset==AfkReferenceStudy.Weather.RainNight)rain.Simulate(.8f,true,true,true);
                    foreach(var s in actor.GetComponentsInChildren<SkinnedMeshRenderer>())
                    {
                        if(s.sharedMaterials.All(m=>m.GetColor("_BaseColor").a==0))continue;
                        s.BakeMesh(baked);
                        foreach(var p in baked.vertices)
                        {
                            var screen=camera.WorldToViewportPoint(s.transform.TransformPoint(p));
                            if(screen.z<camera.nearClipPlane||screen.x<.02f||screen.x>.98f||screen.y<.03f||screen.y>.97f)throw new InvalidOperationException("Character clipped.");
                        }
                    }
                    string path=Path.Combine(Evidence,preset+"-"+(size.x>size.y?"Desktop":"Portrait")+".png");
                    var oldTarget=camera.targetTexture;var oldActive=RenderTexture.active;
                    var rt=RenderTexture.GetTemporary(size.x,size.y,24,RenderTextureFormat.ARGB32);
                    var image=new Texture2D(size.x,size.y,TextureFormat.RGB24,false);
                    try
                    {
                        camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
                        image.ReadPixels(new Rect(0,0,size.x,size.y),0,0);image.Apply();
                        var pixels=image.GetPixels32();var colors=new HashSet<int>();int magenta=0;
                        for(int i=0;i<pixels.Length;i+=23){var c=pixels[i];colors.Add((c.r/8<<10)+(c.g/8<<5)+c.b/8);if(c.r>240&&c.b>240&&c.g<20)magenta++;}
                        if(colors.Count<100||magenta>30)throw new InvalidOperationException("Blank or shader-error frame.");
                        File.WriteAllBytes(path,image.EncodeToPNG());
                        captures.Add(new {preset=preset.ToString(),width=size.x,height=size.y,quantizedColors=colors.Count,magentaSamples=magenta,path=Path.GetFullPath(path)});
                    }
                    finally{camera.targetTexture=oldTarget;RenderTexture.active=oldActive;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(image);}
                }
        }
        finally
        {
            Object.DestroyImmediate(baked);actor.Play(state.fullPathHash,0,state.normalizedTime);actor.Update(0);actor.speed=speed;
            camera.aspect=aspect;study.SetWeather(weather);study.ApplyFraming();
        }
        var result=new {unity=Application.unityVersion,scene=ScenePath,avatar=actor.avatar.isValid,originalIdleSeconds=actor.runtimeAnimatorController.animationClips.Single().length,deformationSquared=deformation,stationaryRoot=true,captures};
        File.WriteAllText(Path.Combine(Evidence,"render-probe.json"),Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented));
        return result;
    }
}
