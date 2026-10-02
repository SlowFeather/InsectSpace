using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

namespace InsectSpace.Rendering
{
    /// <summary>Active-scene sky ownership; presentation only, no simulation dependencies.</summary>
    [DisallowMultipleComponent]
    public sealed class StylizedSkyController : MonoBehaviour
    {
        public enum Preset { Day, Sunset, Night }
        [SerializeField] private Material skyTemplate;
        [SerializeField] private StylizedSkyProfile[] profiles = new StylizedSkyProfile[3];
        [SerializeField] private Camera targetCamera;
        [SerializeField] private Light directionalLight;
        [SerializeField] private Preset initialPreset;
        [SerializeField, Range(0,360)] private float rotationDegrees;
        [SerializeField] private bool animateClouds = true;
        [SerializeField] private bool followQuality = true;
        private static StylizedSkyController owner;
        private static readonly Dictionary<Scene, EnvironmentState> pendingRestores = new Dictionary<Scene, EnvironmentState>();
        private static bool restoreHooks;
        private Material instance, previousSky;
        private CameraClearFlags previousClear;
        private Color previousAmbient, previousFog, previousLightColor;
        private AmbientMode previousAmbientMode;
        private float previousLightIntensity;
        private State current, from, destination;
        private float elapsed, duration, cloudPhase, highCloudPhase;
        private int quality = -1;
        private bool owns;
        public Preset CurrentPreset { get; private set; }
        public Material RuntimeMaterial => instance;
        public bool IsTransitioning => duration > 0 && elapsed < duration;
        public bool AnimateClouds { get => animateClouds; set => animateClouds = value; }
        public int AppliedQuality => quality;
        public float CloudPhase => cloudPhase;

        // RenderSettings belongs to the active scene. Restore inactive scenes when they
        // next become active, before a new controller captures their baseline.
        private struct EnvironmentState
        {
            public Material sky;
            public Color ambient, fog;
            public AmbientMode mode;
            public void Restore() { RenderSettings.skybox=sky; RenderSettings.ambientMode=mode; RenderSettings.ambientLight=ambient; RenderSettings.fogColor=fog; }
        }
        private static void RestorePending(Scene before, Scene after)
        {
            if (pendingRestores.TryGetValue(after,out var state)) { state.Restore(); pendingRestores.Remove(after); }
        }
        private static void ForgetUnloaded(Scene scene) { pendingRestores.Remove(scene); }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            SceneManager.activeSceneChanged-=RestorePending; SceneManager.sceneUnloaded-=ForgetUnloaded;
            pendingRestores.Clear(); owner=null; restoreHooks=false;
        }

        private struct State
        {
            public Color zenith, middle, horizon, cloudLight, cloudShade, celestial, sunlight, ambient;
            public float exposure, coverage, night, intensity, size, speed;
            public Vector3 direction;
            public static State Read(StylizedSkyProfile p) => new State { zenith=p.zenith,middle=p.middle,horizon=p.horizon,cloudLight=p.cloudLight,cloudShade=p.cloudShade,celestial=p.celestialColor,sunlight=p.sunlight,ambient=p.ambient,exposure=p.exposure,coverage=p.coverage,night=p.night,intensity=p.lightIntensity,size=p.celestialSize,speed=p.cloudSpeed,direction=p.celestialDirection.normalized };
            public static State Blend(State a, State b, float t) => new State { zenith=Color.Lerp(a.zenith,b.zenith,t),middle=Color.Lerp(a.middle,b.middle,t),horizon=Color.Lerp(a.horizon,b.horizon,t),cloudLight=Color.Lerp(a.cloudLight,b.cloudLight,t),cloudShade=Color.Lerp(a.cloudShade,b.cloudShade,t),celestial=Color.Lerp(a.celestial,b.celestial,t),sunlight=Color.Lerp(a.sunlight,b.sunlight,t),ambient=Color.Lerp(a.ambient,b.ambient,t),exposure=Mathf.Lerp(a.exposure,b.exposure,t),coverage=Mathf.Lerp(a.coverage,b.coverage,t),night=Mathf.Lerp(a.night,b.night,t),intensity=Mathf.Lerp(a.intensity,b.intensity,t),size=Mathf.Lerp(a.size,b.size,t),speed=Mathf.Lerp(a.speed,b.speed,t),direction=Vector3.Slerp(a.direction,b.direction,t).normalized };
        }

