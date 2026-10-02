using System;
using System.IO;
using HybridCLR.Editor;
using HybridCLR.Editor.Settings;
using InsectSpace.Client;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace InsectSpace.Editor
{
    // Local development does not grant a production release approval or validate native hot update.
    public static class WebDevelopmentBuild
    {
        public static bool IsBuilding { get; private set; }
        public static string OutputRoot => Path.GetFullPath(Path.Combine(SettingsUtil.ProjectDir,
            "../../../.artifacts/unity6/WebDevelopment"));

        public static bool IsIsolatedProject() => string.Equals(Path.GetFullPath(SettingsUtil.ProjectDir),
            Path.GetFullPath(Path.Combine(SettingsUtil.ProjectDir, "../../../.artifacts/unity6/ValidationClient")),
            StringComparison.OrdinalIgnoreCase);

        public static bool IsDevelopmentBuild(string outputPath, BuildTarget target, BuildOptions options,
            bool scoped, bool isolated, bool scriptsOnly)
        {
            if (!scoped || !isolated || scriptsOnly || target != BuildTarget.WebGL ||
                (options & BuildOptions.Development) == 0 || string.IsNullOrWhiteSpace(outputPath)) return false;
            try { return string.Equals(Path.GetFullPath(outputPath).TrimEnd('/', '\\'), OutputRoot,
                StringComparison.OrdinalIgnoreCase); }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
        }

        public static void Build()
        {
#if UNITY_6000_0_OR_NEWER && !TUANJIE_2022_3_OR_NEWER
            if (!IsIsolatedProject() || Application.unityVersion != "6000.6.3f1" ||
                EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
                throw new BuildFailedException("Use tools/Invoke-Unity.ps1 -Engine Unity -Action Web on the pinned editor.");
            FoundationSetup.Prepare();
            const string configPath = "Assets/InsectSpace/Resources/InsectSpaceBoot.json";
            string originalConfig = File.ReadAllText(configPath);
            bool originalHybrid = HybridCLRSettings.Instance.enable;
            string generated = "Assets/InsectSpace/Generated/WebDevelopment";
            Directory.CreateDirectory(generated);
            try
            {
                var config = BootConfiguration.Load();
                config.webDevelopment = true;
                config.editorSimulate = false;
                config.localSmokeMode = true;
                config.useWeChatSdk = false;
                config.resourceMode = ResourceMode.Offline;
                config.discoverRemoteVersion = false;
                config.remoteRoot = config.fallbackRoot = "";
                config.playerBuildId = "unity-web-development-001";
                config.ValidateWebDevelopment();
                File.WriteAllText(configPath, JsonUtility.ToJson(config, true));
                HybridCLRSettings.Instance.enable = false;
                HybridCLRSettings.Save();
                // Preserve gameplay, GF factories and YooAsset's reflected file-system constructors.
                // Package-local link.xml files do not preserve these types in this Unity Web build.
                File.WriteAllText(Path.Combine(generated, "link.xml"),
                    "<linker>" +
                    "<assembly fullname=\"InsectSpace.Gameplay.HotUpdate\" preserve=\"all\" />" +
                    "<assembly fullname=\"Framework.Core\" preserve=\"all\" />" +
                    "<assembly fullname=\"Framework.Unity\" preserve=\"all\" />" +
                    "<assembly fullname=\"YooAsset\" preserve=\"all\" />" +
                    "</linker>");
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                ContentBuild.BuildPackages();
                HotUpdateBuild.BuildCorePackage();
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.WebGL, ScriptingImplementation.IL2CPP);
                PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.WebGL, Il2CppCodeGeneration.OptimizeSize);
                // AssetBundle-only components (for example CapsuleCollider) are absent from the build scene.
                // Keep engine types for local content development; production stripping needs its own audit.
                PlayerSettings.stripEngineCode = false;
                PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
                PlayerSettings.WebGL.decompressionFallback = false;
                PlayerSettings.WebGL.dataCaching = false;
                IsBuilding = true;
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { FoundationSetup.ScenePath },
                    locationPathName = OutputRoot,
                    target = BuildTarget.WebGL,
                    options = BuildOptions.Development,
                    extraScriptingDefines = new[] { "INSECTSPACE_WEB_DEVELOPMENT" }
                });
                if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                    throw new BuildFailedException("Web development build failed: " + report.summary.result);
                Debug.Log("[InsectSpace] WEB_DEVELOPMENT_BUILT " + OutputRoot);
            }
            finally
            {
                IsBuilding = false;
                File.WriteAllText(configPath, originalConfig);
                HybridCLRSettings.Instance.enable = originalHybrid;
                HybridCLRSettings.Save();
                AssetDatabase.Refresh();
            }
#else
            throw new BuildFailedException("Web development uses Unity 6.6; use Tuanjie for WeChat MiniGame.");
#endif
        }
    }
}
