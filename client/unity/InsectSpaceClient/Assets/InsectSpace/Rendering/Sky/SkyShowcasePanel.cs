using UnityEngine;

namespace InsectSpace.Rendering
{
    /// <summary>Local art-review controls. IMGUI accepts desktop clicks and mobile touches.</summary>
    public sealed class SkyShowcasePanel : MonoBehaviour
    {
        [SerializeField] private StylizedSkyController sky;
        [SerializeField] private Camera showcaseCamera;
        private bool skyOnly;
        private int originalMask;
        private GUIStyle title, small, button, eyebrow;
        private readonly string[] names = { "DAYLIGHT", "GOLDEN HOUR", "MOONRISE" };
        private readonly string[] descriptions = { "A quiet sky above the meadow", "The last light of a long journey", "A thousand small lights, one vast sky" };
        private void Awake() { if (showcaseCamera) originalMask=showcaseCamera.cullingMask; }
        private void OnGUI()
        {
            if (!sky || !showcaseCamera) return;
            if (title==null)
            {
                title=new GUIStyle(GUI.skin.label) { fontSize=48,fontStyle=FontStyle.Bold }; title.normal.textColor=new Color(.97f,.98f,.92f);
                eyebrow=new GUIStyle(GUI.skin.label) { fontSize=17 }; eyebrow.normal.textColor=new Color(.84f,.93f,.90f);
                small=new GUIStyle(GUI.skin.label) { fontSize=18 }; small.normal.textColor=new Color(.91f,.94f,.85f);
                button=new GUIStyle(GUI.skin.label) { alignment=TextAnchor.MiddleCenter,fontSize=19,fontStyle=FontStyle.Bold }; button.normal.textColor=new Color(.96f,.97f,.88f);
            }
            Rect safe=Screen.safeArea; float scale=Mathf.Min(safe.width/900f,safe.height/1500f);
            float w=safe.width/scale,h=safe.height/scale;
            var old=GUI.matrix; GUI.matrix=Matrix4x4.TRS(new Vector3(safe.x,Screen.height-safe.yMax,0),Quaternion.identity,Vector3.one*scale);
            GUI.Label(new Rect(46,38,w-92,28),"I N S E C T S P A C E   /   R E N D E R   S T U D Y",eyebrow);
            GUI.Label(new Rect(43,72,w-90,66),"Skies of the Meadow",title);
            GUI.Label(new Rect(46,141,w-92,30),descriptions[(int)sky.CurrentPreset],small);
            Line(new Rect(46,188,80,2),new Color(.97f,.92f,.70f,.8f));
            float panelY=h-210;
            Fill(new Rect(30,panelY,w-60,182),new Color(.035f,.105f,.12f,.86f));
            float bw=(w-92)/3;
            for(int i=0;i<3;i++)
            {
                var r=new Rect(46+i*bw,panelY+14,bw-8,63);
                bool selected=(int)sky.CurrentPreset==i;
                Fill(r,selected?new Color(.29f,.43f,.40f,.95f):new Color(.12f,.23f,.24f,.72f));
                if(selected) Line(new Rect(r.x+16,r.yMax-3,r.width-32,2),new Color(.94f,.79f,.42f));
                if(GUI.Button(r,names[i],button)) sky.SetPreset((StylizedSkyController.Preset)i,1.6f);
            }
            if(GUI.Button(new Rect(46,panelY+86,(w-110)*.40f,44),sky.AnimateClouds?"CLOUDS  /  MOVING":"CLOUDS  /  PAUSED",button)) sky.AnimateClouds=!sky.AnimateClouds;
            if(GUI.Button(new Rect(w*.46f,panelY+86,w*.45f,44),skyOnly?"VIEW  /  SKY ONLY":"VIEW  /  WITH SCENE",button)) { skyOnly=!skyOnly;showcaseCamera.cullingMask=skyOnly?0:originalMask; }
            string[] q={"LOW","BALANCED","HIGH"};
            if(GUI.Button(new Rect(46,panelY+135,w-92,32),"QUALITY  /  "+q[Mathf.Clamp(sky.AppliedQuality,0,2)]+"     ·     TAP TO CHANGE",button)) QualitySettings.SetQualityLevel((sky.AppliedQuality+1)%3,true);
            GUI.matrix=old;
        }
        private static void Fill(Rect rect,Color color) { var before=GUI.color; GUI.color=color;GUI.DrawTexture(rect,Texture2D.whiteTexture);GUI.color=before; }
        private static void Line(Rect rect,Color color) => Fill(rect,color);
    }
}
