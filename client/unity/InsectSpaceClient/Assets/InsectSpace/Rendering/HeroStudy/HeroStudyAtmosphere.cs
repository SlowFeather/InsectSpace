using UnityEngine;

namespace InsectSpace.Rendering
{
    /// <summary>Scene-local palette treatment following the sky transition. Never edits shared materials.</summary>
    public sealed class HeroStudyAtmosphere : MonoBehaviour
    {
        [SerializeField] private StylizedSkyController sky;
        [SerializeField] private Renderer[] surfaces = System.Array.Empty<Renderer>();
        private MaterialPropertyBlock[] original, working;
        private float applied=-1;
        private static readonly int Mist=Shader.PropertyToID("_MistIntensity");
        public void Configure(StylizedSkyController controller,Renderer[] renderers)
        {Restore();sky=controller;surfaces=renderers;Capture();}
        private void OnEnable() => Capture();
        private void Capture()
        {
            if(original!=null)return;
            original=new MaterialPropertyBlock[surfaces.Length];working=new MaterialPropertyBlock[surfaces.Length];
            for(int i=0;i<surfaces.Length;i++)
            {
                original[i]=new MaterialPropertyBlock();working[i]=new MaterialPropertyBlock();
                if(!surfaces[i])continue;
                surfaces[i].GetPropertyBlock(original[i]);surfaces[i].GetPropertyBlock(working[i]);
            }
            applied=-1;
        }
        private void LateUpdate()
        {
            if(!sky || !sky.RuntimeMaterial)return;
            float strength=sky.AtmosphereStrength;
            if(Mathf.Approximately(strength,applied))return;
            Capture();applied=strength;
            for(int i=0;i<surfaces.Length;i++)
            {if(!surfaces[i])continue;working[i].SetFloat(Mist,strength);surfaces[i].SetPropertyBlock(working[i]);}
        }
        private void OnDisable() => Restore();
        private void Restore()
        {
            if(original==null)return;
            for(int i=0;i<surfaces.Length;i++)if(surfaces[i])surfaces[i].SetPropertyBlock(original[i]);
            original=null;working=null;applied=-1;
        }
    }
}
