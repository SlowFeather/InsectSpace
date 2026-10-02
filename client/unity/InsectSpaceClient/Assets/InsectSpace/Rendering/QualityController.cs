using System;
using InsectSpace.Contracts;
using UnityEngine;

namespace InsectSpace.Rendering
{
    public static class QualityController
    {
        public static QualityTier Current { get; private set; }
        public static event Action<QualityTier> Changed;
        public static void Apply(QualityTier tier, int targetFps)
        {
            int index = (int)tier;
            if (index < 0 || index >= QualitySettings.names.Length || targetFps < 15 || targetFps > 120)
                throw new ArgumentOutOfRangeException(nameof(tier));
            QualitySettings.SetQualityLevel(index, true);
#if UNITY_WEBGL && INSECTSPACE_WEB_DEVELOPMENT && !UNITY_EDITOR
            // Match browser presentation timing instead of using a software timer for the mobile FPS budget.
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = -1;
#else
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = targetFps;
#endif
            Current = tier;
            Changed?.Invoke(tier);
        }
    }
}
