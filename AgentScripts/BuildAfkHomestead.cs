using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using InsectSpace.Rendering;

// Rebuilds the local reference's streamed map without writing Unity YAML.
public static class BuildAfkHomestead
{
    const string Root = "Assets/Temp/AFKStudy/Homestead";
    const string Source = Root + "/Source";
    const string Generated = Root + "/Generated";
    const string ScenePath = Root + "/AFKHomestead.unity";
    const string Advanced = "Assets/Temp/AFKStudy/Advanced";
    static JObject Read(string path) => JObject.Parse(File.ReadAllText(path));
    static float F(JToken t, string name, float fallback = 0) => (float?)t?[name] ?? fallback;
    static Vector3 V(JToken t) => new Vector3(F(t,"x"), F(t,"y"), F(t,"z"));
    static Color C(JToken t) => new Color(F(t,"r"), F(t,"g"), F(t,"b"), F(t,"a",1));
    static void Folder(string path)
    {
        Directory.CreateDirectory(path);
    }
    static T Store<T>(T item, string path) where T : UnityEngine.Object
    {
        var old = AssetDatabase.LoadAssetAtPath<T>(path);
        if (old) {
            if (item is Mesh source && old is Mesh destination) {
                // Updating a loaded mesh must also invalidate its GPU buffers.
                destination.Clear(); destination.indexFormat=source.indexFormat;
                destination.vertices=source.vertices; destination.normals=source.normals;
                destination.colors=source.colors; destination.tangents=source.tangents;
                for (int channel=0;channel<8;channel++) {
                    var uv=new List<Vector4>(); source.GetUVs(channel,uv); destination.SetUVs(channel,uv);
                }
                destination.subMeshCount=source.subMeshCount;
                for(int i=0;i<source.subMeshCount;i++) destination.SetIndices(source.GetIndices(i),source.GetTopology(i),i);
                destination.bounds=source.bounds; destination.UploadMeshData(false);
            } else EditorUtility.CopySerialized(item,old);
            UnityEngine.Object.DestroyImmediate(item); EditorUtility.SetDirty(old); return old;
        }
        AssetDatabase.CreateAsset(item,path); return item;
    }
    static Matrix4x4 Matrix(JToken values, bool columnMajor)
    {
        var m = new Matrix4x4();
        for (int r=0;r<4;r++) for (int c=0;c<4;c++) m[r,c]=(float)values[columnMajor ? c*4+r : r*4+c];
        return m;
    }
    static void TransformMatrix(Transform t, Matrix4x4 m)
    {
        var scale = new Vector3(m.GetColumn(0).magnitude,m.GetColumn(1).magnitude,m.GetColumn(2).magnitude);
        if (m.determinant < 0) scale.x = -scale.x;
        t.localPosition=m.GetColumn(3); t.localScale=scale;
        t.localRotation=Quaternion.LookRotation(m.GetColumn(2)/scale.z,m.GetColumn(1)/scale.y);
    }
    static Mesh ImportMesh(string id, string sourceFolder = Source + "/Prefabs")
    {
        var d=Read(sourceFolder+"/"+id+".json");
        string path=Generated+"/Meshes/"+id+".asset";
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (!mesh) { mesh=new Mesh(); AssetDatabase.CreateAsset(mesh,path); }
        mesh.Clear(); mesh.name=(string)d["name"]; mesh.indexFormat=IndexFormat.UInt32;
        mesh.SetVertices(d["vertices"].Select(V).ToList());
        if (d["normals"].Count()==mesh.vertexCount) mesh.SetNormals(d["normals"].Select(V).ToList());
        if (d["uv"].Count()==mesh.vertexCount) mesh.SetUVs(0,d["uv"].Select(t=>new Vector2(F(t,"x"),F(t,"y"))).ToList());
        if (d["colors"].Count()==mesh.vertexCount) mesh.SetColors(d["colors"].Select(C).ToList());
        mesh.subMeshCount=d["submeshes"].Count();
        int sub=0; foreach(var s in d["submeshes"]) mesh.SetTriangles(s["indices"].Select(t=>(int)t).ToArray(),sub++);
        if (d["normals"].Count()!=mesh.vertexCount) mesh.RecalculateNormals();
        if (d["tangents"]?.Count()==mesh.vertexCount) mesh.SetTangents(d["tangents"].Select(t=>new Vector4(F(t,"x"),F(t,"y"),F(t,"z"),F(t,"w"))).ToList());
        else if (mesh.uv.Length==mesh.vertexCount) mesh.RecalculateTangents();
        mesh.RecalculateBounds(); mesh.UploadMeshData(false); EditorUtility.SetDirty(mesh); return mesh;
    }
    static Texture2D Texture(string id, string folder) => string.IsNullOrEmpty(id)?null:AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"/"+id+".png");
    static void ImportFloat(Material material, string property, float value)
    {
        int index=material.shader.FindPropertyIndex(property);
        if(index<0)return;
        var type=material.shader.GetPropertyType(index);
        if(type==ShaderPropertyType.Float||type==ShaderPropertyType.Range)material.SetFloat(property,value);
    }
    static void ImportColor(Material material, string property, JToken value)
    {
        int index=material.shader.FindPropertyIndex(property);
        if(index<0)return;
        var type=material.shader.GetPropertyType(index);
        if(type==ShaderPropertyType.Color)material.SetColor(property,C(value));
        else if(type==ShaderPropertyType.Vector)material.SetVector(property,C(value));
    }
    static Material ImportMaterial(JToken d, Shader shader, string sourceFolder = Source + "/Prefabs")
    {
        string id=(string)d["id"], type=(string)d["shader"];
        var mat=AssetDatabase.LoadAssetAtPath<Material>(Generated+"/Materials/"+id+".mat");
        if (!mat) {mat=new Material(shader);AssetDatabase.CreateAsset(mat,Generated+"/Materials/"+id+".mat");}
        mat.shader=shader; mat.name=(string)d["name"]; mat.enableInstancing=true;
        foreach(var p in d["floats"]) {
            string prop=(string)p["property"];
            if(prop=="_AoIntensity")prop="_AOIntensity";
            ImportFloat(mat,prop,(float)p["value"]);
        }
        foreach(var p in d["colors"]) ImportColor(mat,(string)p["property"],p["value"]);
        foreach(var p in d["textures"]) {
            string prop=(string)p["property"];
            if (prop=="_NormalTex") prop="_NormalMap";
            if (!mat.HasProperty(prop) || string.IsNullOrEmpty((string)p["id"])) continue;
            int index=shader.FindPropertyIndex(prop);
            if(shader.GetPropertyType(index)!=ShaderPropertyType.Texture||shader.GetPropertyTextureDimension(index)!=TextureDimension.Tex2D)continue;
            mat.SetTexture(prop,Texture((string)p["id"],sourceFolder));
            mat.SetTextureScale(prop,new Vector2(F(p["scale"],"x",1),F(p["scale"],"y",1)));
            mat.SetTextureOffset(prop,new Vector2(F(p["offset"],"x"),F(p["offset"],"y")));
        }
        if(!mat.HasProperty("_Kind")) {EditorUtility.SetDirty(mat);return mat;}
        bool foliage=type.Contains("Foliage"), grass=type.Contains("Grass"), terrain=type.Contains("Terrain"), rock=type.Contains("Mountain");
        mat.SetFloat("_Cull",rock?F(d["floats"].FirstOrDefault(t=>(string)t["property"]=="_Cull"),"value",2):0);
        mat.SetFloat("_Kind",terrain?1:rock?2:foliage?3:grass?4:0);
        bool detail=d["textures"].Any(t=>(string)t["property"]=="_DetailTex"&&!string.IsNullOrEmpty((string)t["id"]));
        mat.SetFloat("_HasDetail",detail?1:0);
        mat.SetFloat("_AlphaTest",foliage||detail||type.Contains("Clip")||F(d["floats"].FirstOrDefault(t=>(string)t["property"]=="_TYPE_FLOWER"),"value")>.5f?1:0);
        if (foliage||detail) mat.SetFloat("_Cutoff",F(d["floats"].FirstOrDefault(t=>(string)t["property"]=="_ColorCutoff"),"value",.5f));
        mat.SetFloat("_HasNormal",d["textures"].Any(t=>((string)t["property"]).Contains("Normal")&&!string.IsNullOrEmpty((string)t["id"]))?1:0);
        mat.SetTexture("_WorldMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Source+"/VT/homestead_01/"+(rock?"small_diffuse":"diffuse")+"-world.png"));
        mat.SetVector("_WorldMapRect",new Vector4(0,0,408,544));
        mat.SetFloat("_Wind",grass?.025f:0);
        EditorUtility.SetDirty(mat); return mat;
    }
    static Dictionary<string,GameObject> ImportTemplates(string sourceFolder, Transform parent)
    {
        var report=Read(sourceFolder+"/prefabs.json");
        var normalIds=new HashSet<string>(report["materials"].SelectMany(m=>m["textures"])
            .Where(t=>((string)t["property"]).Contains("Normal")).Select(t=>(string)t["id"]));
        foreach(var texture in report["textures"]) {
            string path=sourceFolder+"/"+(string)texture["id"]+".png";
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            bool linear=normalIds.Contains((string)texture["id"]);
            importer.textureType=TextureImporterType.Default;importer.sRGBTexture=(bool?)texture["sRGB"]??!linear;
            importer.alphaIsTransparency=false;importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.maxTextureSize=4096;importer.SaveAndReimport();
        }
        var shader=Shader.Find("InsectSpace/Local Homestead Surface");
        var meshes=report["meshes"].ToDictionary(t=>(string)t["id"],t=>ImportMesh((string)t["id"],sourceFolder));
        var materials=report["materials"].ToDictionary(t=>(string)t["id"],t=>ImportMaterial(t,shader,sourceFolder));
        var nodes=report["nodes"].ToDictionary(t=>(string)t["id"],t=>new GameObject((string)t["name"]));
        foreach(var d in report["nodes"]) {
            var go=nodes[(string)d["id"]];string p=(string)d["parent"];
            go.transform.SetParent(!string.IsNullOrEmpty(p)&&nodes.ContainsKey(p)?nodes[p].transform:parent,false);
            if(d["matrix"]!=null)TransformMatrix(go.transform,Matrix(d["matrix"],false));
            else {go.transform.localPosition=V(d["position"]);go.transform.localScale=V(d["scale"]);
                var q=d["rotation"];go.transform.localRotation=new Quaternion(F(q,"x"),F(q,"y"),F(q,"z"),F(q,"w",1));}
            if(meshes.TryGetValue((string)d["mesh"]??"",out var mesh)&&d["materials"].Count()>0) {
                go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();
                r.sharedMaterials=d["materials"].Select(t=>materials.TryGetValue((string)t,out var m)?m:null).ToArray();
                r.enabled=((bool?)d["enabled"]??true)&&r.sharedMaterials.All(m=>m);
                r.shadowCastingMode=(bool?)d["shadow"]==false?ShadowCastingMode.Off:ShadowCastingMode.On;
            }
            go.SetActive((bool?)d["active"]??true);
        }
        return nodes.Values.Where(g=>g.transform.parent==parent).ToDictionary(g=>g.name,StringComparer.OrdinalIgnoreCase);
    }
    public static object BuildLibrary()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play first.");
        Folder(Generated+"/Prefabs");Folder(Generated+"/Meshes");Folder(Generated+"/Materials");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var root=new GameObject("Temporary prefab import");int count=0;
        try {
            foreach(string folder in new[]{Source+"/Prefabs",Root+"/Supplement",Root+"/Buildings",Root+"/Monument",Root+"/FlowerVariants",Root+"/PlayerTemplate",Root+"/FlowerCandidates"}) {
                var templates=ImportTemplates(folder,root.transform);
                foreach(var item in templates) {
                    if(!item.Key.StartsWith("pb")&&!item.Key.StartsWith("pd")&&!item.Key.StartsWith("hex_"))continue;
                    PrefabUtility.SaveAsPrefabAsset(item.Value,Generated+"/Prefabs/"+item.Key+".prefab");count++;
                }
                foreach(Transform child in root.transform.Cast<Transform>().ToArray())UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
        } finally {UnityEngine.Object.DestroyImmediate(root);}
        // Streamed LOD0 must override the resident LOD2 templates on every rebuild.
        ImportCliffRecovery();
        AssetDatabase.SaveAssets();return new {prefabs=count,streamedCliffs=6};
    }
    public static object ImportCliffRecovery()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play first.");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var root=new GameObject("Temporary streamed cliff import");int count=0;
        try {
            foreach(var item in ImportTemplates(Root+"/CliffRecovery",root.transform)) {
                if(!item.Key.StartsWith("pbsc_cliff_homesteadbroadleaf_"))continue;
                PrefabUtility.SaveAsPrefabAsset(item.Value,Generated+"/Prefabs/"+item.Key+".prefab");count++;
            }
        } finally {UnityEngine.Object.DestroyImmediate(root);}
        if(count!=6)throw new InvalidOperationException("Expected six streamed cliff prefabs, found "+count);
        AssetDatabase.SaveAssets();return new {prefabs=count};
    }
    public static object PreviewCliffs()
    {
        string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../../.artifacts/reference/afk-journey/prop-review"));Directory.CreateDirectory(folder);
        var world=GameObject.Find("LOCAL recovered assets - composed reference view")??GameObject.Find("Homestead original streamed layout");if(world)world.SetActive(false);
        var camera=Camera.main;var p=camera.transform.position;var q=camera.transform.rotation;float size=camera.orthographicSize,aspect=camera.aspect;
        var results=new List<object>();
        try {
            var paths=Directory.GetFiles(Generated+"/Prefabs","*.prefab").Where(p=>new[]{"mainbuilding_01","mainbuilding_new_1","afkidle_homestead"}.Any(p.Contains)).ToArray();
            foreach(string path in paths) {
                var go=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                go.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);go.SetActive(true);
                var renderers=go.GetComponentsInChildren<Renderer>().Where(r=>r.enabled).ToArray();
                if(renderers.Length==0){UnityEngine.Object.DestroyImmediate(go);continue;}
                var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                for(int angle=0;angle<1;angle++) {
                    var rotation=Quaternion.Euler(35,angle*180,0);camera.transform.SetPositionAndRotation(bounds.center-rotation*Vector3.forward*50,rotation);
                    camera.orthographicSize=Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z)*.62f;camera.aspect=1;
                    Capture(camera,Path.Combine(folder,go.name+"-"+angle+".png"),512,512);
                }
                results.Add(new {name=go.name,center=bounds.center.ToString(),size=bounds.size.ToString()});UnityEngine.Object.DestroyImmediate(go);
            }
        } finally {if(world)world.SetActive(true);camera.transform.SetPositionAndRotation(p,q);camera.orthographicSize=size;camera.aspect=aspect;}
        return results;
    }
    public static object ImportMonument()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var root=new GameObject("Temporary monument import");
        try {foreach(var item in ImportTemplates(Root+"/Buildings",root.transform))
            if(item.Key.StartsWith("pbsc_"))PrefabUtility.SaveAsPrefabAsset(item.Value,Generated+"/Prefabs/"+item.Key+".prefab");}
        finally {UnityEngine.Object.DestroyImmediate(root);}
        AssetDatabase.SaveAssets();return true;
    }
    public static object ImportFlowers()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play first.");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var root=new GameObject("Temporary flower import");int count=0;
        try {foreach(string folder in new[]{Root+"/FlowerVariants",Root+"/PlayerTemplate",Root+"/FlowerCandidates"}) {
            foreach(var item in ImportTemplates(folder,root.transform)) {
                PrefabUtility.SaveAsPrefabAsset(item.Value,Generated+"/Prefabs/"+item.Key+".prefab");count++;
            }
        }} finally {UnityEngine.Object.DestroyImmediate(root);}
        AssetDatabase.SaveAssets();return new{prefabs=count};
    }
    public static object PreviewFlowers()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play first.");
        string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../../.artifacts/validation/afk-recovered/2026-10-09/flower-review"));
        Directory.CreateDirectory(folder);
        var study=UnityEngine.Object.FindAnyObjectByType<AfkRecoveredStudy>();
        var camera=study.View;var position=camera.transform.position;var rotation=camera.transform.rotation;
        float size=camera.orthographicSize,aspect=camera.aspect,time=study.Environment.TimeOfDay;
        bool active=study.gameObject.activeSelf,asyncCompilation=ShaderUtil.allowAsyncCompilation;
        var paths=new List<string>();GameObject temporary=null;
        try {
            ShaderUtil.allowAsyncCompilation=false;study.gameObject.SetActive(false);
            study.SetNight(false);
            foreach(string name in new[]{"pbsc_bio_chapter03_dandelion_01_hd","pbsc_bio_chapter03_dandelion_02_hd","pbsc_bio_ep05_dandelion_01_hd","pbsc_bio_ep05_dandelion_02_hd","pbsc_flower_02_hd","pbsc_flower_03_hd","pbsc_flower_06_hd","pbsc_flower_09_hd","pbsc_bio_chapter02_meadowflowers_01_hd","pbsc_bio_chapter02_meadowflowers_02_hd","pbsc_bio_chapter02_meadowflowers_03_hd","pbsc_bio_ep02_flower_02_hd"}) {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Generated+"/Prefabs/"+name+".prefab");
                if(!prefab)continue;
                temporary=(GameObject)PrefabUtility.InstantiatePrefab(prefab);temporary.SetActive(true);
                temporary.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                var renderers=temporary.GetComponentsInChildren<Renderer>().Where(r=>r.enabled).ToArray();
                if(renderers.Length==0){UnityEngine.Object.DestroyImmediate(temporary);continue;}
                var bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
                float verticalOffset=renderers.SelectMany(r=>r.sharedMaterials).Where(m=>m&&m.HasProperty("_OffsetY")).Select(m=>m.GetFloat("_OffsetY")).DefaultIfEmpty(0).Max();
                bounds.center+=Vector3.up*verticalOffset;
                var view=Quaternion.Euler(38,0,0);camera.transform.SetPositionAndRotation(bounds.center-view*Vector3.forward*30,view);
                camera.orthographicSize=Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z)*.7f;camera.aspect=1;
                string path=Path.Combine(folder,name+".png");Capture(camera,path,512,512);paths.Add(path);
                UnityEngine.Object.DestroyImmediate(temporary);temporary=null;
            }
        } finally {
            if(temporary)UnityEngine.Object.DestroyImmediate(temporary);
            study.gameObject.SetActive(active);study.Environment.TimeOfDay=time;study.Environment.Apply();study.ApplyActorLight();
            camera.transform.SetPositionAndRotation(position,rotation);camera.orthographicSize=size;camera.aspect=aspect;
            ShaderUtil.allowAsyncCompilation=asyncCompilation;
        }
        return new{paths};
    }
    public static object ProbeShadows()
    {
        var ground=GameObject.Find("Meadow").GetComponent<Renderer>();var old=ground.sharedMaterial;
        var lit=new Material(Shader.Find("Universal Render Pipeline/Lit"));lit.color=new Color(.4f,.5f,.3f);
        var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.transform.position=new Vector3(2,1,0);cube.transform.localScale=new Vector3(2,2,2);
        string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../../.artifacts/reference/afk-journey"));
        try {Capture(Camera.main,folder+"/shadow-custom.png",540,960);ground.sharedMaterial=lit;Capture(Camera.main,folder+"/shadow-lit.png",540,960);}
        finally {ground.sharedMaterial=old;UnityEngine.Object.DestroyImmediate(lit);UnityEngine.Object.DestroyImmediate(cube);}
        return UnityEngine.Shader.enabledGlobalKeywords.Select(k=>k.name).Where(n=>n.Contains("SHADOW")).ToArray();
    }
    static void Capture(Camera camera,string path,int width,int height)
    {
        var rt=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32);
        var previous=camera.targetTexture;var active=RenderTexture.active;float aspect=camera.aspect;
        bool asyncCompilation=ShaderUtil.allowAsyncCompilation;
        var image=new Texture2D(width,height,TextureFormat.RGB24,false);
        try {ShaderUtil.allowAsyncCompilation=false;camera.aspect=(float)width/height;camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
        finally {ShaderUtil.allowAsyncCompilation=asyncCompilation;camera.targetTexture=previous;camera.aspect=aspect;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(image);}
    }
    const string StudyScene = Root + "/AFKRecoveredLakeside.unity";
    static GameObject Place(string key,Vector3 position,Vector3 scale,float yaw,Transform parent)
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Generated+"/Prefabs/"+key+".prefab");
        if(!prefab)throw new InvalidOperationException("Missing extracted prefab "+key);
        var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab);go.transform.SetParent(parent,false);
        go.transform.SetPositionAndRotation(position,Quaternion.Euler(0,yaw,0));go.transform.localScale=scale;go.SetActive(true);return go;
    }
    static void PlaceStudyCliffs(Transform parent)
    {
        var cliffRoot=new GameObject("Cliffs - original dimensions and hex seams").transform;
        cliffRoot.SetParent(parent,false);
        int recovered=0;
        // Recover two source contours, translated as groups without altering mesh scale.
        foreach(var record in Read(Source+"/layout.json")["instances"]) {
            string key=Path.GetFileNameWithoutExtension((string)record["key"]);
            if(!key.StartsWith("pbsc_cliff_homesteadbroadleaf_"))continue;
            var matrix=Matrix(record["matrix"],true);var p=(Vector3)matrix.GetColumn(3);
            if(Mathf.Abs(p.y-7.048166f)>.001f)continue;
            bool high=key.EndsWith("_2_hd")&&p.x>=188.99f&&p.x<=198.01f&&p.z>=100&&p.z<=110;
            bool low=key.EndsWith("_1_hd")&&p.x>=212.99f&&p.x<=255.01f&&p.z>=94&&p.z<=110;
            if(!high&&!low)continue;
            var offset=high?new Vector3(197,0,95):new Vector3(209,3.524083f,95);
            matrix.SetColumn(3,new Vector4(p.x-offset.x,p.y-offset.y,p.z-offset.z,1));
            var go=Place(key,Vector3.zero,Vector3.one,0,cliffRoot);TransformMatrix(go.transform,matrix);recovered++;
        }
        if(recovered!=18)throw new InvalidOperationException("Expected eighteen source cliff modules, found "+recovered);
        // Recover the actual upper terrain too: cliff modules contain the lip,
        // not the whole plateau behind it. Trees must have a surface under them.
        foreach(var record in Read(Source+"/layout.json")["instances"]) {
            if((string)record["kind"]!="Terrain")continue;
            var matrix=Matrix(record["matrix"],true);var p=(Vector3)matrix.GetColumn(3);
            if(Mathf.Abs(p.y-7.048166f)>.001f||p.z>130.01f)continue;
            bool high=p.x>=188.99f&&p.x<=198.01f&&p.z>=106;
            // Keep the adjacent contour and its top beyond the wide camera's right edge.
            bool low=p.x>=212.99f&&p.x<=255.01f&&p.z>=94;
            if(!high&&!low)continue;
            var offset=high?new Vector3(197,0,95):new Vector3(209,3.524083f,95);
            matrix.SetColumn(3,new Vector4(p.x-offset.x,p.y-offset.y,p.z-offset.z,1));
            var go=Place(Path.GetFileNameWithoutExtension((string)record["key"]),Vector3.zero,Vector3.one,0,cliffRoot);
            TransformMatrix(go.transform,matrix);
            // Open terrain tiles have no side walls; the recovered cliffs cast the plateau shadow.
            foreach(var renderer in go.GetComponentsInChildren<MeshRenderer>())renderer.shadowCastingMode=ShadowCastingMode.Off;
        }
    }
    static float Bank(float z) {
        float contour=Mathf.Sqrt(Mathf.Max(0,1-Mathf.Pow((z-4.8f)/7.8f,2)));
        return -8+contour*(5.9f+.24f*Mathf.Sin(z*1.55f)+.1f*Mathf.Sin(z*3.7f+.8f));
    }
    static float FarBank(float z) => -8-18f*Mathf.Sqrt(Mathf.Max(0,1-Mathf.Pow((z-4.8f)/7.8f,2)));
    static float ShoreDistance(Vector2 point,List<Vector2> shore)
    {
        float nearest=float.MaxValue;
        for(int i=0;i<shore.Count;i++) {
            var a=shore[i];var edge=shore[(i+1)%shore.Count]-a;
            float t=Mathf.Clamp01(Vector2.Dot(point-a,edge)/Mathf.Max(edge.sqrMagnitude,.000001f));
            nearest=Mathf.Min(nearest,(point-a-edge*t).sqrMagnitude);
        }
        return Mathf.Sqrt(nearest);
    }
    static void DrawStudyMesh(string name,List<Vector3> vertices,List<int> indices,List<Color> colors,Material material,Transform parent)
    {
        var mesh=new Mesh {name=name,indexFormat=IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);
        if(colors!=null)mesh.SetColors(colors);mesh.RecalculateNormals();mesh.RecalculateBounds();
        mesh=Store(mesh,Generated+"/Meshes/Study-"+name+".asset");
        var go=new GameObject(name);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
        var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=material;r.shadowCastingMode=ShadowCastingMode.Off;
    }
    static void FrameMapCamera(Camera camera)
    {
        camera.orthographic=true;camera.orthographicSize=12.6f;camera.nearClipPlane=.1f;camera.farClipPlane=600;
        var rotation=Quaternion.Euler(45,0,0);
        camera.transform.SetPositionAndRotation(new Vector3(43,0,140)-rotation*Vector3.forward*64,rotation);
    }
    public static object FrameFullMap()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before framing the full map.");
        var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
        FrameMapCamera(Camera.main);
        UnityEngine.Object.FindAnyObjectByType<AfkHomesteadEnvironment>().Apply();
        EditorSceneManager.SaveScene(scene);
        string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../../.artifacts/validation/afk-recovered/2026-10-08/FullMap-Portrait.png"));
        Capture(Camera.main,path,1080,1920);
        return new {scene=ScenePath,screenshot=path};
    }
    public static object BuildStudy()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before building.");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        foreach(string name in new[]{"InsectSpace/Local Homestead Surface","InsectSpace/Local Homestead Water","InsectSpace/Local AFK Character","InsectSpace/Local AFK Volumetric Fog","InsectSpace/Local Fairy Glow"}) {
            var shader=Shader.Find(name);
            if(!shader||ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("Shader failed: "+name);
        }
        var original=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
        var light=UnityEngine.Object.FindAnyObjectByType<AfkHomesteadEnvironment>();
        var sourceLight=UnityEngine.Object.Instantiate(light.gameObject);
        var profile=UnityEngine.Object.FindAnyObjectByType<Volume>().sharedProfile;
        var actorScene=EditorSceneManager.OpenScene("Assets/Temp/AFKStudy/Replica/AFKLakeside.unity",OpenSceneMode.Additive);
        var actorSource=actorScene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Animator>()).First();
        var actor=UnityEngine.Object.Instantiate(actorSource.gameObject);actor.name="AFK Faye original rig and idle";
        var campSource=actorScene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>()).First(t=>t.name=="Camp at right foreground").gameObject;
        var camp=UnityEngine.Object.Instantiate(campSource);
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(sourceLight,scene);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(actor,scene);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camp,scene);
        EditorSceneManager.CloseScene(actorScene,true);EditorSceneManager.CloseScene(original,true);
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
        var root=new GameObject("LOCAL recovered assets - composed reference view");
        var study=root.AddComponent<AfkRecoveredStudy>();
        var camera=new GameObject("Reference Camera").AddComponent<Camera>();camera.tag="MainCamera";
        camera.nearClipPlane=.1f;camera.farClipPlane=150;camera.allowHDR=true;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.38f,.48f,.45f);
        var cameraData=camera.gameObject.AddComponent<UniversalAdditionalCameraData>();cameraData.renderPostProcessing=true;
        cameraData.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        var volume=new GameObject("Recovered LUT and bloom").AddComponent<Volume>();volume.isGlobal=true;volume.sharedProfile=profile;
        ImportAdvancedInputs();
        var environment=sourceLight.GetComponent<AfkHomesteadEnvironment>();
        ConfigureEnvironment(environment.Sun,profile);
        environment.TimeOfDay=.5f;environment.ShotRotationOffset=new Vector3(0,-40,0);environment.ShotDayYawOffset=60;
        environment.ShotAmbientStrength=.88f;environment.Apply();
        actor.transform.SetParent(root.transform);actor.transform.SetPositionAndRotation(new Vector3(.1f,0,0),Quaternion.Euler(0,160,0));actor.transform.localScale=Vector3.one*1.15f;
        ConfigureCharacter(actor);
        var actorLight=new GameObject("Fairy companion - warm light").AddComponent<Light>();
        actorLight.transform.SetParent(actor.transform,false);actorLight.transform.localPosition=new Vector3(-.85f,1.65f,.4f);
        actorLight.type=LightType.Point;actorLight.color=new Color(1,.82f,.52f);actorLight.intensity=8;actorLight.range=8.5f;
        actorLight.shadows=LightShadows.None;actorLight.enabled=false;study.ActorLight=actorLight;
        var fairy=GameObject.CreatePrimitive(PrimitiveType.Quad);fairy.name="LOCAL fairy glow";
        UnityEngine.Object.DestroyImmediate(fairy.GetComponent<Collider>());
        fairy.transform.SetParent(actorLight.transform,false);fairy.transform.localScale=Vector3.one*.65f;
        var fairyRenderer=fairy.GetComponent<MeshRenderer>();fairyRenderer.shadowCastingMode=ShadowCastingMode.Off;
        fairyRenderer.receiveShadows=false;
        fairyRenderer.sharedMaterial=Store(new Material(Shader.Find("InsectSpace/Local Fairy Glow")){name="LOCAL fairy glow"},Generated+"/Materials/StudyFairy.mat");
        study.FairyVisual=fairy;study.FairyVisible=true;study.FairyLightIntensity=8;
        camp.transform.SetParent(root.transform);camp.transform.SetPositionAndRotation(new Vector3(10,0,-12),Quaternion.Euler(0,-28,0));camp.transform.localScale=Vector3.one*.34f;
        study.View=camera;study.Actor=actor.GetComponent<Animator>();study.Environment=environment;study.ApplyFraming();
        study.ApplyCharacterFacing();environment.Cycle=true;environment.CycleSeconds=240;
        PlaceStudyCliffs(root.transform);
        var shrubs=new[]{new Vector3(-1.8f,0,6),new Vector3(-.4f,0,5),new Vector3(1,0,8),new Vector3(3,0,4),new Vector3(-1.2f,0,1),new Vector3(0,0,-3),new Vector3(6.5f,3.5f,8.3f)};
        for(int i=0;i<shrubs.Length;i++)Place(i==5?"pdsc_bio_westfall_shrub_03_hd":"pbsc_bio_ep06_broadleaf_shrub_"+(i%2==0?"14":"13")+"_hd",shrubs[i],Vector3.one*(i==5?1.12f:.9f+(i%3)*.12f),0,root.transform);
        for(int i=0;i<5;i++)Place("pbsc_bio_ep06_broadleaf_tree_0"+(i%2+4)+"_hd",new Vector3(i<3?-8+i*3.8f:4+(i-3)*5,i<3?7.048166f:3.524083f,i<3?14.3f:11.4f),Vector3.one*(i<3?.75f:.9f),0,root.transform);
        for(int i=0;i<7;i++){float z=i==0?2.5f:6.5f+(i%4)*1.1f;Place("pbsc_bio_westfall_reed_01_billboad_hd",new Vector3(Bank(z)-.3f-(i/4)*1.4f-(i%3)*.36f,-.08f,z),Vector3.one*(.9f+(i%3)*.12f),0,root.transform);}
        for(int i=0;i<3;i++)Place("pbsc_bio_chapter00_stone_0"+(i==0?1:3)+"_hd",new Vector3(Bank(i*3+3)+.1f,0,i*3+3),Vector3.one*.9f,80+i*37,root.transform);
        Place("pbsc_homestead_mainbuilding_01_homestead_root_hd",new Vector3(-7f,0,-10.5f),Vector3.one*.9f,150,root.transform);
        var flowerCenters=new[]{new Vector2(-.5f,3.6f),new Vector2(1,4),new Vector2(3.8f,6.3f),new Vector2(-2.1f,-.3f),new Vector2(-3,-2.5f),new Vector2(-2.6f,-6.1f),new Vector2(-3.2f,-8),new Vector2(2,-8.8f),new Vector2(.3f,-12.6f)};
        for(int i=0;i<flowerCenters.Length;i++) {
            var p=flowerCenters[i];
            PlaceStudyFlowers(p,i,root.transform);
            if(i>4)PlaceStudyFlowers(p+new Vector2(1.2f,-.6f),i+9,root.transform);
        }
        var grassReport=Read(Source+"/layout.json")["instances"].Where(t=>(string)t["kind"]=="Grass"&&((string)t["key"]).Contains("grass_detail")).ToArray();
        int grassCount=0;
        foreach(var d in grassReport) {
            var m=Matrix(d["matrix"],true);var p=(Vector3)m.GetColumn(3);p-=new Vector3(150,0,195);
            if(Mathf.Abs(p.x)>25||p.z < -25||p.z>9||p.y<-.5f||p.y>.5f)continue;
            if(p.z>-3&&p.z<12.6f&&p.x<Bank(p.z)+.25f)continue;
            if(Vector2.Distance(new Vector2(p.x,p.z),new Vector2(-7,-10.5f))<4)continue;
            Place("pbsc_grass_detail_homestead_hd",new Vector3(p.x,0,p.z),Vector3.one*m.GetColumn(0).magnitude,0,root.transform);grassCount++;
        }
        var surface=Shader.Find("InsectSpace/Local Homestead Surface");
        var groundTileImporter=(TextureImporter)AssetImporter.GetAtPath(Source+"/TemplateMeadow.png");
        groundTileImporter.textureCompression=TextureImporterCompression.Uncompressed;
        groundTileImporter.filterMode=FilterMode.Bilinear;groundTileImporter.SaveAndReimport();
        var terrainData=Read(Source+"/Prefabs/prefabs.json")["materials"].Single(m=>(string)m["name"]=="terrain_realtime_map");
        var terrain=AssetDatabase.LoadAssetAtPath<Material>(Generated+"/Materials/"+(string)terrainData["id"]+".mat");
        if(!terrain)throw new InvalidOperationException("Import the recovered terrain material first.");
        var ground=Store(new Material(terrain){name="Recovered VT meadow"},Generated+"/Materials/StudyGround.mat");
        // Color properties are uploaded in linear space; store calibrated multipliers as sRGB.
        var dayGroundTint=new Color(.46f,.5f,.8f).gamma;
        ground.SetColor("_StudyDayTint",dayGroundTint);ground.SetColor("_StudyDayTopTint",Color.white);
        ground.SetColor("_StudyNightTint",new Color(1,.94f,1.02f));ground.SetFloat("_StudyNightExposure",.78f);
        ground.SetFloat("_StudyNightTopExposure",1);ground.SetFloat("_StudyNightTopSaturation",1);ground.SetFloat("_StudyLocalLightWeight",1.5f);
        ground.SetColor("_StudyNightTopTint",Color.white);ground.SetColor("_StudyNightShadowTint",new Color(1.05f,1.07f,1.22f));
        ground.SetFloat("_StudyLocalShadowWeight",.7f);
        ground.SetTexture("_WorldMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Source+"/VT/homestead_01/diffuse-world.png"));
        ground.SetTexture("_TerrainDetail",AssetDatabase.LoadAssetAtPath<Texture2D>(Source+"/TemplateMeadow.png"));ground.SetFloat("_TerrainDetailStrength",.65f);
        ground.SetFloat("_TerrainDetailTiling",.045f);ground.SetFloat("_TerrainDetailContrast",4);
        ground.SetFloat("_WorldMapMip",3);
        var rect=new Vector4(-140,-185,408,544);ground.SetVector("_WorldMapRect",rect);
        var vertices=new List<Vector3>();var triangles=new List<int>();
        // Share the water's sampled rim with the ground. The previous disconnected
        // one-unit squares produced large dark triangles along the curved shore.
        for(int row=0;row<=122;row++) {
            float z=row==0?-40:row==122?40:Mathf.Lerp(-3,12.6f,(row-1)/120f);
            float near=Bank(z),far=FarBank(z),width=near-far;
            float shelf=Mathf.Min(1.5f,width*.4f);
            var xs=new[]{-45f,far-2,far-.25f,far,far+shelf*.05f,far+shelf*.15f,far+shelf*.3f,far+shelf*.5f,far+shelf*.75f,far+shelf,
                (near+far)*.5f,near-shelf,near-shelf*.75f,near-shelf*.5f,near-shelf*.3f,near-shelf*.15f,near-shelf*.05f,near,near+.25f,near+2,45f};
            for(int col=0;col<xs.Length;col++) {
                float depth=(row>0&&row<122)?Mathf.SmoothStep(0,.65f,Mathf.Clamp01(Mathf.Min(near-xs[col],xs[col]-far)*.85f)):0;
                vertices.Add(new Vector3(xs[col],-depth-.025f,z));
                if(row>0&&col>0){int a=row*xs.Length+col;triangles.AddRange(new[]{a-xs.Length-1,a-1,a,a-xs.Length-1,a,a-xs.Length});}
            }
        }
        DrawStudyMesh("Meadow",vertices,triangles,null,ground,root.transform);
        var water=Store(new Material(AssetDatabase.LoadAssetAtPath<Material>(Generated+"/Materials/Water.mat")){name="Recovered lake study"},Generated+"/Materials/StudyWater.mat");
        ConfigureWaterReflection(water);
        water.SetFloat("_StudyNightExposure",.55f);water.SetVector("_ApproximateMaskRange",new Vector4(.74f,.81f,.12f,0));
        water.SetColor("_StudyNightTint",new Color(1,1.18f,1));
        water.SetColor("_StudyDayTint",new Color(1.3f,.9f,.94f).gamma);
        water.SetFloat("_FirstLayerWidth",.04f);water.SetFloat("_StudyShoreStrength",.55f);
        water.SetFloat("_StudySoftShore",1);water.SetFloat("_StudyRippleStrength",.28f);
        water.SetFloat("_StudyShoreWaveSpeed",.4f);water.SetFloat("_StudyShoreWaveSpacing",.8f);
        water.SetFloat("_StudyShoreWaveWidth",.045f);water.SetFloat("_StudyShoreWaveRange",1.8f);
        water.SetFloat("_StudyShoreWaveStrength",.6f);water.SetFloat("_StudyShoreWaveTime",-1);
        water.SetFloat("_StudyShoreSwayHeight",.009f);water.SetFloat("_StudyShoreSwayWidth",.035f);
        vertices.Clear();triangles.Clear();var colors=new List<Color>();
        var shoreContour=new List<Vector2>();
        for(int row=0;row<=120;row++) {float z=Mathf.Lerp(-3,12.6f,row/120f);shoreContour.Add(new Vector2(Bank(z),z));}
        for(int row=120;row>=0;row--) {float z=Mathf.Lerp(-3,12.6f,row/120f);shoreContour.Add(new Vector2(FarBank(z),z));}
        const int waterColumns=128;
        for(int row=0;row<=120;row++) {
            float z=Mathf.Lerp(-3,12.6f,row/120f),edge=Bank(z);
            for(int column=0;column<=waterColumns;column++) {
                float t=Mathf.Sin(column/(float)waterColumns*Mathf.PI*.5f);
                float px=Mathf.Lerp(edge,FarBank(z),t*t);
                float depth=Mathf.Clamp01(Mathf.Min(edge-px,px-FarBank(z))*3.5f);
                // Keep the composed lake above the meadow tessellation at its rim.
                // Otherwise coarse ground triangles pierce the water in the wide view.
                vertices.Add(new Vector3(px,.005f,z));colors.Add(new Color(depth,ShoreDistance(new Vector2(px,z),shoreContour),0,1));
                if(row>0&&column>0){int a=row*(waterColumns+1)+column;triangles.AddRange(new[]{a-waterColumns-2,a-1,a,a-waterColumns-2,a,a-waterColumns-1});}
            }
        }
        DrawStudyMesh("Lake",vertices,triangles,colors,water,root.transform);
        var effects=camera.gameObject.AddComponent<AfkStudyCameraEffects>();
        effects.Water=root.transform.Find("Lake").GetComponent<Renderer>();
        effects.FogMaterial=Store(new Material(Shader.Find("InsectSpace/Local AFK Volumetric Fog")){name="LOCAL reconstructed volumetric atmosphere"},Generated+"/Materials/StudyFog.mat");
        effects.FogMaterial.SetTexture("_NoiseTex",Texture("FogNoise",Advanced));
        var fogSource=JArray.Parse(File.ReadAllText(Source+"/environment.json")).First(t=>(string)t["type"]=="EnvironmentInfo")["data"]["fog"];
        effects.FogMaterial.SetFloat("_NoiseTile",F(fogSource,"NoiseTile",50));
        effects.WaterHeight=.005f;
        var materialCopies=new Dictionary<Material,Material>();
        foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>()) {
            renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>{
                if(!m||m.shader!=surface||m==ground)return m;
                if(materialCopies.TryGetValue(m,out var copy))return copy;
                copy=Store(new Material(m){name=m.name+" study"},Generated+"/Materials/Study-"+m.name+".mat");copy.SetVector("_WorldMapRect",rect);
                // These empirical controls compensate for unrecovered zone overrides in this shot.
                copy.SetColor("_StudyDayTint",Color.white);copy.SetColor("_StudyDayTopTint",Color.white);
                copy.SetColor("_StudyNightTint",Color.white);copy.SetFloat("_StudyNightExposure",1);
                copy.SetFloat("_StudyNightTopExposure",1);copy.SetFloat("_StudyNightTopSaturation",1);copy.SetFloat("_StudyLocalLightWeight",1);
                copy.SetColor("_StudyNightTopTint",Color.white);copy.SetColor("_StudyNightShadowTint",Color.white);
                copy.SetFloat("_StudyLocalShadowWeight",.7f);
                copy.SetFloat("_UseSourceZoneLight",0);
                if(copy.GetFloat("_Kind")==2){copy.SetColor("_StudyDayTint",new Color(.44f,.5f,.63f).gamma);copy.SetColor("_StudyDayTopTint",new Color(.964f,.952f,1.795f).gamma);copy.SetFloat("_StudyNightExposure",.8f);copy.SetFloat("_StudyNightTopExposure",.4f);copy.SetFloat("_StudyNightTopSaturation",.6f);copy.SetColor("_StudyNightTopTint",new Color(.9f,1.05f,.9f));copy.SetFloat("_StudyLocalLightWeight",.25f);}
                if(copy.GetFloat("_Kind")==3){copy.SetColor("_StudyNightTint",new Color(.9f,.96f,.78f));copy.SetFloat("_StudyNightExposure",.7f);copy.SetFloat("_StudyLocalLightWeight",.3f);}
                if(copy.GetFloat("_TYPE_SLIVER")>.5f) {copy.SetColor("_StudyDayTint",new Color(.16f,.19f,.16f).gamma);copy.SetFloat("_StudyNightExposure",.35f);copy.SetFloat("_StudyLocalLightWeight",.2f);}
                if(copy.GetFloat("_Kind")==4&&copy.GetFloat("_TYPE_FLOWER")<.5f&&copy.GetFloat("_TYPE_SLIVER")<.5f) {
                    copy.SetColor("_StudyDayTint",dayGroundTint);
                    copy.SetColor("_StudyNightTint",ground.GetColor("_StudyNightTint"));copy.SetFloat("_StudyNightExposure",.78f);
                    copy.SetColor("_StudyNightShadowTint",ground.GetColor("_StudyNightShadowTint"));
                    copy.SetFloat("_StudyLocalLightWeight",1.5f);copy.SetFloat("_GrassHeight",copy.GetFloat("_GrassHeight")*.8f);
                }
                if(copy.GetFloat("_Kind")==2)copy.SetTexture("_WorldMap",ground.GetTexture("_WorldMap"));
                if(copy.GetFloat("_Kind")==1||copy.GetFloat("_Kind")==2){copy.SetTexture("_TerrainDetail",ground.GetTexture("_TerrainDetail"));copy.SetFloat("_TerrainDetailStrength",.65f);}
                if(copy.GetFloat("_Kind")==4&&copy.GetFloat("_TYPE_FLOWER")<.5f){copy.SetTexture("_TerrainDetail",ground.GetTexture("_TerrainDetail"));copy.SetFloat("_TerrainDetailStrength",.65f);}
                if(copy.GetFloat("_Kind")==1||copy.GetFloat("_Kind")==2||copy.GetFloat("_Kind")==4) {
                    copy.SetFloat("_TerrainDetailTiling",ground.GetFloat("_TerrainDetailTiling"));
                    copy.SetFloat("_TerrainDetailContrast",ground.GetFloat("_TerrainDetailContrast"));
                    copy.SetFloat("_WorldMapMip",ground.GetFloat("_WorldMapMip"));
                }
                if(m.name.Contains("westfall_shrub_03")){copy.SetColor("_Tint",new Color(.28f,.53f,.4f));copy.SetColor("_StudyNightTint",new Color(.9f,1,.65f));copy.SetFloat("_StudyNightExposure",1);copy.SetFloat("_StudyLocalLightWeight",.25f);}
                // Composition-only adjustments; keep the imported material library intact.
                if(m.name=="mdsc_bio_chapter03_dandelion_02_impostor") {
                    copy.SetFloat("_OffsetY",0);copy.SetFloat("_Width",1.8f);copy.SetFloat("_Height",.9f);
                    copy.SetColor("_StudyDayTint",new Color(.8f,.85f,.8f).gamma);
                }
                if(m.name.Contains("homestead_mainbuilding"))copy.SetFloat("_StudyNightExposure",.55f);
                materialCopies[m]=copy;return copy;
            }).ToArray();
            if(renderer.sharedMaterials.Any(m=>m&&m.HasProperty("_Kind")&&m.GetFloat("_Kind")==3))renderer.shadowCastingMode=ShadowCastingMode.On;
        }
        // Adjacent plateau tiles must use the same VT-painted cap response as the cliffs.
        var cap=materialCopies.Values.First(m=>m.GetFloat("_Kind")==2);
        foreach(var plateau in materialCopies.Values.Where(m=>m.GetFloat("_Kind")==1)) {
            foreach(string p in new[]{"_AOIntensity","_DarkThreshold","_ShadowThreshold","_ShadowFeather","_ShadowIntensity","_InShadowIntensity","_StudyLocalLightWeight"})plateau.SetFloat(p,cap.GetFloat(p));
            foreach(string p in new[]{"_DarkTint","_MidTint","_StudyNightTint","_StudyNightTopTint","_StudyNightShadowTint"})plateau.SetColor(p,cap.GetColor(p));
            plateau.SetColor("_StudyDayTint",(cap.GetColor("_StudyDayTint").linear*cap.GetColor("_StudyDayTopTint").linear).gamma);
            plateau.SetColor("_StudyDayTopTint",Color.white);
            plateau.SetFloat("_StudyNightExposure",cap.GetFloat("_StudyNightExposure")*cap.GetFloat("_StudyNightTopExposure"));
            plateau.SetFloat("_StudyNightTopExposure",1);
            plateau.SetFloat("_StudyNightTopSaturation",cap.GetFloat("_StudyNightTopSaturation"));
            EditorUtility.SetDirty(plateau);
        }
        ApplyGrassBounds();AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene,StudyScene);
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(StudyScene);Selection.activeGameObject=root;
        string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../../.artifacts/validation/afk-recovered/2026-10-09"));Directory.CreateDirectory(folder);
        Capture(camera,Path.Combine(folder,"Draft-Portrait.png"),1080,1920);
        return new {scene=StudyScene,grassCount,renderers=root.GetComponentsInChildren<Renderer>().Length,screenshot=folder+"/Draft-Portrait.png"};
    }
    static void PlaceStudyFlowers(Vector2 center,int index,Transform parent)
    {
        // Short, wide billboards use the original yellow flower diffuse and normal.
        for(int j=0;j<3;j++) {
            float angle=(index*31+j*137.5f)*Mathf.Deg2Rad;
            var offset=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*(j==0?0:.38f);
            Place("pbsc_bio_chapter03_dandelion_02_hd",new Vector3(center.x+offset.x,-.015f,center.y+offset.y),Vector3.one*(.5f+j*.025f),0,parent);
        }
    }
    static void ConfigureCharacter(GameObject actor)
    {
        const string folder="Assets/Temp/AFKStudy/Models/Faye";
        var report=Read(folder+"/materials.json");
        foreach(var texture in report["textures"]) {
            var importer=(TextureImporter)AssetImporter.GetAtPath(folder+"/"+(string)texture["name"]+".png");
            importer.textureType=TextureImporterType.Default;importer.sRGBTexture=(bool)texture["sRGB"];
            importer.textureCompression=TextureImporterCompression.Uncompressed;importer.maxTextureSize=4096;
            importer.SaveAndReimport();
        }
        var materials=report["materials"].ToDictionary(m=>(string)m["name"],m=>ImportMaterial(m,Shader.Find("InsectSpace/Local AFK Character"),folder));
        foreach(var material in materials.Values) {
            var source=report["materials"].Single(m=>(string)m["name"]==material.name);
            string cube=(string)source["textures"].FirstOrDefault(t=>(string)t["property"]=="_CustomEnvironmentReflection")?["id"];
            // Skin has no serialized cube override. Use the body's decoded environment.
            material.SetTexture("_CustomEnvironmentReflection",AssetDatabase.LoadAssetAtPath<Cubemap>(Generated+"/"+(cube??"hdr_10")+".asset"));
            material.SetFloat("_StudyLocalLightSharp",1);EditorUtility.SetDirty(material);
        }
        foreach(var renderer in actor.GetComponentsInChildren<SkinnedMeshRenderer>()) {
            renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>materials.TryGetValue(m.name,out var copy)?copy:m).ToArray();
        }
    }
    static void ImportAdvancedInputs()
    {
        var inputs=Read(Advanced+"/inputs.json");
        foreach(var texture in inputs["textures"]) {
            string path=Advanced+"/"+(string)texture["binding"]+".png";
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=TextureImporterType.Default;importer.sRGBTexture=(bool)texture["sRGB"];
            importer.wrapMode=TextureWrapMode.Repeat;importer.mipmapEnabled=true;
            importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
        }
        foreach(var input in inputs["cubemaps"]) {
            var cube=new Cubemap((int)input["size"],TextureFormat.RGBA32,true){name=(string)input["name"],wrapMode=TextureWrapMode.Clamp};
            foreach(var face in input["faces"]) {
                string path=Advanced+"/"+(string)face["file"];
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType=TextureImporterType.Default;importer.sRGBTexture=(bool?)input["sRGB"]??false;
                importer.isReadable=true;importer.mipmapEnabled=false;
                importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
                var image=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                cube.SetPixels(image.GetPixels(),(CubemapFace)Enum.Parse(typeof(CubemapFace),(string)face["face"]));
            }
            cube.Apply(true,false);Store(cube,Generated+"/"+(string)input["name"]+".asset");
        }
    }
    public static object Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before rebuilding.");
        Folder(Generated+"/Meshes");Folder(Generated+"/Materials");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var report=Read(Source+"/Prefabs/prefabs.json");
        var normalIds=new HashSet<string>(report["materials"].SelectMany(t=>t["textures"]).Where(t=>((string)t["property"]).Contains("Normal")).Select(t=>(string)t["id"]));
        foreach(var texture in report["textures"]) {
            string path=Source+"/Prefabs/"+(string)texture["id"]+".png";
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=TextureImporterType.Default; importer.sRGBTexture=!normalIds.Contains((string)texture["id"]);
            importer.alphaSource=TextureImporterAlphaSource.FromInput; importer.alphaIsTransparency=false;
            importer.textureCompression=TextureImporterCompression.Uncompressed; importer.maxTextureSize=4096;
            if (EditorUtility.IsDirty(importer)) importer.SaveAndReimport();
        }
        foreach (string channel in new[]{"small_diffuse","diffuse"}) {
            var vt=(TextureImporter)AssetImporter.GetAtPath(Source+"/VT/homestead_01/"+channel+"-world.png");
            vt.wrapMode=TextureWrapMode.Clamp; vt.textureCompression=TextureImporterCompression.Uncompressed; vt.maxTextureSize=4096;vt.SaveAndReimport();
        }
        var shader=Shader.Find("InsectSpace/Local Homestead Surface");
        if (!shader || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Homestead shader failed to compile.");
        var meshes=report["meshes"].ToDictionary(t=>(string)t["id"],t=>ImportMesh((string)t["id"]));
        var materials=report["materials"].ToDictionary(t=>(string)t["id"],t=>ImportMaterial(t,shader));
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var templates=new GameObject("Reference templates");
        var nodes=report["nodes"].ToDictionary(t=>(string)t["id"],t=>new GameObject((string)t["name"]));
        foreach(var d in report["nodes"]) {
            var go=nodes[(string)d["id"]]; string parent=(string)d["parent"];
            go.transform.SetParent(!string.IsNullOrEmpty(parent)&&nodes.ContainsKey(parent)?nodes[parent].transform:templates.transform,false);
            if (d["matrix"]!=null) TransformMatrix(go.transform,Matrix(d["matrix"],false));
            else {go.transform.localPosition=V(d["position"]);go.transform.localScale=V(d["scale"]);var q=d["rotation"];go.transform.localRotation=new Quaternion(F(q,"x"),F(q,"y"),F(q,"z"),F(q,"w",1));}
            if (meshes.TryGetValue((string)d["mesh"]??"",out var mesh)) {
                go.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterials=d["materials"].Select(t=>materials[(string)t]).ToArray();
                renderer.shadowCastingMode=(bool?)d["shadow"]==false?ShadowCastingMode.Off:ShadowCastingMode.On;renderer.receiveShadows=true;
                renderer.enabled=(bool?)d["enabled"]??true;
            }
            go.SetActive((bool?)d["active"]??true);
        }
        var roots=nodes.Values.Where(g=>g.transform.parent==templates.transform).ToDictionary(g=>g.name,StringComparer.OrdinalIgnoreCase);
        var world=new GameObject("Homestead original streamed layout");
        var groups=new Dictionary<string,Transform>();
        int count=0;var missing=new HashSet<string>();
        foreach(var d in Read(Source+"/layout.json")["instances"]) {
            string key=Path.GetFileNameWithoutExtension((string)d["key"]),kind=(string)d["kind"];
            if (!roots.TryGetValue(key,out var original)) {missing.Add(key);continue;}
            if (!groups.TryGetValue(kind,out var parent)) {parent=new GameObject(kind).transform;parent.SetParent(world.transform);groups[kind]=parent;}
            var go=UnityEngine.Object.Instantiate(original,parent);go.name=key+"_"+count++;
            TransformMatrix(go.transform,Matrix(d["matrix"],true));go.SetActive(true);
        }
        UnityEngine.Object.DestroyImmediate(templates);
        int waterTiles=BuildWater(world.transform);
        var camera=new GameObject("Homestead Camera").AddComponent<Camera>();camera.tag="MainCamera";
        FrameMapCamera(camera);
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.19f,.30f,.32f);camera.allowHDR=true;
        camera.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=true;
        var light=new GameObject("Reference Sun").AddComponent<Light>();light.type=LightType.Directional;
        light.shadows=LightShadows.Soft;light.shadowBias=.025f;light.shadowNormalBias=.15f;
        ApplyEnvironment(light,.5f);
        var volume=new GameObject("Reference post processing").AddComponent<Volume>();volume.isGlobal=true;
        var profile=new VolumeProfile();var bloom=profile.Add<Bloom>();bloom.threshold.Override(1);bloom.intensity.Override(2);bloom.scatter.Override(.7f);
        var tonemap=profile.Add<Tonemapping>();tonemap.mode.Override(TonemappingMode.Neutral);
        string profilePath=Generated+"/HomesteadVolume.asset";
        if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath)) AssetDatabase.DeleteAsset(profilePath);
        AssetDatabase.CreateAsset(profile,profilePath);
        foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component,profile);
        volume.sharedProfile=profile;
        ConfigureEnvironment(light,profile);
        ApplyGrassBounds();
        AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene,ScenePath);Selection.activeGameObject=world;
        return new {scene=ScenePath,instances=count,waterTiles,meshes=meshes.Count,materials=materials.Count,missing=missing.ToArray()};
    }
    static void ConfigureWaterReflection(Material water)
    {
        string folder=Source+"/Water/Reflection";
        var record=Read(folder+"/cubemaps.json")["cubemaps"].First(t=>(string)t["name"]=="skybox_02");
        string path=Generated+"/skybox_02.asset";
        var cube=AssetDatabase.LoadAssetAtPath<Cubemap>(path);
        if(!cube) {
            cube=new Cubemap((int)record["size"],TextureFormat.RGBA32,true){name="Original skybox_02 water reflection",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Trilinear};
            foreach(var face in record["faces"]) {
                var image=new Texture2D(2,2,TextureFormat.RGBA32,false);
                try {image.LoadImage(File.ReadAllBytes(folder+"/"+(string)face["file"]));cube.SetPixels(image.GetPixels(),(CubemapFace)Enum.Parse(typeof(CubemapFace),(string)face["face"]));}
                finally {UnityEngine.Object.DestroyImmediate(image);}
            }
            cube.Apply(true,false);AssetDatabase.CreateAsset(cube,path);
        }
        var data=Read(Source+"/Water/water.json")["materials"].First(t=>(string)t["name"]=="mt_water_homestead");
        foreach(var value in (JObject)data["floats"])ImportFloat(water,value.Key,(float)value.Value);
        foreach(var value in (JObject)data["colors"])ImportColor(water,value.Key,value.Value);
        foreach(var value in (JObject)data["textures"]) {
            if(!water.HasProperty(value.Key))continue;
            string texturePath=Source+"/Water/"+(string)value.Value["name"]+".png";
            if(!File.Exists(texturePath))continue;
            var importer=(TextureImporter)AssetImporter.GetAtPath(texturePath);
            if(value.Key=="_LitSpotTex" && importer.sRGBTexture) {
                importer.sRGBTexture=false;importer.SaveAndReimport();
            }
            water.SetTexture(value.Key,AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
        }
        water.SetTexture("_RefCube",cube);water.SetFloat("_RecoveredReflection",1);EditorUtility.SetDirty(water);
    }
    static int BuildWater(Transform parent)
    {
        var report=Read(Source+"/Water/water.json");
        var textures=new Dictionary<string,Texture2D>();
        foreach(var d in report["textures"]) {
            string name=(string)d["name"];
            if((string)d["format"]=="RGBAHalf") {
                var texture=new Texture2D((int)d["width"],(int)d["height"],TextureFormat.RGBAHalf,false,true){name=name};
                texture.LoadRawTextureData(File.ReadAllBytes(Source+"/Water/"+name+".half.bytes"));texture.Apply(false);
                texture.wrapMode=name.Contains("watertex")?TextureWrapMode.Clamp:TextureWrapMode.Repeat;
                textures[name]=Store(texture,Generated+"/"+name+".asset");
            } else textures[name]=AssetDatabase.LoadAssetAtPath<Texture2D>(Source+"/Water/"+name+".png");
        }
        var materialData=report["materials"].First(t=>(string)t["name"]=="mt_water_homestead");
        var water=Store(new Material(Shader.Find("InsectSpace/Local Homestead Water")){name="Original homestead water"},Generated+"/Materials/Water.mat");
        foreach(var p in (JObject)materialData["floats"]) ImportFloat(water,p.Key,(float)p.Value);
        foreach(var p in (JObject)materialData["colors"]) ImportColor(water,p.Key,p.Value);
        foreach(var p in (JObject)materialData["textures"]) if(water.HasProperty(p.Key)&&textures.TryGetValue((string)p.Value["name"],out var texture)) water.SetTexture(p.Key,texture);
        ConfigureWaterReflection(water);
        var shore=Store(new Material(Shader.Find("InsectSpace/Local Homestead Surface")){name="Original shoreline bed"},Generated+"/Materials/Shore.mat");
        shore.SetFloat("_Kind",1);shore.SetFloat("_ShadowThreshold",.5f);shore.SetFloat("_InShadowIntensity",1);
        shore.SetTexture("_WorldMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Source+"/VT/homestead_01/diffuse-world.png"));
        shore.SetVector("_WorldMapRect",new Vector4(0,0,408,544));
        var meshData=Read(Source+"/Water/water_hexagonmesh.json");
        var original=meshData["vertices"].Select(V).ToArray();
        var vertexIndices=meshData["uv1"].Select(t=>(int)F(t,"x")).ToArray();
        var indices=meshData["submeshes"][0]["indices"].Select(t=>(int)t).ToArray();
        var data=textures["homestead_01_watertex"].GetPixels();
        var waterPositions=new List<Vector3>();var shorePositions=new List<Vector3>();var triangles=new List<int>();var colors=new List<Color>();
        int tileCount=0;
        foreach(var tile in report["tiles"]) {
            int x=(int)tile["x"],z=(int)tile["z"];
            var position=new Vector3(x*3,(int)tile["height_layer"]*2,(z*2+x%2)*Mathf.Sqrt(3));
            // Map rotations use the opposite sign from Unity's Y rotation.
            // Verified on 32,614 shared vertices: max depth seam < 0.004.
            var rotation=Quaternion.Euler(0,-(float)tile["rotation"],0);int start=waterPositions.Count;
            for(int i=0;i<original.Length;i++) {
                int sample=(int)tile["template"]*64+vertexIndices[i];float depth=data[sample/4][sample%4];
                float d=Mathf.Clamp01(depth*(int)tile["water_layers"]*.7f);d=d*d*(3-2*d);
                var point=rotation*original[i]+position;
                shorePositions.Add(point-Vector3.up*d);waterPositions.Add(point-Vector3.up*.7f);
                colors.Add(new Color(depth,depth,depth,1));
            }
            foreach(int index in indices)triangles.Add(start+index);tileCount++;
        }
        var root=new GameObject("Recovered water and shoreline");root.transform.SetParent(parent,false);
        for(int pass=0;pass<2;pass++) {
            var mesh=new Mesh{name=pass==0?"Original water tiles":"Original deformed shore",indexFormat=IndexFormat.UInt32};
            mesh.SetVertices(pass==0?waterPositions:shorePositions);mesh.SetTriangles(triangles,0);mesh.SetColors(colors);mesh.RecalculateNormals();mesh.RecalculateBounds();
            mesh=Store(mesh,Generated+"/Meshes/"+(pass==0?"Water":"Shore")+".asset");
            var go=new GameObject(mesh.name);go.transform.SetParent(root.transform,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=pass==0?water:shore;renderer.shadowCastingMode=ShadowCastingMode.Off;
        }
        EditorUtility.SetDirty(water);EditorUtility.SetDirty(shore);return tileCount;
    }

    public static object CaptureViews()
    {
        string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../../.artifacts/reference/afk-journey"));
        var camera=Camera.main;var captures=new List<string>();
        foreach(var target in new[]{new Vector3(205,0,95),new Vector3(225,0,125),new Vector3(235,0,145),new Vector3(205,0,165)}) {
            camera.transform.SetPositionAndRotation(target+new Vector3(0,45,-45),Quaternion.Euler(45,0,0));camera.orthographicSize=25;
            var rt=RenderTexture.GetTemporary(900,1200,24,RenderTextureFormat.ARGB32);var previous=camera.targetTexture;var active=RenderTexture.active;float aspect=camera.aspect;
            var tex=new Texture2D(900,1200,TextureFormat.RGB24,false);
            try {camera.aspect=.75f;camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,900,1200),0,0);tex.Apply();
                string path=Path.Combine(folder,"homestead-water-"+target.x+"-"+target.z+".png");File.WriteAllBytes(path,tex.EncodeToPNG());captures.Add(path);}
            finally {camera.targetTexture=previous;camera.aspect=aspect;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(tex);}
        }
        return captures;
    }
    static Gradient Gradient(JToken t)
    {
        var gradient=new Gradient();int colors=(int)t["m_NumColorKeys"],alpha=(int)t["m_NumAlphaKeys"];
        gradient.SetKeys(Enumerable.Range(0,colors).Select(i=>new GradientColorKey(C(t["key"+i]),(float)t["ctime"+i]/65535)).ToArray(),
            Enumerable.Range(0,alpha).Select(i=>new GradientAlphaKey(F(t["key"+i],"a",1),(float)t["atime"+i]/65535)).ToArray());return gradient;
    }
    static float Curve(JToken t,float time)
    {
        return CurveData(t).Evaluate(time);
    }
    static AnimationCurve CurveData(JToken t) => new AnimationCurve(t["m_Curve"].Select(k=>new Keyframe(F(k,"time"),F(k,"value"),F(k,"inSlope"),F(k,"outSlope"),F(k,"inWeight"),F(k,"outWeight")){weightedMode=(WeightedMode)(int)k["weightedMode"]}).ToArray());
    static void ConfigureEnvironment(Light light,VolumeProfile profile)
    {
        var source=JArray.Parse(File.ReadAllText(Source+"/environment.json")).First(t=>(string)t["type"]=="EnvironmentInfo")["data"];
        var env=source["timeOfDay"];
        var controller=light.GetComponent<AfkHomesteadEnvironment>()??light.gameObject.AddComponent<AfkHomesteadEnvironment>();
        controller.Sun=light;controller.LightColor=Gradient(env["lightColorGradient"]);controller.AmbientColor=Gradient(env["ambientColorGradient"]);
        controller.LightIntensity=CurveData(env["lightIntensityCurve"]);
        controller.SunX=CurveData(env["sunRotCurveX"]);controller.SunY=CurveData(env["sunRotCurveY"]);controller.SunZ=CurveData(env["sunRotCurveZ"]);
        controller.TerrainFresnel=new Vector3(F(source["effect"],"TerrainFresnelIntensity"),F(source["effect"],"TerrainFresnelExp"),F(source["effect"],"TerrainFresnelClamp"));
        var water=source["waterColor"];
        controller.SourceAmbientValues=true;controller.SourceWaterValues=true;
        controller.WaterCenterColor=C(water["CenterColor"]);controller.WaterBedColor=C(water["BedColor"]);
        controller.WaterCoastColor=C(water["CoastColor"]);controller.WaterIntersectColor=C(water["IntersectColor"]);
        controller.WaterReflectLight=Gradient(water["WaterLightTint"]);controller.WaterReflectDark=Gradient(water["WaterDarkTint"]);
        controller.WaterReflectCloud=Gradient(water["WaterCloudTint"]);controller.SunReflection=Gradient(water["SunReflectionTint"]);
        controller.WaterSkyWeight=CurveData(water["SkyTintWeight"]);
        controller.WaterGradient=new Vector3(F(water,"GradientOffset"),F(water,"GradientRange"),F(water,"FadeOnY"));
        var cloud=source["cloud"];var fog=source["fog"];
        controller.CloudTexture=Texture("CloudShadow",Advanced);
        controller.CloudScale=.015f/Mathf.Max(F(cloud,"Size",1),.01f);
        controller.CloudSpeed=new Vector2(F(cloud["Speed"],"x"),F(cloud["Speed"],"y"))*.1f;
        controller.CloudFalloff=F(cloud,"FallOff",.8f);controller.CloudStrength=.25f;
        controller.FogColor=Gradient(fog["fogColorLine"]);controller.FogScatterColor=Gradient(fog["scatteringColorLine"]);
        controller.FogScattering=CurveData(fog["ScatteringLine"]);controller.VolumeLight=CurveData(fog["VolumeLightLine"]);
        controller.FogDensity=CurveData(fog["HeightFogDensity"]);
        // The reference density curve is empty; this extinction is a local reconstruction.
        if(controller.FogDensity.length==0)controller.FogDensity=AnimationCurve.Constant(0,1,.0015f);
        controller.FogStart=F(fog,"fogStartDis",35);controller.FogHeight=3;
        controller.FogHeightFalloff=1/Mathf.Max(F(fog,"fogGradientDis",9.27f),.1f);
        controller.Apply();EditorUtility.SetDirty(controller);
        string path=Source+"/ColorLookup.png";
        if(File.Exists(path)) {
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.sRGBTexture=false;importer.mipmapEnabled=false;importer.wrapMode=TextureWrapMode.Clamp;
            importer.filterMode=FilterMode.Bilinear;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.maxTextureSize=1024;importer.SaveAndReimport();
            if(!profile.TryGet<ColorLookup>(out var lookup)) {lookup=profile.Add<ColorLookup>();AssetDatabase.AddObjectToAsset(lookup,profile);}
            lookup.texture.Override(AssetDatabase.LoadAssetAtPath<Texture2D>(path));lookup.contribution.Override(.85f);
            EditorUtility.SetDirty(profile);EditorUtility.SetDirty(lookup);
        }
    }
    public static object UpdateRecovery()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before updating recovery.");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var shader=Shader.Find("InsectSpace/Local Homestead Surface");
        if(ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("Surface shader failed.");
        int materials=0;
        foreach(string folder in new[]{Source+"/Prefabs",Root+"/Supplement",Root+"/Buildings",Root+"/Monument",Root+"/FlowerVariants",Root+"/PlayerTemplate",Root+"/FlowerCandidates"})
            foreach(var material in Read(folder+"/prefabs.json")["materials"]) {ImportMaterial(material,shader,folder);materials++;}
        int grassBounds=ApplyGrassBounds();
        var light=UnityEngine.Object.FindObjectsByType<Light>().First(l=>l.type==LightType.Directional);
        ConfigureEnvironment(light,UnityEngine.Object.FindAnyObjectByType<Volume>().sharedProfile);
        AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        return new {materials,grassBounds,lut="tx_lut_prologue_01"};
    }
    static int ApplyGrassBounds()
    {
        int grassBounds=0;
        foreach(var renderer in UnityEngine.Object.FindObjectsByType<MeshRenderer>()) {
            var mesh=renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if(!mesh)continue;
            var grass=renderer.sharedMaterials.Where(m=>m&&m.HasProperty("_Kind")&&m.GetFloat("_Kind")==4&&m.GetFloat("_UseBillboard")<.5f).ToArray();
            if(grass.Length==0)continue;
            float height=grass.Max(m=>m.GetFloat("_GrassHeight"));
            float width=grass.Max(m=>m.GetFloat("_GrassWidth")*Mathf.Clamp(m.GetFloat("_GrassHeight")*.8f,.3f,1));
            float flowerScale=grass.Max(m=>m.GetFloat("_TYPE_FLOWER")>.5f?m.GetFloat("_Scale"):1);
            height*=flowerScale;width*=flowerScale;
            var bounds=mesh.bounds;bounds.center=Vector3.Scale(bounds.center,new Vector3(width,height,width));
            bounds.size=Vector3.Scale(bounds.size,new Vector3(width,height,width))+Vector3.one*.2f;
            renderer.localBounds=bounds;grassBounds++;
        }
        return grassBounds;
    }
    public static object ApplyEnvironment(float time)
    {
        ApplyEnvironment(UnityEngine.Object.FindObjectsByType<Light>().First(l=>l.type==LightType.Directional),time);return time;
    }
    static void ApplyEnvironment(Light light,float time)
    {
        var env=JArray.Parse(File.ReadAllText(Source+"/environment.json")).First(t=>(string)t["type"]=="EnvironmentInfo")["data"];
        var day=env["timeOfDay"];
        light.color=Gradient(day["lightColorGradient"]).Evaluate(time);light.intensity=Curve(day["lightIntensityCurve"],time);
        light.transform.rotation=Quaternion.Euler(Curve(day["sunRotCurveX"],time),Curve(day["sunRotCurveY"],time),Curve(day["sunRotCurveZ"],time));
        RenderSettings.sun=light;RenderSettings.ambientMode=AmbientMode.Flat;
        var ambient=Gradient(day["ambientColorGradient"]).Evaluate(time);RenderSettings.ambientLight=ambient;
        Shader.SetGlobalColor("_StudyAmbient",ambient.linear);Shader.SetGlobalFloat("_StudyTimeOfDay",time);
        RenderSettings.fog=false;
    }
}
