using System;
using UnityEngine;

namespace InsectSpace.Rendering
{
    /// <summary>Baked mesh selection for the local meadow study; presentation only.</summary>
    [DisallowMultipleComponent]
    public sealed class MeadowGrassLod : MonoBehaviour
    {
        [Serializable]
        public sealed class Chunk
        {
            public MeshFilter filter;
            public Mesh low;
            public Mesh medium;
            public Mesh high;
        }

        [SerializeField] private Chunk[] chunks = Array.Empty<Chunk>();
        private int appliedQuality = -1;
        public int AppliedQuality => appliedQuality;
        public int ChunkCount => chunks.Length;

        public void Configure(Chunk[] value)
        {
            chunks = value ?? Array.Empty<Chunk>();
            ApplyQuality(QualitySettings.GetQualityLevel());
        }

        private void OnEnable() => ApplyQuality(QualitySettings.GetQualityLevel());

        private void LateUpdate()
        {
            int quality = Mathf.Clamp(QualitySettings.GetQualityLevel(), 0, 2);
            if (quality != appliedQuality) ApplyQuality(quality);
        }

        public void ApplyQuality(int quality)
        {
            appliedQuality = Mathf.Clamp(quality, 0, 2);
            foreach (var chunk in chunks)
            {
                if (chunk?.filter == null) continue;
                chunk.filter.sharedMesh = appliedQuality == 0 ? chunk.low :
                    appliedQuality == 1 ? chunk.medium : chunk.high;
            }
        }
    }
}
