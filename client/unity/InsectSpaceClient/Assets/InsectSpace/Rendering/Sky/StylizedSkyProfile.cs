using UnityEngine;

namespace InsectSpace.Rendering
{
    [CreateAssetMenu(menuName = "InsectSpace/Rendering/Stylized Sky Profile")]
    public sealed class StylizedSkyProfile : ScriptableObject
    {
        public Color zenith = new Color(.045f,.33f,.65f);
        public Color middle = new Color(.18f,.55f,.77f);
        public Color horizon = new Color(.64f,.83f,.86f);
        public Color cloudLight = new Color(.95f,.98f,.91f);
        public Color cloudShade = new Color(.48f,.73f,.80f);
        public Color celestialColor = new Color(1,.89f,.62f);
        public Color sunlight = new Color(1,.95f,.82f);
        public Color ambient = new Color(.48f,.61f,.64f);
        [Range(0,3)] public float exposure = 1;
        [Range(0,1.5f)] public float coverage = 1;
        [Range(0,1)] public float night;
        [Range(0,3)] public float lightIntensity = 1.15f;
        [Range(.005f,.1f)] public float celestialSize = .022f;
        public Vector3 celestialDirection = new Vector3(.55f,.38f,.74f);
        [Tooltip("Turns per second; visual wind only.")] public float cloudSpeed = .00065f;
        [Header("Optional atmosphere override")]
        public bool overrideFog;
        public bool fogEnabled = true;
        public FogMode fogMode = FogMode.Linear;
        [Min(0)] public float fogStart = 8;
        [Min(.01f)] public float fogEnd = 34;
        [Min(0)] public float fogDensity = .035f;
        [Range(0,1)] public float atmosphereStrength;
    }
}
