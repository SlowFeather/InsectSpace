using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using InsectSpace.Rendering;

// Run through Unity CLI. All serialized assets and scene edits use Unity APIs.
public static class BuildStylizedSky
{
    const string Root="Assets/InsectSpace/Rendering/Sky";
    const string ScenePath="Assets/InsectSpace/Scenes/SkyShowcase.unity";
    const string Meadow="Assets/InsectSpace/Scenes/MeadowShowcase.unity";
    static Color C(string hex) { ColorUtility.TryParseHtmlString(hex,out var color); return color; }
    static void Folder(string path) { var a=path.Split('/'); var p=a[0];for(int i=1;i<a.Length;i++){if(!AssetDatabase.IsValidFolder(p+"/"+a[i]))AssetDatabase.CreateFolder(p,a[i]);p+="/"+a[i];} }
    public static object Run()
    {
        if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before authoring sky assets.");
        for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++) if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save open scenes before building the showcase.");
        Folder(Root+"/Profiles");Folder(Root+"/Textures");Folder(Root+"/Materials");Folder(Root+"/Prefabs");Folder(Root+"/SceneMaterials");
        Shader shader=Shader.Find("InsectSpace/Mobile Painterly Sky");
        if(!shader || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Compile the sky shader first.");
        var atlas=GenerateAtlas();
        var profiles=new[]{Profile(0),Profile(1),Profile(2)};
        var mats=new Material[3];
        for(int i=0;i<3;i++)
        {
            string path=Root+"/Materials/"+profiles[i].name+".mat"; mats[i]=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!mats[i]) {mats[i]=new Material(shader);AssetDatabase.CreateAsset(mats[i],path);}
            mats[i].shader=shader; Apply(mats[i],profiles[i]);mats[i].SetTexture("_CloudAtlas",atlas);mats[i].EnableKeyword("_SKY_DETAILS");EditorUtility.SetDirty(mats[i]);
        }
        // A reusable controller prefab has no scene-specific camera/light references.
        var prefabGo=new GameObject("Stylized Sky");var prefabController=prefabGo.AddComponent<StylizedSkyController>();
        Configure(prefabController,mats[0],profiles,null,null);
        PrefabUtility.SaveAsPrefabAsset(prefabGo,Root+"/Prefabs/StylizedSky.prefab");UnityEngine.Object.DestroyImmediate(prefabGo);
        AssetDatabase.SaveAssets();
        var scene=EditorSceneManager.OpenScene(Meadow,OpenSceneMode.Single);
        EditorSceneManager.SaveScene(scene,ScenePath,true);
        scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
        // OpenScene can unload assets that were only held by transient managed references.
        profiles=new[]{"Daylight","GoldenHour","Moonrise"}.Select(n=>AssetDatabase.LoadAssetAtPath<StylizedSkyProfile>(Root+"/Profiles/"+n+".asset")).ToArray();
        mats=profiles.Select(p=>AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/"+p.name+".mat")).ToArray();
        atlas=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Textures/PainterlyCloudAtlas.png");
        shader=Shader.Find("InsectSpace/Mobile Painterly Sky");
        var roots=scene.GetRootGameObjects();
        var cam=roots.SelectMany(r=>r.GetComponentsInChildren<Camera>(true)).Single();
        cam.name="Sky Showcase Camera";cam.orthographic=false;cam.fieldOfView=45;cam.nearClipPlane=.1f;cam.farClipPlane=160;cam.clearFlags=CameraClearFlags.Skybox;
        cam.transform.position=new Vector3(.35f,2.65f,-9.4f);cam.transform.rotation=Quaternion.Euler(-4f,0,0);
        cam.GetUniversalAdditionalCameraData().renderPostProcessing=true;
        var light=roots.SelectMany(r=>r.GetComponentsInChildren<Light>()).First(l=>l.type==LightType.Directional);
        light.color=profiles[0].sunlight;light.intensity=profiles[0].lightIntensity;light.transform.rotation=Quaternion.Euler(42,-38,0);
        // Remove only near-camera tree/rock occluders in the copied presentation scene.
        foreach(var renderer in roots.Where(r=>r.name.StartsWith("Meadow vegetation")).SelectMany(r=>r.GetComponentsInChildren<MeshRenderer>(true)))
        {
            if(renderer.name.StartsWith("Grass_") || renderer.name=="MeadowTerrain" || renderer.name.Contains("clearing") || renderer.name.Contains("footpath")) continue;
            var b=renderer.bounds;
            if(b.center.z < 2 && b.center.y>.6f && Mathf.Abs(b.center.x)<5 && b.size.y>.9f) renderer.gameObject.SetActive(false);
        }
        var controller=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/StylizedSky.prefab")).GetComponent<StylizedSkyController>();
        controller.name="Stylized Sky - day sunset night";Configure(controller,mats[0],profiles,cam,light);
        var panel=new GameObject("Sky art review controls").AddComponent<SkyShowcasePanel>();var panelData=new SerializedObject(panel);
        panelData.FindProperty("sky").objectReferenceValue=controller;panelData.FindProperty("showcaseCamera").objectReferenceValue=cam;panelData.ApplyModifiedPropertiesWithoutUndo();
        RenderSettings.skybox=mats[0];RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=profiles[0].ambient;
        RenderSettings.fog=true;RenderSettings.fogColor=profiles[0].horizon;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogStartDistance=22;RenderSettings.fogEndDistance=70;
        // Ground extension avoids showing the finite meadow mesh edge against the sky.
        var ground=new GameObject("Distant meadow horizon");var mf=ground.AddComponent<MeshFilter>();var mr=ground.AddComponent<MeshRenderer>();
        var mesh=new Mesh {name="Distant meadow horizon"};
        mesh.vertices=new[]{new Vector3(-140,-.17f,15),new Vector3(-140,-.17f,145),new Vector3(140,-.17f,145),new Vector3(140,-.17f,15)};mesh.triangles=new[]{0,1,2,0,2,3};mesh.colors=Enumerable.Repeat(C("#70A281").linear,4).ToArray();mesh.uv=new[]{Vector2.zero,Vector2.up,Vector2.one,Vector2.right};mesh.RecalculateNormals();mesh.RecalculateBounds();
        string meshPath=Root+"/DistantMeadow.asset";var stored=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);if(stored){EditorUtility.CopySerialized(mesh,stored);UnityEngine.Object.DestroyImmediate(mesh);mesh=stored;EditorUtility.SetDirty(mesh);}else AssetDatabase.CreateAsset(mesh,meshPath);mf.sharedMesh=mesh;
        mr.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/InsectSpace/Rendering/Showcase/Painterly/Ground.mat");mr.shadowCastingMode=ShadowCastingMode.Off;
        // The original meadow shader deliberately ignores light intensity. This scene-only
        // variant respects the sun/ambient presets; original meadow materials stay intact.
        var lit=Shader.Find("InsectSpace/Sky Lit Meadow");
        if(!lit || ShaderUtil.ShaderHasError(lit))throw new InvalidOperationException("Compile the sky-lit meadow shader first.");
        var copies=new System.Collections.Generic.Dictionary<Material,Material>();
        foreach(var renderer in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Renderer>(true)))
        {
            var assigned=renderer.sharedMaterials;
            for(int i=0;i<assigned.Length;i++)
            {
                var source=assigned[i];if(!source || source.shader.name!="InsectSpace/Meadow Painterly")continue;
                if(!copies.TryGetValue(source,out var copy))
                {
                    string p=Root+"/SceneMaterials/"+source.name+".mat";copy=AssetDatabase.LoadAssetAtPath<Material>(p);
                    if(!copy){copy=new Material(source);AssetDatabase.CreateAsset(copy,p);}else EditorUtility.CopySerialized(source,copy);
                    copy.shader=lit;EditorUtility.SetDirty(copy);copies[source]=copy;
                }
                assigned[i]=copy;
            }
            renderer.sharedMaterials=assigned;
        }
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);Selection.activeGameObject=controller.gameObject;
        return new { scene=ScenePath, shader=shader.name, atlas=AssetDatabase.GetAssetPath(atlas), atlasSize=new[]{atlas.width,atlas.height}, presets=profiles.Select(p=>p.name).ToArray(), mobile="one sky pass; low=1 texture read; balanced/high=2" };
    }
    static void Configure(StylizedSkyController c,Material material,StylizedSkyProfile[] profiles,Camera cam,Light sun)
    {
        var so=new SerializedObject(c);so.FindProperty("skyTemplate").objectReferenceValue=material;var list=so.FindProperty("profiles");list.arraySize=3;
        for(int i=0;i<3;i++)list.GetArrayElementAtIndex(i).objectReferenceValue=profiles[i];
        so.FindProperty("targetCamera").objectReferenceValue=cam;so.FindProperty("directionalLight").objectReferenceValue=sun;so.ApplyModifiedPropertiesWithoutUndo();
    }
    static StylizedSkyProfile Profile(int i)
    {
        string name=new[]{"Daylight","GoldenHour","Moonrise"}[i]; string path=Root+"/Profiles/"+name+".asset";
        var p=AssetDatabase.LoadAssetAtPath<StylizedSkyProfile>(path);if(!p){p=ScriptableObject.CreateInstance<StylizedSkyProfile>();p.name=name;AssetDatabase.CreateAsset(p,path);}
        string[][] colors={
            new[]{"#116BAF","#409DD2","#B4DEDC","#FFFCE4","#85BDCC","#FFF4CC","#FFF1D6","#7F9F9F"},
            new[]{"#405384","#CF8593","#F7D1A0","#FFE2AE","#B4829A","#FFD181","#FFCC8B","#8D879B"},
            new[]{"#09132F","#20365F","#61788D","#9EADB9","#354665","#EEF3D6","#ADCEFF","#465C7F"}};
        var c=colors[i];p.zenith=C(c[0]);p.middle=C(c[1]);p.horizon=C(c[2]);p.cloudLight=C(c[3]);p.cloudShade=C(c[4]);p.celestialColor=C(c[5]);p.sunlight=C(c[6]);p.ambient=C(c[7]);
        p.night=i==2?1:0;p.exposure=1;p.coverage=i==2?.72f:1;p.lightIntensity=i==0?1.18f:i==1?.9f:.25f;p.cloudSpeed=i==2?.00032f:.00065f;p.celestialSize=i==2?.026f:.018f;
        p.celestialDirection=new Vector3(-.12f,i==1?.12f:.31f,1).normalized;EditorUtility.SetDirty(p);return p;
    }
    static void Apply(Material m,StylizedSkyProfile p)
    {
        m.SetColor("_Zenith",SC(p.zenith));m.SetColor("_Middle",SC(p.middle));m.SetColor("_Horizon",SC(p.horizon));m.SetColor("_CloudLight",SC(p.cloudLight));m.SetColor("_CloudShade",SC(p.cloudShade));m.SetColor("_SunColor",SC(p.celestialColor));m.SetVector("_SunDirection",p.celestialDirection);
        m.SetFloat("_Exposure",p.exposure);m.SetFloat("_Coverage",p.coverage);m.SetFloat("_Night",p.night);m.SetFloat("_SunSize",p.celestialSize);m.SetFloat("_Rotation",0);m.SetVector("_CloudOffset",Vector4.zero);
    }
    static float S(float a,float b,float value) { float t=Mathf.Clamp01((value-a)/(b-a));return t*t*(3-2*t); }
    static Color SC(Color color) => color;
    struct Puff { public float x,y,rx,ry; }
    static Texture2D GenerateAtlas()
    {
        const int w=2048,h=1024;var pixels=new Color32[w*h];var rng=new System.Random(62137);
        // R/G contain painted, scalloped cumulus silhouettes and light bands. B is independent cirrus.
        for(int cluster=0;cluster<16;cluster++)
        {
            float cx=cluster/16f+(float)rng.NextDouble()*.010f;
            float cy=.528f+(float)rng.NextDouble()*.035f;
            if(cluster==7 || cluster==9)cy+=.025f;
            float width=.016f+(float)rng.NextDouble()*.014f,height=.020f+(float)rng.NextDouble()*.030f;
            var puffs=new Puff[9];
            for(int i=0;i<puffs.Length;i++){float f=(i-4)/4f;puffs[i]=new Puff{x=cx+f*width*.86f,y=cy+(1-f*f)*height*.31f+(float)rng.NextDouble()*height*.18f,rx=width*(.30f+(float)rng.NextDouble()*.23f),ry=height*(.36f+(float)rng.NextDouble()*.35f)};}
            int x0=Mathf.FloorToInt((cx-width*1.6f)*w),x1=Mathf.CeilToInt((cx+width*1.6f)*w);
            int y0=Mathf.Max(0,Mathf.FloorToInt((cy-height)*h)),y1=Mathf.Min(h-1,Mathf.CeilToInt((cy+height*1.5f)*h));
            for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++)
            {
                float u=(float)x/w,v=(float)y/h,distance=10,localLight=0;
                foreach(var puff in puffs){float dx=(u-puff.x)/puff.rx,dy=(v-puff.y)/puff.ry;float d=Mathf.Sqrt(dx*dx+dy*dy);if(d<distance){distance=d;localLight=Mathf.Clamp01(.62f+dy*.40f-dx*.16f);}}
                float grain=(Mathf.PerlinNoise(u*680,v*850)-.5f)*.058f+(Mathf.PerlinNoise(u*173,v*219)-.5f)*.035f;
                float alpha=1-S(.87f,1.06f,distance+grain);
                alpha*=S(cy-height*.57f,cy-height*.12f,v);
                int index=y*w+((x%w+w)%w);if(alpha<=pixels[index].r/255f)continue;
                float light=S(cy-height*.35f,cy+height*.70f,v);
                light=Mathf.Lerp(.15f,.97f,light*.50f+S(.15f,.9f,localLight)*.50f);light+=grain*.5f;
                pixels[index]=new Color(alpha,Mathf.Clamp01(light),pixels[index].b/255f,1);
            }
        }
        for(int c=0;c<13;c++)
        {
            float cx=(c+.3f)/13,cy=.66f+(float)rng.NextDouble()*.16f,rx=.023f+(float)rng.NextDouble()*.024f,ry=.003f+(float)rng.NextDouble()*.005f;
            int x0=(int)((cx-rx*1.3f)*w),x1=(int)((cx+rx*1.3f)*w);int y0=(int)((cy-ry*2)*h),y1=(int)((cy+ry*2)*h);
            for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++){float dx=((float)x/w-cx)/rx,dy=((float)y/h-cy)/ry;float feather=1-S(.28f,1.0f,dx*dx+dy*dy);int k=y*w+(x%w+w)%w;var old=pixels[k];old.b=(byte)Mathf.Max(old.b,feather*220);pixels[k]=old;}
        }
        var texture=new Texture2D(w,h,TextureFormat.RGB24,true,true);texture.SetPixels32(pixels);texture.Apply(true,false);
        string path=Root+"/Textures/PainterlyCloudAtlas.png";File.WriteAllBytes(path,texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.sRGBTexture=false;importer.alphaSource=TextureImporterAlphaSource.None;importer.mipmapEnabled=true;importer.wrapModeU=TextureWrapMode.Repeat;importer.wrapModeV=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Bilinear;importer.anisoLevel=0;importer.isReadable=false;importer.maxTextureSize=2048;importer.textureCompression=TextureImporterCompression.CompressedHQ;
        foreach(string platform in new[]{"Android","iPhone"}){var ps=importer.GetPlatformTextureSettings(platform);ps.overridden=true;ps.maxTextureSize=2048;ps.format=TextureImporterFormat.ASTC_6x6;importer.SetPlatformTextureSettings(ps);}
        var desktop=importer.GetPlatformTextureSettings("Standalone");desktop.overridden=true;desktop.maxTextureSize=2048;desktop.format=TextureImporterFormat.BC7;importer.SetPlatformTextureSettings(desktop);
        var web=importer.GetPlatformTextureSettings("WebGL");web.overridden=true;web.maxTextureSize=2048;web.format=TextureImporterFormat.DXT5;importer.SetPlatformTextureSettings(web);
        importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}

