using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using InsectSpace.Rendering;

// Unity CLI authoring only. All native assets are serialized by the connected Editor.
public static class BuildHeroStudy
{
    const string Root = "Assets/InsectSpace/Rendering/HeroStudy";
    const string ScenePath = "Assets/InsectSpace/Scenes/HeroCloudStudy.unity";
    static System.Random random;
    static Color C(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }
    static float R(float a,float b) => Mathf.Lerp(a,b,(float)random.NextDouble());
    static float S(float a,float b,float x) { float t=Mathf.Clamp01((x-a)/(b-a));return t*t*(3-2*t); }
    static void Folder(string p) { var a=p.Split('/');var s=a[0];for(int i=1;i<a.Length;i++){if(!AssetDatabase.IsValidFolder(s+"/"+a[i]))AssetDatabase.CreateFolder(s,a[i]);s+="/"+a[i];} }
    public static object Run()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before authoring.");
        for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
            if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save scenes first.");
        Folder(Root+"/Meshes");Folder(Root+"/Materials");Folder(Root+"/Profiles");Folder(Root+"/Textures");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var modelPath=Root+"/Valerya/Valerya_BreathingIdle.fbx";
        var importer=AssetImporter.GetAtPath(modelPath) as ModelImporter;
        if(importer==null)throw new InvalidOperationException("Import the Mixamo-rigged Valerya FBX first.");
        importer.globalScale=100;importer.importNormals=ModelImporterNormals.Calculate;importer.normalSmoothingAngle=100;
        importer.animationType=ModelImporterAnimationType.Human;importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation=true;
        var clips=importer.defaultClipAnimations;
        foreach(var c in clips){c.name="BreathingIdle";c.loopTime=true;c.loopPose=true;c.lockRootRotation=true;c.lockRootHeightY=true;c.lockRootPositionXZ=true;}
        importer.clipAnimations=clips;
        importer.importTangents=ModelImporterTangents.None;importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;
        importer.isReadable=false;importer.SaveAndReimport();
        var model=AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if(!model)throw new InvalidOperationException("Model failed to import.");
        var skyShader=Shader.Find("InsectSpace/Hero Cumulus Sky");
        var groundShader=Shader.Find("InsectSpace/Hero Meadow Atmosphere");
        var heroShader=Shader.Find("InsectSpace/Painterly Hero");
        foreach(var shader in new[]{skyShader,groundShader,heroShader})
            if(!shader || ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("Required shader is missing or broken.");
        var atlas=CloudAtlas();
        var profiles=new StylizedSkyProfile[4];
        var names=new[]{"Daylight","GoldenHour","Moonrise","Mist"};
        for(int i=0;i<profiles.Length;i++)
        {
            var source=AssetDatabase.LoadAssetAtPath<StylizedSkyProfile>("Assets/InsectSpace/Rendering/Sky/Profiles/"+names[i==3?0:i]+".asset");
            string path=Root+"/Profiles/"+names[i]+".asset";
            var p=AssetDatabase.LoadAssetAtPath<StylizedSkyProfile>(path);
            if(!p){p=ScriptableObject.CreateInstance<StylizedSkyProfile>();AssetDatabase.CreateAsset(p,path);}
            EditorUtility.CopySerialized(source,p);p.name=names[i];profiles[i]=p;
            p.cloudSpeed=.00022f;p.celestialDirection=new Vector3(-.8f,.6f,-.2f).normalized;
            p.overrideFog=true;p.fogEnabled=true;p.fogMode=FogMode.Linear;p.fogStart=58;p.fogEnd=180;p.atmosphereStrength=0;
            if(i==0)
            {
                p.zenith=C("#2878AA");p.middle=C("#58ABCB");p.horizon=C("#C1DAD1");
                p.cloudLight=C("#FFF6DE");p.cloudShade=C("#72A9BA");p.ambient=C("#A1B9C1");
                p.sunlight=C("#FFF1D5");p.lightIntensity=1.10f;p.coverage=1;p.exposure=1;
            }
            if(i==3)
            {
                p.zenith=C("#8B959C");p.middle=C("#9DA8AC");p.horizon=C("#B1BBBA");
                p.cloudLight=p.horizon;p.cloudShade=p.middle;p.coverage=0;p.celestialColor=Color.black;
                p.sunlight=C("#D3DED7");p.ambient=C("#A2B3B6");p.lightIntensity=.63f;p.exposure=1;
                p.fogStart=7;p.fogEnd=38;p.atmosphereStrength=1;
            }
            EditorUtility.SetDirty(p);
        }
        var sky=Mat("HeroCloudSky",skyShader);sky.SetTexture("_CloudAtlas",atlas);
        sky.EnableKeyword("_SKY_DETAILS");ApplySky(sky,profiles[0]);
        var turf=Mat("Turf",groundShader);var grass=Mat("Grass",groundShader);
        foreach(var m in new[]{turf,grass}) {m.SetColor("_BaseColor",Color.white);m.SetColor("_ShadowTint",C("#527F81"));m.SetFloat("_VertexColor",1);m.SetFloat("_Wind",m==grass?.025f:0);m.SetFloat("_CloudShadow",.62f);}
        var heroMaterials=new Material[7];
        for(int i=0;i<7;i++)
        {
            var matches=Directory.GetFiles(Root+"/Valerya","albedo_"+i.ToString("00")+".*").Where(p=>!p.EndsWith(".meta")).ToArray();
            if(matches.Length!=1)throw new InvalidOperationException("Missing character albedo "+i);
            var path=matches[0].Replace('\\','/');
            var ti=(TextureImporter)AssetImporter.GetAtPath(path);ti.sRGBTexture=true;ti.maxTextureSize=2048;ti.mipmapEnabled=true;ti.isReadable=false;
            ti.textureCompression=TextureImporterCompression.CompressedHQ;ti.SaveAndReimport();
            var m=Mat("Valerya_"+i,heroShader);m.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            m.SetColor("_BaseColor",Color.white);m.SetColor("_ShadowTint",C("#A8B9CA"));m.SetFloat("_PaletteShift",i==0||i==4||i==5?0:1);
            m.SetFloat("_Softness",i==4?.72f:i==0?.42f:.15f);heroMaterials[i]=m;
        }
        AssetDatabase.SaveAssets();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        // Reload references after scene replacement, which can unload unreferenced objects.
        model=AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);sky=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/HeroCloudSky.mat");
        turf=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Turf.mat");grass=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Grass.mat");
        for(int i=0;i<profiles.Length;i++)profiles[i]=AssetDatabase.LoadAssetAtPath<StylizedSkyProfile>(Root+"/Profiles/"+names[i]+".asset");
        for(int i=0;i<7;i++)heroMaterials[i]=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Valerya_"+i+".mat");
        random=new System.Random(102026);
        var terrainRoot=new GameObject("Open rolling meadow - procedural seed 102026").transform;
        Ground(terrainRoot,turf);Grasses(terrainRoot,grass);MeadowDetails(terrainRoot,groundShader);
        var character=(GameObject)PrefabUtility.InstantiatePrefab(model);character.name="Valerya - Mixamo humanoid - breathing idle";
        character.transform.position=new Vector3(.12f,Height(.12f,0),0);character.transform.rotation=Quaternion.Euler(0,170,0);
        foreach(var renderer in character.GetComponentsInChildren<Renderer>())
        {
            var mats=renderer.sharedMaterials;
            for(int i=0;i<mats.Length;i++)
            {
                var digits=new string(mats[i].name.Where(char.IsDigit).ToArray());
                int index=int.Parse(digits);mats[i]=heroMaterials[index];
            }
            renderer.sharedMaterials=mats;renderer.shadowCastingMode=ShadowCastingMode.On;
        }
        var clip=AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>().Single(c=>c.name=="BreathingIdle");
        var animator=character.GetComponent<Animator>();if(!animator)animator=character.AddComponent<Animator>();
        string controllerPath=Root+"/ValeryaIdle.controller";
        var ac=AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if(!ac)ac=AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        var stateMachine=ac.layers[0].stateMachine;
        var state=stateMachine.states.Select(s=>s.state).FirstOrDefault(s=>s.name=="BreathingIdle")??stateMachine.AddState("BreathingIdle");
        state.motion=clip;stateMachine.defaultState=state;EditorUtility.SetDirty(ac);
        animator.runtimeAnimatorController=ac;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        if(!animator.avatar || !animator.avatar.isValid || !animator.avatar.isHuman)throw new InvalidOperationException("Mixamo humanoid avatar did not import.");
        clip.SampleAnimation(character,0);
        var camera=new GameObject("Hero Study Camera",typeof(Camera),typeof(AudioListener)).GetComponent<Camera>();camera.tag="MainCamera";
        camera.transform.position=new Vector3(0,1.31f,-2.62f);camera.transform.rotation=Quaternion.Euler(0,0,0);
        camera.fieldOfView=39;camera.nearClipPlane=.05f;camera.farClipPlane=220;camera.clearFlags=CameraClearFlags.Skybox;
        var cameraData=camera.GetUniversalAdditionalCameraData();cameraData.renderPostProcessing=true;
        cameraData.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;cameraData.antialiasingQuality=AntialiasingQuality.High;
        camera.gameObject.AddComponent<HeroStudyFraming>().Configure(camera,character.transform);
        var sun=new GameObject("Warm key light").AddComponent<Light>();sun.type=LightType.Directional;sun.transform.rotation=Quaternion.Euler(38,-32,0);
        sun.color=profiles[0].sunlight;sun.intensity=profiles[0].lightIntensity;sun.shadows=LightShadows.Soft;sun.shadowStrength=.82f;sun.shadowBias=.025f;
        RenderSettings.sun=sun;RenderSettings.skybox=sky;RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=profiles[0].ambient;
        RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogColor=profiles[0].horizon;RenderSettings.fogStartDistance=58;RenderSettings.fogEndDistance=180;
        var controller=new GameObject("Painterly atmosphere - day sunset night mist").AddComponent<StylizedSkyController>();
        var so=new SerializedObject(controller);so.FindProperty("skyTemplate").objectReferenceValue=sky;so.FindProperty("targetCamera").objectReferenceValue=camera;so.FindProperty("directionalLight").objectReferenceValue=sun;
        var ps=so.FindProperty("profiles");ps.arraySize=profiles.Length;for(int i=0;i<profiles.Length;i++)ps.GetArrayElementAtIndex(i).objectReferenceValue=profiles[i];so.ApplyModifiedPropertiesWithoutUndo();
        terrainRoot.gameObject.AddComponent<HeroStudyAtmosphere>().Configure(controller,terrainRoot.GetComponentsInChildren<Renderer>());
        new GameObject("Local hero study controls").AddComponent<HeroStudyControls>().Configure(controller,animator);
        AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene,ScenePath);
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);Selection.activeGameObject=character;
        return new {scene=ScenePath,character=character.name,triangles=54074,rigged=animator.avatar.isHuman,animationSeconds=clip.length,atlas=new[]{atlas.width,atlas.height},grassChunks=terrainRoot.GetComponent<MeadowGrassLod>().ChunkCount};
    }
    static Material Mat(string name,Shader shader)
    {string p=Root+"/Materials/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(p);if(!m){m=new Material(shader);AssetDatabase.CreateAsset(m,p);}m.shader=shader;EditorUtility.SetDirty(m);return m;}
    static void ApplySky(Material m,StylizedSkyProfile p)
    {
        m.SetColor("_Zenith",p.zenith);m.SetColor("_Middle",p.middle);m.SetColor("_Horizon",p.horizon);m.SetColor("_CloudLight",p.cloudLight);m.SetColor("_CloudShade",p.cloudShade);m.SetColor("_SunColor",p.celestialColor);
        m.SetVector("_SunDirection",p.celestialDirection);m.SetFloat("_Exposure",p.exposure);m.SetFloat("_Coverage",p.coverage);m.SetFloat("_Night",0);m.SetFloat("_SunSize",p.celestialSize);m.SetVector("_CloudOffset",Vector4.zero);
    }
    struct Puff { public float x,y,rx,ry,light; }
    public static object RebuildCloudAtlas()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before authoring.");
        var atlas=CloudAtlas();AssetDatabase.SaveAssets();
        return new {atlas=AssetDatabase.GetAssetPath(atlas),width=atlas.width,height=atlas.height,encoding="R signed contour distance; G painted light"};
    }
    static Texture2D CloudAtlas()
    {
        const int w=4096,h=2048;var pixels=new Color32[w*h];random=new System.Random(81203);
        // Multi-tier cumulus; R encodes a smooth signed contour distance, G painted light.
        // The reference-facing sector is deliberately asymmetrical, with open blue breathing space.
        for(int c=0;c<19;c++)
        {
            float cx=c/19f+R(-.006f,.006f),baseY=.504f+R(-.003f,.014f),width=R(.017f,.032f),height=R(.030f,.062f);
            if(c==8){cx=.459f;width=.022f;height=.050f;}
            if(c==10){cx=.561f;width=.033f;height=.083f;}
            if(c==9){cx=.514f;width=.013f;height=.033f;}
            var puffs=new List<Puff>();
            // Join the cloud tiers so their caps do not leave enclosed sky holes.
            puffs.Add(new Puff{x=cx+width*.10f,y=baseY+height*.16f,rx=width*.94f,ry=height*.43f,light=.42f});
            for(int tier=0;tier<3;tier++)
            {
                int count=8-tier*2;float spread=width*(1-tier*.28f);
                for(int k=0;k<count;k++)
                {
                    float f=count>1?(float)k/(count-1)*2-1:0;
                    var puff=new Puff {x=cx+f*spread+R(-.002f,.002f)+tier*width*.12f,y=baseY+tier*height*.27f+(1-f*f)*height*.14f+R(-.006f,.006f),
                        rx=width*R(.19f,.34f),ry=height*R(.17f,.31f),light=.34f+tier*.22f};
                    puffs.Add(puff);
                    // Small asymmetric scallops break the broad ellipse outline.
                    puffs.Add(new Puff{x=puff.x-puff.rx*.42f,y=puff.y+puff.ry*.69f,rx=puff.rx*.52f,ry=puff.ry*.52f,light=puff.light+.10f});
                }
            }
            PaintCloud(pixels,w,h,puffs,baseY-.014f);
        }
        // Small separated cotton clouds overhead instead of broad blurry cirrus streaks.
        foreach(float cx in new[]{.413f,.475f,.536f,.552f,.604f,.22f,.77f})
        {
            float cy=R(.585f,.612f),rx=R(.003f,.005f);var puffs=new List<Puff>();
            for(int k=0;k<5;k++)puffs.Add(new Puff{x=cx+(k-2)*rx*.44f,y=cy+(1-Mathf.Abs(k-2)/2f)*rx*.65f,rx=rx*.60f,ry=rx*R(.9f,1.25f),light=.82f});
            PaintCloud(pixels,w,h,puffs,cy-rx*1.15f);
        }
        var texture=new Texture2D(w,h,TextureFormat.RGB24,true,true);texture.SetPixels32(pixels);texture.Apply(true,false);
        string path=Root+"/Textures/LayeredCumulus.png";File.WriteAllBytes(path,texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);var ti=(TextureImporter)AssetImporter.GetAtPath(path);
        ti.sRGBTexture=false;ti.alphaSource=TextureImporterAlphaSource.None;ti.mipmapEnabled=true;ti.wrapModeU=TextureWrapMode.Repeat;ti.wrapModeV=TextureWrapMode.Clamp;ti.filterMode=FilterMode.Trilinear;ti.isReadable=false;ti.maxTextureSize=4096;ti.textureCompression=TextureImporterCompression.Uncompressed;
        foreach(string platform in new[]{"Android","iPhone"}) {var p=ti.GetPlatformTextureSettings(platform);p.overridden=true;p.maxTextureSize=2048;p.format=TextureImporterFormat.ASTC_6x6;ti.SetPlatformTextureSettings(p);}
        // Avoid block compression shifting the distance-field zero contour on desktop.
        var desktop=ti.GetPlatformTextureSettings("Standalone");desktop.overridden=true;desktop.maxTextureSize=4096;desktop.format=TextureImporterFormat.RGBA32;ti.SetPlatformTextureSettings(desktop);
        ti.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
    static void PaintCloud(Color32[] pixels,int w,int h,List<Puff> puffs,float bottom)
    {
        float top=puffs.Max(p=>p.y+p.ry);
        foreach(var p in puffs)
        {
            const float distanceRange=8f;
            int x0=Mathf.FloorToInt((p.x-p.rx)*w-distanceRange),x1=Mathf.CeilToInt((p.x+p.rx)*w+distanceRange);
            int y0=Mathf.Max(0,Mathf.FloorToInt((p.y-p.ry)*h-distanceRange)),y1=Mathf.Min(h-1,Mathf.CeilToInt((p.y+p.ry)*h+distanceRange));
            for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++)
            {
                float u=(x+.5f)/w,v=(y+.5f)/h,dx=(u-p.x)/p.rx,dy=(v-p.y)/p.ry;
                // Large scallops define the contour. Fine texture never perturbs its edge.
                float distance=(1-Mathf.Sqrt(dx*dx+dy*dy))*Mathf.Min(p.rx*w,p.ry*h);
                distance=Mathf.Min(distance,(v-bottom)*h);
                float shape=Mathf.Clamp01(.5f+distance/(distanceRange*2));if(shape<=0)continue;
                float alpha=S(-.65f,.65f,distance);
                float grain=(Mathf.PerlinNoise(u*410,v*520)-.5f)*.018f;
                float band=S(-.04f,.10f,dy+dx*.22f+grain);
                float vertical=S(bottom,top,v);
                float light=Mathf.Clamp01(.25f+vertical*.64f+band*.055f+S(.70f,.91f,dy-dx*.22f)*.07f);
                int k=y*w+(x%w+w)%w;var old=(Color)pixels[k];
                // Front puffs paint lit caps over deeper tiers without softening the outer edge.
                float union=Mathf.Max(old.r,shape);
                float lit=old.r<=.5f?(shape>old.r?light:old.g):Mathf.Lerp(old.g,light,alpha*.32f);
                pixels[k]=new Color(union,lit,0,1);
            }
        }
    }
    static float Height(float x,float z)
    {
        return S(2,19,z)*(.90f+.50f*Mathf.Sin(x*.065f+z*.045f))+.13f*Mathf.Sin(x*.23f+z*.13f)*S(1,10,z);
    }
    static Color Palette(float x,float z)
    {
        float patch=Mathf.PerlinNoise(x*.12f+43,z*.16f+32);
        Color col=Color.Lerp(C("#608D78"),C("#B2BC7D"),S(.21f,.79f,patch));
        col=Color.Lerp(col,C("#5D9B8C"),S(24,70,z)*.55f);
        float path=1-S(.78f,1.38f,Mathf.Abs(x-PathX(z)));
        col=Color.Lerp(col,C("#C8BF94"),path*S(-4,1,z)*.82f);
        return col*Mathf.Lerp(.97f,1.03f,Mathf.PerlinNoise(x*.65f+4,z*.65f+12));
    }
    static float PathX(float z) => 3.6f+Mathf.Sin(z*.13f)*3+z*.03f;
    sealed class Geo
    {
        public List<Vector3> v=new List<Vector3>();public List<Vector3> n=new List<Vector3>();public List<Color> c=new List<Color>();public List<Vector2> uv=new List<Vector2>();public List<int> t=new List<int>();
        public void Vertex(Vector3 p,Color col,Vector2 coord) {v.Add(p);n.Add(Vector3.up);c.Add(col.linear);uv.Add(coord);}
        public void Blade(Vector3 p,float height,float width,float yaw,Color col)
        {
            int k=v.Count;var side=new Vector3(Mathf.Cos(yaw),0,Mathf.Sin(yaw))*width;var bend=new Vector3(.025f,0,.04f);
            Vertex(p-side,col,Vector2.zero);Vertex(p+side,col,Vector2.right);Vertex(p+Vector3.up*height*.57f+bend*.4f-side*.5f,col*1.03f,new Vector2(0,.57f));
            Vertex(p+Vector3.up*height*.57f+bend*.4f+side*.5f,col*1.03f,new Vector2(1,.57f));Vertex(p+Vector3.up*height+bend,col*1.09f,new Vector2(.5f,1));
            t.AddRange(new[]{k,k+2,k+1,k+1,k+2,k+3,k+2,k+4,k+3});
        }
        public void Face(Vector3 a,Vector3 b,Vector3 d,Color color)
        {
            int k=v.Count;var normal=Vector3.Cross(b-a,d-a).normalized;
            foreach(var p in new[]{a,b,d}) {Vertex(p,color,Vector2.zero);n[n.Count-1]=normal;}
            t.AddRange(new[]{k,k+1,k+2});
        }
        public Mesh Mesh() {var m=new Mesh{indexFormat=IndexFormat.UInt32};m.SetVertices(v);m.SetNormals(n);m.SetColors(c);m.SetUVs(0,uv);m.SetTriangles(t,0);m.RecalculateBounds();return m;}
    }
    static Mesh Save(string name,Mesh m)
    {
        string path=Root+"/Meshes/"+name+".asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);m.name=name;
        if(!existing){AssetDatabase.CreateAsset(m,path);return m;}
        existing.Clear();existing.indexFormat=m.indexFormat;existing.vertices=m.vertices;existing.normals=m.normals;existing.colors=m.colors;existing.uv=m.uv;existing.triangles=m.triangles;existing.bounds=m.bounds;existing.UploadMeshData(false);UnityEngine.Object.DestroyImmediate(m);EditorUtility.SetDirty(existing);return existing;
    }
    static GameObject MeshObject(string name,Geo geo,Transform parent,Material material)
    {
        var go=new GameObject(name);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=Save(name,geo.Mesh());
        var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=material;r.shadowCastingMode=ShadowCastingMode.Off;return go;
    }
    static void Ground(Transform parent,Material material)
    {
        var g=new Geo();const int nx=160,nz=180;
        for(int z=0;z<=nz;z++)for(int x=0;x<=nx;x++)
        {float px=(x/(float)nx-.5f)*170,pz=-9+z/(float)nz*160;g.Vertex(new Vector3(px,Height(px,pz),pz),Palette(px,pz),Vector2.zero);}
        for(int z=0;z<nz;z++)for(int x=0;x<nx;x++){int k=z*(nx+1)+x;g.t.AddRange(new[]{k,k+nx+1,k+1,k+1,k+nx+1,k+nx+2});}
        MeshObject("RollingTurf",g,parent,material);
    }
    static void Grasses(Transform parent,Material material)
    {
        var chunks=new List<MeadowGrassLod.Chunk>();
        for(int iz=0;iz<6;iz++)for(int ix=0;ix<6;ix++)
        {
            var high=new Geo();var medium=new Geo();var low=new Geo();
            for(int i=0;i<700;i++)
            {
                float x=-18+ix*6+R(0,6),z=-4+iz*7+R(0,7);
                float patch=Mathf.PerlinNoise(x*.35f+18,z*.35f+8);
                if(patch<.43f || (z>-1 && Mathf.Abs(x-PathX(z))<1.22f) || new Vector2(x-.12f,z).sqrMagnitude<.66f)continue;
                float h=R(.07f,.19f)*Mathf.Lerp(1,1.3f,S(10,38,z));float width=R(.017f,.035f);float yaw=R(0,Mathf.PI);
                var p=new Vector3(x,Height(x,z),z);var col=Palette(x,z)*R(.90f,1.05f);
                high.Blade(p,h,width,yaw,col);if(i%3<2)medium.Blade(p,h,width,yaw,col);if(i%3==0)low.Blade(p,h,width,yaw,col);
                high.Blade(p+new Vector3(.045f,0,.025f),h*.74f,width*.75f,yaw+.9f,col);
                if(i%3<2)medium.Blade(p+new Vector3(.045f,0,.025f),h*.74f,width*.75f,yaw+.9f,col);
            }
            string name="Blades_"+ix+"_"+iz;var go=MeshObject(name,high,parent,material);
            chunks.Add(new MeadowGrassLod.Chunk{filter=go.GetComponent<MeshFilter>(),high=go.GetComponent<MeshFilter>().sharedMesh,medium=Save(name+"_Medium",medium.Mesh()),low=Save(name+"_Low",low.Mesh())});
        }
        parent.gameObject.AddComponent<MeadowGrassLod>().Configure(chunks.ToArray());
    }
    static void MeadowDetails(Transform parent,Shader shader)
    {
        var detail=Mat("MeadowDetails",shader);detail.SetColor("_BaseColor",Color.white);
        detail.SetFloat("_VertexColor",1);detail.SetColor("_ShadowTint",C("#477D83"));detail.SetFloat("_Wind",0);detail.SetFloat("_CloudShadow",.62f);
        var flowers=Mat("MeadowFlowers",shader);flowers.SetColor("_BaseColor",Color.white);
        flowers.SetFloat("_VertexColor",1);flowers.SetColor("_ShadowTint",C("#699A86"));flowers.SetFloat("_Wind",.02f);flowers.SetFloat("_CloudShadow",.35f);
        var flora=new Geo();
        var clusters=new[]{new Vector2(-1.8f,1.1f),new Vector2(1.65f,2.7f),new Vector2(-3.4f,4.7f),new Vector2(3.5f,6.8f),new Vector2(-5.6f,7.8f),new Vector2(7.8f,12.5f),new Vector2(-8.2f,17)};
        for(int c=0;c<clusters.Length;c++)for(int i=0;i<21;i++)
        {
            var center=clusters[c];float angle=R(0,Mathf.PI*2),radius=Mathf.Sqrt(R(0,1))*.68f;
            float x=center.x+Mathf.Cos(angle)*radius,z=center.y+Mathf.Sin(angle)*radius;
            var p=new Vector3(x,Height(x,z),z);float h=R(.18f,.36f);
            var stem=C("#668D6D");flora.Blade(p,h,.014f,R(0,Mathf.PI),stem);
            flora.Blade(p,h*.65f,.065f,R(0,Mathf.PI),C("#80A087"));
            var top=p+Vector3.up*h;var col=c%3==0?C("#F3E6B0"):C("#E7C966");
            for(int petal=0;petal<5;petal++)
            {
                float a=petal*Mathf.PI*2/5;var outward=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));var side=Vector3.Cross(Vector3.up,outward);
                var tip=top+outward*.075f+Vector3.up*.018f;
                flora.Face(top,tip-side*.045f,tip+side*.045f,col*R(.95f,1.05f));
            }
            flora.Face(top+new Vector3(-.023f,.008f,-.019f),top+new Vector3(0,.008f,.028f),top+new Vector3(.023f,.008f,-.019f),C("#C59B47"));
        }
        MeshObject("WildflowerClusters",flora,parent,flowers);
        var rocks=new Geo();
        foreach(var p in new[]{new Vector3(-3.8f,0,3.1f),new Vector3(-4.4f,0,3.6f),new Vector3(7.3f,0,9),new Vector3(-9,0,13),new Vector3(10,0,21)})
        {
            var center=p+Vector3.up*Height(p.x,p.z);float size=R(.35f,.75f);
            Blob(rocks,center,new Vector3(size,R(.28f,.58f),size*.73f),C("#8B9E9B"),7);
        }
        MeshObject("WeatheredMeadowStones",rocks,parent,detail).GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.On;
        var shrubs=new Geo();
        foreach(var p in new[]{new Vector3(-5,0,5),new Vector3(8.2f,0,10.4f),new Vector3(-9.2f,0,15),new Vector3(11.4f,0,22),new Vector3(-15,0,24)})
        {
            var center=p+Vector3.up*Height(p.x,p.z);
            for(int i=0;i<4;i++)Blob(shrubs,center+new Vector3(R(-.4f,.4f),R(.05f,.2f),R(-.35f,.35f)),new Vector3(.62f,.65f,.55f),Color.Lerp(C("#3D7772"),C("#729586"),R(0,1)),8);
        }
        MeshObject("RoundedMeadowShrubs",shrubs,parent,detail).GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.On;
        var trees=new Geo();
        foreach(var p in new[]{new Vector3(-16,0,22),new Vector3(-19,0,28),new Vector3(17,0,25),new Vector3(21,0,34),new Vector3(-24,0,40),new Vector3(27,0,49)})
        {
            var center=p+Vector3.up*Height(p.x,p.z);float h=R(3.3f,4.5f);
            Blob(trees,center,new Vector3(.17f,h*.66f,.17f),C("#63776A"),6);
            Blob(trees,center+Vector3.up*(h*.48f),new Vector3(1.8f,h*.73f,1.5f),Color.Lerp(C("#4D8582"),C("#82A591"),R(0,1)),10);
        }
        MeshObject("DistantMeadowGrove",trees,parent,detail).GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.On;
    }
    static void Blob(Geo geo,Vector3 p,Vector3 size,Color color,int sides)
    {
        const int rings=4;var vertices=new Vector3[rings,sides];
        for(int ring=0;ring<rings;ring++)for(int s=0;s<sides;s++)
        {
            float t=ring/(float)(rings-1),angle=s*Mathf.PI*2/sides;
            float radius=(ring==0?.78f:ring==3?.32f:1)*R(.88f,1.12f);
            vertices[ring,s]=p+Vector3.Scale(new Vector3(Mathf.Cos(angle)*radius,t,Mathf.Sin(angle)*radius),size);
        }
        for(int ring=0;ring<rings-1;ring++)for(int s=0;s<sides;s++)
        {
            int next=(s+1)%sides;var col=color*Mathf.Lerp(.86f,1.10f,ring/(float)(rings-1));
            geo.Face(vertices[ring,s],vertices[ring+1,s],vertices[ring,next],col);
            geo.Face(vertices[ring,next],vertices[ring+1,s],vertices[ring+1,next],col);
        }
        for(int s=0;s<sides;s++)geo.Face(vertices[rings-1,s],p+Vector3.up*(size.y*1.04f),vertices[rings-1,(s+1)%sides],color*1.1f);
    }
}
