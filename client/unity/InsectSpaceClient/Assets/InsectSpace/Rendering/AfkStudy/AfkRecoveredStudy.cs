using UnityEngine;

namespace InsectSpace.Rendering
{
    /// <summary>Local visual study only. Original assets, reconstructed framing.</summary>
    public sealed class AfkRecoveredStudy : MonoBehaviour
    {
        public Camera View;
        public Animator Actor;
        public AfkHomesteadEnvironment Environment;
        public Light ActorLight;
        public GameObject FairyVisual;
        public bool FairyVisible = true;
        [Min(0)] public float FairyLightIntensity = 8;
        public Vector3 Focus = new Vector3(0, 0, 1);
        public float Zoom = 1;
        public bool ShowControls = true;
        float aspect = -1;
        SkinnedMeshRenderer[] characterRenderers;
        MaterialPropertyBlock faceProperties;

        void OnEnable() { ApplyFraming(); if (Environment) Environment.Apply(); ApplyActorLight(); ApplyCharacterFacing(); }
        public void ApplyCharacterFacing()
        {
            if (!Actor) return;
            if (characterRenderers == null) characterRenderers = Actor.GetComponentsInChildren<SkinnedMeshRenderer>();
            if (faceProperties == null) faceProperties = new MaterialPropertyBlock();
            foreach (var renderer in characterRenderers) {
                renderer.GetPropertyBlock(faceProperties);
                faceProperties.SetVector("_StudyFaceForward", Actor.transform.forward);
                renderer.SetPropertyBlock(faceProperties);
            }
        }
        public void SetNight(bool night)
        {
            Environment.Cycle = false;
            Environment.TimeOfDay = night ? .05f : .5f;
            Environment.Apply();
            ApplyActorLight();
            ApplyCharacterFacing();
        }
        public void ApplyActorLight()
        {
            if (FairyVisual && FairyVisual.activeSelf != FairyVisible) FairyVisual.SetActive(FairyVisible);
            if (ActorLight && Environment) {
                float night=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.18f,.32f,Mathf.Min(Environment.TimeOfDay,1-Environment.TimeOfDay)));
                ActorLight.enabled=FairyVisible && night>.001f;
                ActorLight.intensity=FairyVisible ? FairyLightIntensity*night : 0;
            }
        }
        public void SetFairyVisible(bool visible)
        {
            FairyVisible=visible;
            ApplyActorLight();
        }
        public void ApplyFraming()
        {
            if (!View) return;
            aspect = View.aspect;
            View.orthographic = true;
            View.orthographicSize = 12.6f / Mathf.Clamp(Zoom, .65f, 1.6f);
            var rotation = Quaternion.Euler(45, 0, 0);
            View.transform.SetPositionAndRotation(Focus - rotation * Vector3.forward * 48, rotation);
        }
        void LateUpdate()
        {
            if (View && !Mathf.Approximately(aspect, View.aspect)) ApplyFraming();
            ApplyActorLight();
            ApplyCharacterFacing();
        }
        void OnGUI()
        {
            if (!ShowControls) return;
            var saved = GUI.matrix;
            float scale = Mathf.Clamp(Screen.height / 1000f, .7f, 1.6f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            GUI.Box(new Rect(12, 12, 290, 181), "LOCAL / AFK Journey rendering study");
            if (GUI.Button(new Rect(22, 40, 78, 26), "Day")) SetNight(false);
            if (GUI.Button(new Rect(106, 40, 78, 26), "Night")) SetNight(true);
            if (GUI.Button(new Rect(190, 40, 100, 26), Actor && Actor.speed == 0 ? "Animate" : "Pause"))
                if (Actor) Actor.speed = Actor.speed == 0 ? 1 : 0;
            GUI.Label(new Rect(22, 77, 55, 24), "Zoom");
            float next = GUI.HorizontalSlider(new Rect(80, 83, 204, 20), Zoom, .65f, 1.6f);
            if (!Mathf.Approximately(next, Zoom)) { Zoom = next; ApplyFraming(); }
            Environment.Cycle=GUI.Toggle(new Rect(22,106,90,24),Environment.Cycle,"Cycle");
            float time=GUI.HorizontalSlider(new Rect(112,114,172,20),Environment.TimeOfDay,0,1);
            if(!Mathf.Approximately(time,Environment.TimeOfDay)) {Environment.TimeOfDay=time;Environment.Apply();}
            GUI.Label(new Rect(22,132,100,20),Mathf.FloorToInt(Environment.TimeOfDay*24).ToString("00")+":"+Mathf.FloorToInt(Environment.TimeOfDay*1440%60).ToString("00"));
            bool fairy=GUI.Toggle(new Rect(22,158,260,24),FairyVisible,"Fairy / light");
            if(fairy!=FairyVisible)SetFairyVisible(fairy);
            GUI.matrix = saved;
        }
    }
}
