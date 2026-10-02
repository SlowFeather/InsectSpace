using InsectSpace.Client;
using InsectSpace.Contracts;
using InsectSpace.Rendering;
using UnityEditor;
using UnityEngine;

namespace InsectSpace.Editor
{
    public sealed class FoundationDashboard : EditorWindow
    {
        [MenuItem("InsectSpace/Foundation/Runtime Status")]
        private static void Open() { GetWindow<FoundationDashboard>("InsectSpace"); }

        private void OnInspectorUpdate() { Repaint(); }
        private void OnGUI()
        {
            EditorGUILayout.LabelField("Foundation Status", EditorStyles.boldLabel);
            var boot = FindObjectOfType<InsectSpaceBootstrap>();
            EditorGUILayout.LabelField("Environment", Application.isPlaying ? "Editor Play Mode / LOCAL SMOKE" : "Editor");
            EditorGUILayout.LabelField("Stage", boot == null ? "Stopped" : boot.Stage);
            EditorGUILayout.LabelField("Session", boot == null ? "-" : boot.Status);
            EditorGUILayout.LabelField("Modules", boot == null ? "0" : boot.ModuleCount.ToString());
            EditorGUILayout.LabelField("Luban Tables", boot == null ? "0" : boot.TableCount.ToString());
            if (boot != null && boot.LastError != null) EditorGUILayout.HelpBox(boot.LastError, MessageType.Error);
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                var tier = (QualityTier)EditorGUILayout.EnumPopup("Quality", QualityController.Current);
                if (tier != QualityController.Current) QualityController.Apply(tier, tier == QualityTier.High ? 60 : 30);
            }
        }
    }
}
