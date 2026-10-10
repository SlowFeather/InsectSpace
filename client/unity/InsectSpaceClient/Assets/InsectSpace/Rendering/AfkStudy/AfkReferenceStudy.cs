using UnityEngine;
using UnityEngine.Rendering;

namespace InsectSpace.Rendering
{
    /// <summary>Lighting and camera controls for an explicitly local reference scene.</summary>
    public sealed class AfkReferenceStudy : MonoBehaviour
    {
        public enum Weather { Day, RainNight }
        [SerializeField] private Camera view;
        [SerializeField] private Light sun;
        [SerializeField] private Animator actor;
        [SerializeField] private ParticleSystem rain;
        [SerializeField] private Material water;
        [SerializeField] private Material[] surfaces;
        [SerializeField] private Weather weather;
        [SerializeField] private float zoom = 1;
        private Color savedFogColor, savedAmbient;
        private bool savedFog;
        private FogMode savedFogMode;
        private float savedStart, savedEnd;
        private AmbientMode savedAmbientMode;
        private float appliedAspect = -1;
        public Weather CurrentWeather => weather;
        public bool Motion { get; private set; } = true;
        public Camera View => view;
        public Animator Actor => actor;

        public void Configure(Camera camera, Light light, Animator character, ParticleSystem particles,
            Material lake, Material[] materials)
        {
            view = camera; sun = light; actor = character; rain = particles;
            water = lake; surfaces = materials;
        }

        private void OnEnable()
        {
            savedFog = RenderSettings.fog; savedFogColor = RenderSettings.fogColor;
            savedFogMode = RenderSettings.fogMode; savedStart = RenderSettings.fogStartDistance;
            savedEnd = RenderSettings.fogEndDistance; savedAmbient = RenderSettings.ambientLight;
            savedAmbientMode = RenderSettings.ambientMode;
            if (view && sun) SetWeather(weather);
        }

        private void OnDisable()
        {
            RenderSettings.fog = savedFog; RenderSettings.fogColor = savedFogColor;
            RenderSettings.fogMode = savedFogMode; RenderSettings.fogStartDistance = savedStart;
            RenderSettings.fogEndDistance = savedEnd; RenderSettings.ambientLight = savedAmbient;
            RenderSettings.ambientMode = savedAmbientMode;
            if (surfaces != null) foreach (var material in surfaces)
                foreach (var renderer in GetComponentsInChildren<Renderer>())
                    if (System.Array.IndexOf(renderer.sharedMaterials, material) >= 0) renderer.SetPropertyBlock(null);
        }

        public void SetWeather(Weather value)
        {
            weather = value;
            bool night = value == Weather.RainNight;
            sun.color = night ? new Color(.37f,.52f,.85f) : new Color(1,.96f,.81f);
            sun.intensity = night ? .85f : 1.12f;
            sun.transform.rotation = Quaternion.Euler(48, night ? -42 : -36, 0);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = night ? new Color(.22f,.33f,.49f) : new Color(.54f,.63f,.58f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = night ? new Color(.13f,.23f,.36f) : new Color(.51f,.63f,.61f);
            RenderSettings.fogStartDistance = 36; RenderSettings.fogEndDistance = 85;
            view.backgroundColor = RenderSettings.fogColor;
            // Property blocks keep both presets from modifying material assets during Play.
            foreach (var renderer in GetComponentsInChildren<Renderer>())
            {
                var block = new MaterialPropertyBlock();
                if (renderer.sharedMaterial == water)
                {
                    block.SetColor("_BaseColor", night ? new Color(.055f,.16f,.33f) : new Color(.22f,.40f,.46f));
                    block.SetColor("_EdgeColor", night ? new Color(.20f,.39f,.57f) : new Color(.67f,.74f,.66f));
                    block.SetFloat("_Rain", night ? 1 : 0);
                }
                else if (renderer.sharedMaterial && renderer.sharedMaterial.HasProperty("_ShadowTint"))
                    block.SetColor("_ShadowTint", night ? new Color(.09f,.18f,.29f) : new Color(.14f,.36f,.37f));
                renderer.SetPropertyBlock(block);
            }
            if (rain)
            {
                if (night && Motion) rain.Play();
                else rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            ApplyFraming();
        }

        public void SetMotion(bool enabled)
        {
            Motion = enabled;
            if (actor) actor.speed = enabled ? 1 : 0;
            if (rain && weather == Weather.RainNight) { if (enabled) rain.Play(); else rain.Pause(); }
        }

        public void SetZoom(float value) { zoom = Mathf.Clamp(value,.75f,1.3f); ApplyFraming(); }

        public void ApplyFraming()
        {
            if (!view) return;
            appliedAspect = view.aspect;
            view.orthographic = true;
            view.orthographicSize = (appliedAspect < 1 ? 12.6f : 9.2f) / zoom;
            var target = new Vector3(0,0,1.2f);
            view.transform.position = target + new Vector3(0,21,-24);
            view.transform.LookAt(target);
        }

        private void LateUpdate()
        {
            if (view && !Mathf.Approximately(appliedAspect,view.aspect)) ApplyFraming();
        }

        private void OnGUI()
        {
            var previous = GUI.matrix;
            float scale = Mathf.Clamp(Screen.height/900f,.65f,1.6f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale,scale,1));
            float width = Screen.width/scale;
            var label = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            GUI.Label(new Rect(14,12,width-28,24),"LOCAL REFERENCE / AFK Journey - Lilith Games",label);
            int selected = GUI.Toolbar(new Rect(14,42,210,28),(int)weather,new[]{"DAY","RAIN / NIGHT"});
            if (selected != (int)weather) SetWeather((Weather)selected);
            bool motion = GUI.Toggle(new Rect(14,80,80,24),Motion,"Motion");
            if (motion != Motion) SetMotion(motion);
            float next = GUI.HorizontalSlider(new Rect(112,87,112,20),zoom,.75f,1.3f);
            if (!Mathf.Approximately(next,zoom)) SetZoom(next);
            GUI.matrix = previous;
        }
    }
}
