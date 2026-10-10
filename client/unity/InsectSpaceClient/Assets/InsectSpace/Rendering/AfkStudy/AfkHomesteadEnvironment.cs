using UnityEngine;
using UnityEngine.Rendering;

namespace InsectSpace.Rendering
{
    [ExecuteAlways]
    public sealed class AfkHomesteadEnvironment : MonoBehaviour
    {
        public Light Sun;
        [Range(0, 1)] public float TimeOfDay = .5f;
        public Gradient LightColor = new Gradient();
        public Gradient AmbientColor = new Gradient();
        public AnimationCurve LightIntensity = AnimationCurve.Constant(0, 1, 1);
        public AnimationCurve SunX = AnimationCurve.Constant(0, 1, 45);
        public AnimationCurve SunY = AnimationCurve.Constant(0, 1, -35);
        public AnimationCurve SunZ = AnimationCurve.Constant(0, 1, 0);
        public Vector3 ShotRotationOffset;
        public float ShotDayYawOffset;
        [Range(0, 1)] public float ShotAmbientStrength = 1;
        public Vector3 TerrainFresnel = new Vector3(109, 10.15f, .75f);
        public bool SourceAmbientValues = true;
        public bool SourceWaterValues = true;
        public Color WaterCenterColor;
        public Color WaterBedColor;
        public Color WaterCoastColor;
        public Color WaterIntersectColor;
        public Gradient WaterReflectLight = new Gradient();
        public Gradient WaterReflectDark = new Gradient();
        public Gradient WaterReflectCloud = new Gradient();
        public Gradient SunReflection = new Gradient();
        public AnimationCurve WaterSkyWeight = AnimationCurve.Constant(0, 1, .3f);
        public Vector3 WaterGradient = new Vector3(-100, 3.28f, 30);
        public bool Cycle;
        [Min(10)] public float CycleSeconds = 240;
        public Texture2D CloudTexture;
        public float CloudScale = .015f;
        public Vector2 CloudSpeed = new Vector2(.003f, .003f);
        public Vector2 CloudOffset;
        [Range(0, 1)] public float CloudStrength = .25f;
        [Range(0, .99f)] public float CloudFalloff = .8f;
        public Gradient FogColor = new Gradient();
        public Gradient FogScatterColor = new Gradient();
        public AnimationCurve FogDensity = AnimationCurve.Constant(0, 1, .06f);
        public AnimationCurve FogScattering = AnimationCurve.Constant(0, 1, 0);
        public AnimationCurve VolumeLight = AnimationCurve.Constant(0, 1, 0);
        public float FogStart = 35;
        public float FogHeight = 5;
        public float FogHeightFalloff = .1f;
        float appliedTime = -1;

        void OnEnable() => Apply();
        void OnValidate() => Apply();
        void Update()
        {
            if (Application.isPlaying && Cycle) Advance(Time.deltaTime);
            if (!Mathf.Approximately(appliedTime, TimeOfDay)) Apply();
        }

        public void Advance(float seconds)
        {
            TimeOfDay = Mathf.Repeat(TimeOfDay + Mathf.Max(0, seconds) / Mathf.Max(10, CycleSeconds), 1);
            Apply();
        }

        void OnDisable()
        {
            Shader.SetGlobalVector("_StudyCloudSettings", Vector4.zero);
        }

        public void Apply()
        {
            if (!Sun) return;
            appliedTime = TimeOfDay;
            Sun.color = LightColor.Evaluate(TimeOfDay);
            Sun.intensity = LightIntensity.Evaluate(TimeOfDay);
            float day = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.18f, .32f, Mathf.Min(TimeOfDay, 1 - TimeOfDay)));
            var rotation = new Vector3(SunX.Evaluate(TimeOfDay), SunY.Evaluate(TimeOfDay), SunZ.Evaluate(TimeOfDay)) + ShotRotationOffset;
            rotation.y += ShotDayYawOffset * day;
            Sun.transform.rotation = Quaternion.Euler(rotation);
            RenderSettings.sun = Sun;
            RenderSettings.ambientMode = AmbientMode.Flat;
            var ambient = AmbientColor.Evaluate(TimeOfDay);
            RenderSettings.ambientLight = ambient;
            var shaderAmbient = SourceAmbientValues ? ambient : ambient.linear;
            shaderAmbient.a *= ShotAmbientStrength;
            Shader.SetGlobalColor("_StudyAmbient", shaderAmbient);
            Shader.SetGlobalFloat("_StudyTimeOfDay", TimeOfDay);
            Shader.SetGlobalVector("_StudyTerrainFresnel", TerrainFresnel);
            Shader.SetGlobalColor("_StudyZoneLightColor", LightColor.Evaluate(TimeOfDay));
            Shader.SetGlobalFloat("_StudyZoneLightIntensity", Sun.intensity);
            Shader.SetGlobalColor("_StudyWaterZoneLight", WaterValue(LightColor.Evaluate(TimeOfDay)) * Sun.intensity);
            Shader.SetGlobalColor("_StudyWaterAmbient", shaderAmbient);
            Shader.SetGlobalColor("_StudyWaterCenter", WaterValue(WaterCenterColor));
            Shader.SetGlobalColor("_StudyWaterBed", WaterValue(WaterBedColor));
            Shader.SetGlobalColor("_StudyWaterCoast", WaterValue(WaterCoastColor));
            Shader.SetGlobalColor("_StudyWaterIntersect", WaterValue(WaterIntersectColor));
            Shader.SetGlobalColor("_StudyWaterLight", WaterValue(WaterReflectLight.Evaluate(TimeOfDay)));
            Shader.SetGlobalColor("_StudyWaterDark", WaterValue(WaterReflectDark.Evaluate(TimeOfDay)));
            Shader.SetGlobalColor("_StudyWaterCloud", WaterValue(WaterReflectCloud.Evaluate(TimeOfDay)));
            Shader.SetGlobalColor("_StudySunReflection", WaterValue(SunReflection.Evaluate(TimeOfDay)));
            Shader.SetGlobalFloat("_StudyWaterSkyWeight", WaterSkyWeight.Evaluate(TimeOfDay));
            Shader.SetGlobalVector("_StudyWaterGradient", WaterGradient);
            Shader.SetGlobalTexture("_StudyCloudTexture", CloudTexture ? CloudTexture : Texture2D.blackTexture);
            Shader.SetGlobalVector("_StudyCloudSettings", new Vector4(CloudScale, CloudSpeed.x, CloudSpeed.y, CloudStrength * day));
            Shader.SetGlobalVector("_StudyCloudOffset", CloudOffset);
            Shader.SetGlobalFloat("_StudyCloudFalloff", CloudFalloff);
            Shader.SetGlobalColor("_StudyFogColor", FogColor.Evaluate(TimeOfDay));
            Shader.SetGlobalColor("_StudyFogScatter", FogScatterColor.Evaluate(TimeOfDay));
            Shader.SetGlobalVector("_StudyFogSettings", new Vector4(FogDensity.Evaluate(TimeOfDay), FogStart, FogHeight, FogHeightFalloff));
            Shader.SetGlobalVector("_StudyFogLighting", new Vector4(FogScattering.Evaluate(TimeOfDay), VolumeLight.Evaluate(TimeOfDay), day, 0));
            RenderSettings.fog = false;
        }

        Color WaterValue(Color value) => SourceWaterValues ? value : value.linear;
    }
}
