using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;

// Presentation-only authoring. Run through Unity CLI run_script; never edits scene YAML.
public static class RebuildMeadow
{
    const string Root = "Assets/InsectSpace/Rendering/Showcase/Painterly";
    const string Source = "Assets/InsectSpace/Rendering/Showcase/KayKitAdventurers";
    static System.Random rng;
    static Material foliage, terrain, dirt, wood, stone, grass, flower, hero;
    static Transform vegetation;
    static Color C(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }
    static float R(float a, float b) { return Mathf.Lerp(a,b,(float)rng.NextDouble()); }

    public static object Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before rebuilding.");
        var current = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (current.path != "Assets/InsectSpace/Scenes/MeadowShowcase.unity" || current.isDirty)
            throw new InvalidOperationException("Open the saved MeadowShowcase scene first.");
        Folder(Root); rng = new System.Random(42017);
        AssetDatabase.Refresh();
        var shader=Shader.Find("InsectSpace/Meadow Painterly");
        if (shader==null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Painterly shader must compile first.");
        // Resolve the replacement and all inputs before clearing the old showcase.
        var model=AssetDatabase.LoadAssetAtPath<GameObject>(Source+"/Mage.fbx");
        if (model==null) throw new InvalidOperationException("Missing licensed KayKit Mage.");
        var idleSource=AssetDatabase.LoadAllAssetsAtPath(Source+"/Mage.fbx").OfType<AnimationClip>().First(c=>c.name=="2H_Melee_Idle");
        foreach(var go in current.GetRootGameObjects()) UnityEngine.Object.DestroyImmediate(go);
        // Turf and blades deliberately share a palette and tint: visible grass silhouettes,
        // without dark individual blades fighting a pale lawn underneath.
        var meadowTint=Color.white;
        terrain=Mat("Ground",shader,meadowTint,0); grass=Mat("Grass",shader,meadowTint,.075f);
        dirt=Mat("Clearing",shader,Color.white,0);
        foliage=Mat("Foliage",shader,Color.white,0); wood=Mat("Bark",shader,Color.white,0);
        stone=Mat("Stone",shader,Color.white,0); flower=Mat("Flowers",shader,Color.white,.04f);
        hero=Mat("Mage",shader,Color.white,0); hero.SetFloat("_VertexColor",0);
        hero.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Source+"/mage_texture.png"));
        vegetation=new GameObject("Meadow vegetation - procedural, seed 42017").transform;
        Ground(); Grass(); Scenery();
        var character=Character(model,idleSource);
        CameraAndLight(); Grid();
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(current);
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(current.path);
        Selection.activeGameObject=character;
        return new {scene=current.path, character=character.name, meshes=AssetDatabase.FindAssets("t:Mesh",new[]{Root}).Length,
            renderers=current.GetRootGameObjects().Sum(g=>g.GetComponentsInChildren<Renderer>(true).Length), seed=42017};
    }

    static Material Mat(string name,Shader shader,Color tint,float wind)
    {
        string path=Root+"/"+name+".mat";
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(m==null) {m=new Material(shader); AssetDatabase.CreateAsset(m,path);}
        m.shader=shader; m.SetColor("_BaseColor",tint); m.SetColor("_ShadowTint",C("#557F88"));
        m.SetFloat("_Wind",wind); m.SetFloat("_VertexColor",1); EditorUtility.SetDirty(m); return m;
    }
    static void Folder(string p)
    { var a=p.Split('/'); string s=a[0]; for(int i=1;i<a.Length;i++){string n=s+"/"+a[i];if(!AssetDatabase.IsValidFolder(n))AssetDatabase.CreateFolder(s,a[i]);s=n;} }
    static Mesh SaveMesh(string name,Mesh mesh)
    {
        string path=Root+"/"+name+".asset"; var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(existing!=null)
        {
            // CopySerialized can leave a previously uploaded native vertex/index buffer stale.
            // Explicit setters invalidate the GPU data while preserving the asset GUID.
            existing.Clear(); existing.indexFormat=mesh.indexFormat; existing.name=name;
            existing.vertices=mesh.vertices; existing.normals=mesh.normals;
            existing.colors=mesh.colors; existing.uv=mesh.uv; existing.triangles=mesh.triangles;
            existing.bounds=mesh.bounds; existing.UploadMeshData(false);
            UnityEngine.Object.DestroyImmediate(mesh);EditorUtility.SetDirty(existing);return existing;
        }
        mesh.name=name;AssetDatabase.CreateAsset(mesh,path);return mesh;
    }
    sealed class Geo
    {
        public List<Vector3> v=new List<Vector3>(); public List<int> t=new List<int>();
        public List<Vector3> n=new List<Vector3>(); public List<Color> c=new List<Color>(); public List<Vector2> uv=new List<Vector2>();
        public void Tri(Vector3 a,Vector3 b,Vector3 d,Color col, bool up=false)
        { int k=v.Count;v.Add(a);v.Add(b);v.Add(d);t.Add(k);t.Add(k+1);t.Add(k+2);var normal=up?Vector3.up:Vector3.Cross(b-a,d-a).normalized; for(int i=0;i<3;i++){n.Add(normal);c.Add(col.linear);uv.Add(Vector2.zero);} }
        public void Blade(Vector3 p,float h,float w,float yaw,Color baseCol,Color tipCol)
        {
            Vector3 side=new Vector3(Mathf.Cos(yaw),0,Mathf.Sin(yaw))*w;
            Vector3 bend=new Vector3(-Mathf.Sin(yaw),0,Mathf.Cos(yaw))*h*.34f;
            int k=v.Count;
            v.Add(p-side);v.Add(p+side);v.Add(p+Vector3.up*h*.54f+bend*.3f-side*.54f);v.Add(p+Vector3.up*h*.54f+bend*.3f+side*.54f);v.Add(p+Vector3.up*h+bend);
            t.AddRange(new[]{k,k+2,k+1,k+1,k+2,k+3,k+2,k+4,k+3});
            for(int i=0;i<5;i++){n.Add(new Vector3(.08f,1,.1f).normalized);c.Add((i<2?baseCol:Color.Lerp(baseCol,tipCol,i==4?1:.55f)).linear);}
            uv.AddRange(new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(0,.54f),new Vector2(1,.54f),new Vector2(.5f,1)});
        }
        public Mesh Mesh(){var m=new Mesh();m.indexFormat=IndexFormat.UInt32;m.SetVertices(v);m.SetTriangles(t,0);m.SetNormals(n);m.SetColors(c);m.SetUVs(0,uv);m.RecalculateBounds();return m;}
    }
    static GameObject MeshObject(string name,Geo g,Material mat,Transform parent=null,bool cast=true)
    {
        var go=new GameObject(name);if(parent!=null)go.transform.SetParent(parent,false);
        go.AddComponent<MeshFilter>().sharedMesh=SaveMesh(name,g.Mesh());
        var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=mat;r.shadowCastingMode=cast?ShadowCastingMode.On:ShadowCastingMode.Off;
        return go;
    }
    static float Clearing(float x,float z)
    { return Mathf.Sqrt(x*x/(3.7f*3.7f)+(z-.7f)*(z-.7f)/(2.6f*2.6f)); }
    static float Path(float x,float z) { return Mathf.Abs(x-(Mathf.Sin(z*.32f)*1.15f+.35f)); }
    static float Height(float x,float z)
    { float edge=Mathf.SmoothStep(0,1,Mathf.Clamp01((Clearing(x,z)-1)*.7f));return edge*(.12f+Mathf.PerlinNoise(x*.19f+30,z*.19f+50)*.65f); }
    static bool IsDirt(float x,float z)
    {return Clearing(x,z)<1f+.045f*Mathf.Sin(x*4+z*2) || Path(x,z)<.72f+.1f*Mathf.Sin(z*3);}
    static Color Palette(float x,float z)
    {
        float k=Mathf.PerlinNoise(x*.27f+40,z*.27f+12);
        Color col=Color.Lerp(C("#247B70"),C("#78A474"),Mathf.SmoothStep(0,1,k));
        float near=Mathf.Clamp01((-z-3.5f)*.17f);
        if(Mathf.Abs(x)>4) col=Color.Lerp(col,C("#183C43"),near*.75f);
        return col;
    }
    static void Ground()
    {
        Geo turf=new Geo(), clearing=new Geo();int size=112;float step=.2857143f;
        var points=new Vector3[size+1,size+1];
        for(int z=0;z<=size;z++)for(int x=0;x<=size;x++)
        {float px=(x-size/2)*step+(x>0&&x<size?R(-.10f,.10f):0);float pz=(z-size/2)*step+(z>0&&z<size?R(-.1f,.1f):0);points[x,z]=new Vector3(px,Height(px,pz),pz);}
        for(int z=0;z<size;z++)for(int x=0;x<size;x++)
        {
            var a=points[x,z];var b=points[x+1,z];var c=points[x,z+1];var d=points[x+1,z+1];
            GroundTri(turf,clearing,a,c,b);GroundTri(turf,clearing,b,c,d);
        }
        MeshObject("MeadowTerrain",turf,terrain);
        MeshObject("Ochre clearing and winding footpath",clearing,dirt);
    }
    static void GroundTri(Geo turf,Geo dirtGeo,Vector3 a,Vector3 b,Vector3 c)
    {
        Vector3 p=(a+b+c)/3;Color col;
        if(IsDirt(p.x,p.z)) { col=Color.Lerp(C("#B7A580"),C("#D2BE92"),Mathf.PerlinNoise(p.x*.6f+20,p.z*.6f+30)); dirtGeo.Tri(a,b,c,col,true); }
        else {col=Palette(p.x,p.z)*R(.98f,1.02f);turf.Tri(a,b,c,col,true);}
    }
    static void Grass()
    {
        // Chunked, deterministic mesh generation, with empty terrain reserved for path/clearing.
        var chunks=new List<InsectSpace.Rendering.MeadowGrassLod.Chunk>();
        for(int iz=0;iz<6;iz++)for(int ix=0;ix<6;ix++)
        {
            Geo g=new Geo();
            for(int i=0;i<550;i++)
            {
                float x=-12+ix*4+R(0,4),z=-9+iz*4+R(0,4);
                if(IsDirt(x,z))continue;
                float patch=Mathf.PerlinNoise(x*.65f+21,z*.65f+77);
                if(R(0,1)>.35f+.65f*patch)continue;
                float h=R(.12f,.30f)*Mathf.Lerp(.7f,1.15f,patch);
                Color col=Palette(x,z);
                for(int b=0;b<3;b++)
                {var p=new Vector3(x+R(-.13f,.13f),0,z+R(-.13f,.13f));p.y=Height(p.x,p.z)+.005f;g.Blade(p,h*R(.6f,1.25f),R(.037f,.060f),.3f+patch*1.7f+R(-.4f,.4f),col*.98f,col*1.08f);}
            }
            if(g.v.Count>0)
            {
                string name="Grass_"+ix+"_"+iz;
                var go=MeshObject(name,g,grass,vegetation,false);
                chunks.Add(new InsectSpace.Rendering.MeadowGrassLod.Chunk {
                    filter=go.GetComponent<MeshFilter>(),high=go.GetComponent<MeshFilter>().sharedMesh,
                    medium=SaveMesh(name+"_Medium",GrassSubset(g,2,3)),low=SaveMesh(name+"_Low",GrassSubset(g,1,3))});
            }
        }
        vegetation.gameObject.AddComponent<InsectSpace.Rendering.MeadowGrassLod>().Configure(chunks.ToArray());
    }
    static Mesh GrassSubset(Geo source,int keep,int period)
    {
        // A blade is exactly five vertices / nine indices. Spatial sampling keeps every chunk.
        Geo g=new Geo();
        for(int b=0;b<source.v.Count/5;b++)
        {
            if(b%period>=keep)continue;int s=b*5,k=g.v.Count;
            for(int j=0;j<5;j++){g.v.Add(source.v[s+j]);g.n.Add(source.n[s+j]);g.c.Add(source.c[s+j]);g.uv.Add(source.uv[s+j]);}
            g.t.AddRange(new[]{k,k+2,k+1,k+1,k+2,k+3,k+2,k+4,k+3});
        }
        return g.Mesh();
    }
    static Geo Boulder(float radius,Color color,int sides=9,int rings=6)
    {
        Geo g=new Geo();var p=new Vector3[rings+1,sides];
        for(int j=0;j<=rings;j++)for(int i=0;i<sides;i++)
        {float phi=j*Mathf.PI/rings;float yaw=i*6.28318f/sides;float rad=radius*R(.92f,1.08f);p[j,i]=j==0?Vector3.up*radius:j==rings?Vector3.down*radius:new Vector3(Mathf.Sin(phi)*Mathf.Cos(yaw),Mathf.Cos(phi),Mathf.Sin(phi)*Mathf.Sin(yaw))*rad;}
        for(int j=0;j<rings;j++)for(int i=0;i<sides;i++)
        {int next=(i+1)%sides;Color col=color*R(.97f,1.03f);if(j>0)g.Tri(p[j,i],p[j,next],p[j+1,i],col);if(j<rings-1)g.Tri(p[j,next],p[j+1,next],p[j+1,i],col);}
        return g;
    }
    static GameObject Rock(string name,Vector3 pos,Vector3 scale,Color tint,Transform parent=null)
    {var go=MeshObject(name,Boulder(1,tint,7,4),stone,parent);go.transform.localPosition=pos;go.transform.localScale=scale;go.transform.localRotation=Quaternion.Euler(R(-10,10),R(0,360),R(-8,8));return go;}
    static void Branch(Geo g,Vector3 a,Vector3 b,float ra,float rb,Color tint)
    {
        var q=Quaternion.FromToRotation(Vector3.up,(b-a).normalized);int count=7;
        for(int i=0;i<count;i++)
        {float t=i*6.28318f/count,u=(i+1)*6.28318f/count;Vector3 d=q*new Vector3(Mathf.Cos(t),0,Mathf.Sin(t)),e=q*new Vector3(Mathf.Cos(u),0,Mathf.Sin(u));Color c=tint*R(.82f,1.15f);g.Tri(a+d*ra,b+d*rb,a+e*ra,c);g.Tri(a+e*ra,b+d*rb,b+e*rb,c);}
    }
    static void Tree(string name,float x,float z,float scale,Color tint)
    {
        var parent=new GameObject(name).transform;parent.SetParent(vegetation,false);parent.position=new Vector3(x,Height(x,z),z);parent.localScale=Vector3.one*scale;
        Geo trunk=new Geo();Branch(trunk,Vector3.zero,new Vector3(.16f,2.6f,0),.25f,.14f,C("#62584C"));
        Branch(trunk,new Vector3(.08f,1.3f,0),new Vector3(-.9f,3,.1f),.16f,.07f,C("#62584C"));
        Branch(trunk,new Vector3(.12f,1.8f,0),new Vector3(.9f,3.1f,.2f),.14f,.065f,C("#62584C"));
        MeshObject(name+"_Trunk",trunk,wood,parent);
        for(int i=0;i<7;i++)
        {
            float angle=i*2.4f,rad=i==0?0:R(.5f,1.12f);
            var crown=MeshObject(name+"_Crown"+i,Boulder(R(.85f,1.1f),Color.Lerp(tint,C("#A7B875"),i==0?.18f:R(0,.10f)),9,5),foliage,parent);
            crown.transform.localPosition=new Vector3(Mathf.Cos(angle)*rad,3.0f+R(-.12f,.45f)+(i==0?.7f:0),Mathf.Sin(angle)*rad);
            crown.transform.localScale=new Vector3(1.15f,.63f,1);
        }
        Geo leafSprigs=new Geo();
        for(int i=0;i<72;i++)
        {
            float a=R(0,6.28f),rad=R(.6f,2f);Vector3 p=new Vector3(Mathf.Cos(a)*rad,3.0f+R(-.05f,.65f),Mathf.Sin(a)*rad);
            Vector3 d=new Vector3(Mathf.Cos(a),R(.15f,.8f),Mathf.Sin(a))*R(.2f,.42f);
            Leaf(leafSprigs,p,p+d,R(.055f,.11f),Color.Lerp(tint,C("#A8BF7C"),R(.12f,.32f)));
        }
        MeshObject(name+"_LeafSprigs",leafSprigs,foliage,parent,false);
    }
    static void Leaf(Geo g,Vector3 a,Vector3 b,float width,Color col)
    {
        Vector3 side=Vector3.Cross((b-a).normalized,Vector3.up).normalized*width;
        Vector3 mid=Vector3.Lerp(a,b,.47f),ridge=mid+Vector3.up*width*.35f;
        g.Tri(a,mid-side,ridge,col);g.Tri(mid-side,b,ridge,col);
        g.Tri(a,ridge,mid+side,col*.89f);g.Tri(mid+side,ridge,b,col*.89f);
    }
    static void Fern(string name,Vector3 p,float scale,Color tint)
    {
        Geo leaves=new Geo();
        for(int f=0;f<9;f++)
        {
            float a=f*2.399f;Vector3 direction=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
            Vector3 tip=direction*R(.55f,.82f)+Vector3.up*R(.3f,.62f);
            for(int j=1;j<5;j++)
            {float t=j/5f;Vector3 b=tip*t+Vector3.up*Mathf.Sin(t*3.14f)*.18f;Vector3 spread=Vector3.Cross(direction,Vector3.up)*(.26f*(1-t)+.1f);
                Leaf(leaves,b,b+spread+direction*.18f,.08f,tint);Leaf(leaves,b,b-spread+direction*.18f,.08f,tint*.96f);}
            Leaf(leaves,tip*.78f,tip+direction*.14f,.08f,tint*1.05f);
        }
        var go=MeshObject(name,leaves,foliage,vegetation,false);go.transform.position=p;go.transform.localScale=Vector3.one*scale;
    }
    static void Scenery()
    {
        Tree("Old Willow Left",-5.2f,3.1f,1.25f,C("#418675"));
        Tree("Golden Ash",4.9f,5.8f,1.1f,C("#819B63"));
        Tree("Distant Willow",-2.9f,8.9f,1.0f,C("#589986"));
        Tree("Right Woodland",8.3f,3.2f,1.65f,C("#376F62"));
        Tree("Foreground Left",-8.3f,-5.8f,1.1f,C("#0C303B"));
        Tree("Foreground Right",8.3f,-5.8f,1.1f,C("#113A40"));
        for(int i=0;i<22;i++)
        {
            float angle=R(0,6.28f),x=Mathf.Cos(angle)*R(5.3f,9),z=Mathf.Sin(angle)*R(4.8f,7)+.8f;
            if(Path(x,z)<1.9f){x+=x>=0?2.5f:-2.5f;}
            var bush=MeshObject("Fern bank "+i,Boulder(1,Palette(x,z)*.9f,8,4),foliage,vegetation);
            bush.transform.position=new Vector3(x,Height(x,z)+.22f,z);bush.transform.localScale=new Vector3(R(.65f,1.5f),R(.25f,.52f),R(.65f,1.3f));
        }
        Rock("Moss boulder west",new Vector3(-3.9f,.37f,2.3f),new Vector3(1.0f,.72f,.68f),C("#92A09A"),vegetation);
        Rock("Moss boulder east",new Vector3(4.2f,.37f,.8f),new Vector3(.94f,.64f,.76f),C("#869490"),vegetation);
        Rock("Small west stone",new Vector3(-3.3f,.15f,2),new Vector3(.38f,.26f,.34f),C("#A3AEA0"),vegetation);
        // Weathered milestone gives the empty clearing an identifiable focal point.
        Rock("Ancient milestone",new Vector3(2.8f,.86f,2.0f),new Vector3(.56f,1.13f,.4f),C("#879F9B"),vegetation);
        Rock("Milestone footing",new Vector3(2.8f,.10f,2),new Vector3(.8f,.18f,.64f),C("#718D82"),vegetation);
        for(int i=0;i<22;i++)
        {float angle=R(0,6.28f);float x=Mathf.Cos(angle)*R(3.5f,4.4f);float z=.7f+Mathf.Sin(angle)*R(2.6f,3.3f);Rock("Border pebble "+i,new Vector3(x,Height(x,z)+.04f,z),new Vector3(R(.09f,.23f),R(.04f,.11f),R(.09f,.23f)),C("#A2AE91"),vegetation);}
        Flowers("Ivory wildflowers",new Vector2(-3.4f,-1.8f),C("#F3E1A2"));
        Flowers("Lavender",new Vector2(3.6f,-1.0f),C("#C4B0CE"));
        Flowers("Sunlit wildflowers",new Vector2(-2.6f,3.5f),C("#FAEAB7"));
        Flowers("Path flowers",new Vector2(2.0f,-4.7f),C("#EBCF90"));
        Fern("Fern near west rock",new Vector3(-4.0f,Height(-4,-.4f),-.4f),1.15f,C("#73A785"));
        Fern("Fern near milestone",new Vector3(3.6f,Height(3.6f,2.5f),2.5f),.9f,C("#96AB73"));
        Fern("Foreground fronds left",new Vector3(-5.1f,Height(-5.1f,-4.7f),-4.7f),2.1f,C("#154449"));
        Fern("Foreground fronds right",new Vector3(5.8f,Height(5.8f,-4.4f),-4.4f),2.1f,C("#153E43"));
    }
    static void Flowers(string name,Vector2 center,Color tint)
    {
        Geo g=new Geo();
        for(int i=0;i<38;i++)
        {float angle=R(0,6.28f),radius=R(.1f,.9f);float x=center.x+Mathf.Cos(angle)*radius,z=center.y+Mathf.Sin(angle)*radius;var p=new Vector3(x,Height(x,z)+R(.2f,.4f),z);float s=R(.035f,.065f);
            for(int j=0;j<5;j++){float a=j*6.28f/5;Vector3 d=new Vector3(Mathf.Cos(a),.05f,Mathf.Sin(a))*s;Vector3 side=Vector3.Cross(d,Vector3.up)*.5f;g.Tri(p,p+d+side,p+d-side,tint,true);}}
        MeshObject(name,g,flower,vegetation,false);
    }
    static GameObject Character(GameObject model,AnimationClip source)
    {
        var clip=UnityEngine.Object.Instantiate(source);clip.name="Mage Idle";
        var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=true;AnimationUtility.SetAnimationClipSettings(clip,settings);
        string clipPath=Root+"/MageIdle.anim";var oldClip=AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        if(oldClip!=null){EditorUtility.CopySerialized(clip,oldClip);UnityEngine.Object.DestroyImmediate(clip);clip=oldClip;}else AssetDatabase.CreateAsset(clip,clipPath);
        string controllerPath=Root+"/MageIdle.controller";
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if(controller==null)controller=AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        var sm=controller.layers[0].stateMachine;
        var state=sm.states.Length>0?sm.states[0].state:sm.AddState("Idle");state.motion=clip;sm.defaultState=state;
        var go=(GameObject)PrefabUtility.InstantiatePrefab(model);go.name="KayKit Mage - CC0 animated hero";
        go.transform.SetPositionAndRotation(new Vector3(-.55f,.025f,.65f),Quaternion.Euler(0,158,0));go.transform.localScale=Vector3.one*.88f;
        foreach(var r in go.GetComponentsInChildren<Renderer>(true))
        {r.sharedMaterial=hero;if(r.name=="Spellbook_open"||r.name=="Spellbook"||r.name=="1H_Wand")r.enabled=false;}
        var animator=go.GetComponent<Animator>();if(animator==null)animator=go.AddComponent<Animator>();
        animator.enabled=true;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.runtimeAnimatorController=controller;
        // Same FBX supplies rig and clip. Sample a real pose for non-playing Editor preview.
        clip.SampleAnimation(go,.35f);
        return go;
    }
    static void CameraAndLight()
    {
        var cameraGo=new GameObject("Showcase Camera");cameraGo.tag="MainCamera";
        var cam=cameraGo.AddComponent<Camera>();cam.orthographic=true;cam.orthographicSize=5.1f;cam.nearClipPlane=.1f;cam.farClipPlane=90;cam.allowHDR=true;cam.allowMSAA=true;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=C("#99B7AF");
        cameraGo.transform.position=new Vector3(0,11.1f,-15.5f);cameraGo.transform.LookAt(new Vector3(0,.55f,1.4f));
        cam.GetUniversalAdditionalCameraData().renderPostProcessing=true;
        var sunGo=new GameObject("Warm late afternoon sun");var sun=sunGo.AddComponent<Light>();sun.type=LightType.Directional;sun.color=C("#FFF0D0");sun.intensity=1.25f;sun.shadows=LightShadows.Soft;sun.shadowStrength=.82f;sun.shadowBias=.035f;sun.shadowNormalBias=.25f;sunGo.transform.rotation=Quaternion.Euler(52,-38,0);RenderSettings.sun=sun;
        RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=C("#718C96");RenderSettings.fog=true;RenderSettings.fogColor=C("#84ADA4");RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogStartDistance=25;RenderSettings.fogEndDistance=55;
        var vol=new GameObject("Meadow color grade").AddComponent<Volume>();vol.isGlobal=true;vol.priority=10;
        string path=Root+"/MeadowGrade.asset";var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if(profile==null){profile=ScriptableObject.CreateInstance<VolumeProfile>();AssetDatabase.CreateAsset(profile,path);}
        ColorAdjustments grade;if(!profile.TryGet(out grade)){grade=profile.Add<ColorAdjustments>(true);AssetDatabase.AddObjectToAsset(grade,profile);}grade.contrast.Override(6);grade.saturation.Override(3);grade.postExposure.Override(0);
        Vignette vignette;if(!profile.TryGet(out vignette)){vignette=profile.Add<Vignette>(true);AssetDatabase.AddObjectToAsset(vignette,profile);}vignette.intensity.Override(.15f);vignette.smoothness.Override(.6f);
        Bloom bloom;if(!profile.TryGet(out bloom)){bloom=profile.Add<Bloom>(true);AssetDatabase.AddObjectToAsset(bloom,profile);}bloom.intensity.Override(.06f);bloom.threshold.Override(1.2f);vol.sharedProfile=profile;EditorUtility.SetDirty(profile);
    }
    static void Grid()
    {
        var root=new GameObject("Optional tactical hex overlay - presentation only");
        var m=Mat("HexOverlay",Shader.Find("Universal Render Pipeline/Unlit"),C("#DCD8B2"),0);m.SetColor("_BaseColor",C("#DCD8B2"));
        for(int z=0;z<3;z++)for(int x=0;x<5;x++)
        {var go=new GameObject("Hex "+x+" "+z);go.transform.SetParent(root.transform,false);var line=go.AddComponent<LineRenderer>();line.sharedMaterial=m;line.widthMultiplier=.014f;line.loop=true;line.positionCount=6;line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;float radius=.67f;Vector3 center=new Vector3((x-2)*1.16f+(z%2)*.58f,.027f,(z-1)*1.0f+.7f);for(int i=0;i<6;i++){float a=(i*60+30)*Mathf.Deg2Rad;line.SetPosition(i,center+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*radius);}}
        root.SetActive(false);
    }
}