        private void OnEnable()
        {
            if (!restoreHooks) { SceneManager.activeSceneChanged+=RestorePending; SceneManager.sceneUnloaded+=ForgetUnloaded; restoreHooks=true; }
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            if (gameObject.scene == SceneManager.GetActiveScene()) Acquire();
        }
        private void OnActiveSceneChanged(Scene before, Scene after)
        {
            if (gameObject.scene == after) Acquire(); else Release();
        }
        private void Acquire()
        {
            if (owns || !skyTemplate || profiles == null || profiles.Length != 3 || !profiles[0] || !profiles[1] || !profiles[2]) return;
            if (owner && owner != this) owner.Release();
            RestorePending(default,gameObject.scene);
            previousSky=RenderSettings.skybox; previousAmbient=RenderSettings.ambientLight; previousAmbientMode=RenderSettings.ambientMode; previousFog=RenderSettings.fogColor;
            if (targetCamera) { previousClear=targetCamera.clearFlags; targetCamera.clearFlags=CameraClearFlags.Skybox; }
            if (directionalLight) { previousLightColor=directionalLight.color; previousLightIntensity=directionalLight.intensity; }
            instance=new Material(skyTemplate) { name=skyTemplate.name+" (runtime)", hideFlags=HideFlags.DontSave };
            RenderSettings.skybox=instance; owner=this; owns=true; quality=-1;
            SetPreset(initialPreset,0); ApplyQuality(QualitySettings.GetQualityLevel());
        }
        public void SetPreset(Preset preset, float transitionSeconds = 1.5f)
        {
            int index=(int)preset;
            if (index < 0 || profiles == null || index >= profiles.Length || !profiles[index]) throw new System.ArgumentOutOfRangeException(nameof(preset));
            CurrentPreset=preset; destination=State.Read(profiles[index]); from=current; elapsed=0; duration=Mathf.Max(0,transitionSeconds);
            if (duration==0) { current=destination; ApplyState(); }
        }
        public void SetQuality(int level) { followQuality=false; ApplyQuality(level); }
        public void ResumeQualityTracking() { followQuality=true; }
        private void ApplyQuality(int level)
        {
            quality=Mathf.Clamp(level,0,2); if (!instance) return;
            if (quality>0) instance.EnableKeyword("_SKY_DETAILS"); else instance.DisableKeyword("_SKY_DETAILS");
        }
        private void Update()
        {
            if (!owns || !instance) return;
            if (followQuality && quality!=Mathf.Clamp(QualitySettings.GetQualityLevel(),0,2)) ApplyQuality(QualitySettings.GetQualityLevel());
            if (IsTransitioning) { elapsed+=Time.unscaledDeltaTime; current=State.Blend(from,destination,Mathf.SmoothStep(0,1,Mathf.Clamp01(elapsed/duration))); ApplyState(); }
            if (animateClouds)
            {
                cloudPhase=Mathf.Repeat(cloudPhase+Time.deltaTime*current.speed,1);
                highCloudPhase=Mathf.Repeat(highCloudPhase-Time.deltaTime*current.speed*.43f,1);
            }
            instance.SetVector(CloudOffset,new Vector4(cloudPhase,0,highCloudPhase,0));
            instance.SetFloat(Rotation,rotationDegrees*Mathf.Deg2Rad);
        }
        private static readonly int Zenith=Shader.PropertyToID("_Zenith"), Middle=Shader.PropertyToID("_Middle"), Horizon=Shader.PropertyToID("_Horizon"), CloudLight=Shader.PropertyToID("_CloudLight"), CloudShade=Shader.PropertyToID("_CloudShade"), SunColor=Shader.PropertyToID("_SunColor"), SunDirection=Shader.PropertyToID("_SunDirection"), Exposure=Shader.PropertyToID("_Exposure"), Coverage=Shader.PropertyToID("_Coverage"), Night=Shader.PropertyToID("_Night"), SunSize=Shader.PropertyToID("_SunSize"), CloudOffset=Shader.PropertyToID("_CloudOffset"), Rotation=Shader.PropertyToID("_Rotation");
        private void ApplyState()
        {
            if (!instance) return;
            instance.SetColor(Zenith,ShaderColor(current.zenith)); instance.SetColor(Middle,ShaderColor(current.middle)); instance.SetColor(Horizon,ShaderColor(current.horizon)); instance.SetColor(CloudLight,ShaderColor(current.cloudLight)); instance.SetColor(CloudShade,ShaderColor(current.cloudShade)); instance.SetColor(SunColor,ShaderColor(current.celestial));
            instance.SetVector(SunDirection,current.direction); instance.SetFloat(Exposure,current.exposure); instance.SetFloat(Coverage,current.coverage); instance.SetFloat(Night,current.night); instance.SetFloat(SunSize,current.size);
            RenderSettings.ambientMode=AmbientMode.Flat; RenderSettings.ambientLight=current.ambient; RenderSettings.fogColor=current.horizon;
            if (directionalLight) { directionalLight.color=current.sunlight; directionalLight.intensity=current.intensity; }
        }
        private void OnDisable() { SceneManager.activeSceneChanged-=OnActiveSceneChanged; Release(); }
        // Material color properties already convert to the active render color space.
        private static Color ShaderColor(Color color) => color;
        private void Release()
        {
            if (!owns) return; owns=false;
            var baseline=new EnvironmentState {sky=previousSky,ambient=previousAmbient,mode=previousAmbientMode,fog=previousFog};
            if (gameObject.scene==SceneManager.GetActiveScene()) { if (RenderSettings.skybox==instance) baseline.Restore(); }
            else if (gameObject.scene.IsValid() && gameObject.scene.isLoaded) pendingRestores[gameObject.scene]=baseline;
            if (targetCamera) targetCamera.clearFlags=previousClear;
            if (directionalLight) { directionalLight.color=previousLightColor; directionalLight.intensity=previousLightIntensity; }
            if (instance) { if (Application.isPlaying) Destroy(instance); else DestroyImmediate(instance); }
            instance=null; if (owner==this) owner=null;
        }
    }
}

