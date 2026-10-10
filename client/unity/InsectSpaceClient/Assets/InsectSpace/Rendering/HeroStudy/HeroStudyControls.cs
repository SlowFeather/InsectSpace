using UnityEngine;

namespace InsectSpace.Rendering
{
    /// <summary>Scene-local art review controls; no simulation or online state.</summary>
    public sealed class HeroStudyControls : MonoBehaviour
    {
        [SerializeField] private StylizedSkyController sky;
        [SerializeField] private Animator character;
        [SerializeField] private HeroStudyFraming framing;
        [SerializeField] private string attribution="Valerya: agra_aoe / CC BY 4.0 / Mixamo";
        private bool expanded;
        private GUIStyle label, button;
        public void Configure(StylizedSkyController value, Animator actor, string credit=null)
        { sky=value;character=actor;framing=FindAnyObjectByType<HeroStudyFraming>();if(credit!=null)attribution=credit; }
        private void OnGUI()
        {
            if(!sky || !character)return;
            if(label==null)
            {
                label=new GUIStyle(GUI.skin.label){fontSize=13};
                label.normal.textColor=new Color(.94f,.95f,.86f);
                button=new GUIStyle(GUI.skin.button){fontSize=14};
            }
            var previous=GUI.matrix;var safe=Screen.safeArea;float scale=Mathf.Clamp(Mathf.Min(safe.width/720f,safe.height/900f),.5f,1.8f);
            GUI.matrix=Matrix4x4.TRS(new Vector3(safe.x,Screen.height-safe.yMax,0),Quaternion.identity,Vector3.one*scale);
            float height=safe.height/scale,width=safe.width/scale;
            GUI.Label(new Rect(18,height-58,width-145,48),"LOCAL ART STUDY\n"+attribution,label);
            if(GUI.Button(new Rect(width-106,height-37,88,28),expanded?"CLOSE":"CONTROLS",button))expanded=!expanded;
            if(expanded)
            {
                GUI.Box(new Rect(width-346,height-208,328,161),GUIContent.none);
                if(framing)
                {
                    if(GUI.Button(new Rect(width-336,height-196,151,30),"HERO",button))framing.SetMode(HeroStudyFraming.ViewMode.Hero);
                    if(GUI.Button(new Rect(width-178,height-196,150,30),"EXPLORE",button))framing.SetMode(HeroStudyFraming.ViewMode.Explore);
                }
                string[] names={"DAY","SUNSET","NIGHT","MIST"};
                for(int i=0;i<4;i++) if(GUI.Button(new Rect(width-336+i*78,height-157,73,30),names[i],button))sky.SetPreset((StylizedSkyController.Preset)i,1.2f);
                if(GUI.Button(new Rect(width-336,height-119,151,28),character.speed>0?"PAUSE MOTION":"RESUME MOTION",button))
                {character.speed=character.speed>0?0:1;sky.AnimateClouds=character.speed>0;}
                if(GUI.Button(new Rect(width-178,height-119,150,28),"QUALITY  "+Mathf.Clamp(sky.AppliedQuality,0,2),button))QualitySettings.SetQualityLevel((sky.AppliedQuality+1)%3,true);
            }
            GUI.matrix=previous;
        }
    }
}
