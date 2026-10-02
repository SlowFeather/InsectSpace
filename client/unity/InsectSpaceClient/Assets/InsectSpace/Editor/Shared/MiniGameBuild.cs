using System.IO;
using System;
using System.Linq;
using System.Reflection;
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using InsectSpace.Client;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace InsectSpace.Editor
{
    public static class MiniGameBuild
    {
        internal static bool IsExporting { get; private set; }
        public static string NativeOutputRoot => Path.Combine(SettingsUtil.HybridCLRDataDir, "MiniGameValidation", "Raw");

        public static void SelectWeChatPlatform()
        {
#if TUANJIE_2022_3_OR_NEWER
            if (!NativeValidationBuild.IsIsolatedProject() || !Application.isBatchMode)
                throw new BuildFailedException("Select the SDK platform only in the isolated validation project.");
            PlayerSettings.MiniGame.SetActiveSubplatform("WeChat", true);
            PlayerSettings.MiniGame.useSlimMetaFileFormat = false;
            AssetDatabase.SaveAssets();
            Debug.Log("[InsectSpace] WECHAT_SUBPLATFORM_SELECTED " + PlayerSettings.MiniGame.GetActiveSubplatform());
#else
            throw new BuildFailedException("Use the pinned Tuanjie editor.");
#endif
        }

        public static void ExportNative()
        {
#if TUANJIE_2022_3_OR_NEWER
            PrepareNative(false);
            const BuildTarget target = BuildTarget.MiniGame;
            Directory.CreateDirectory(NativeOutputRoot);
            BuildReport report;
            IsExporting = true;
            try
            {
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { FoundationSetup.ScenePath },
                    target = target,
                    targetGroup = BuildPipeline.GetBuildTargetGroup(target),
                    locationPathName = NativeOutputRoot,
                    options = BuildOptions.Development
                });
            }
            finally { IsExporting = false; }
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("MiniGame native export failed: " + report.summary.result);
            Debug.Log("[InsectSpace] MINIGAME_NATIVE_EXPORTED " + report.summary.outputPath);
            Debug.Log("[InsectSpace] Raw MiniGame export only. WeChat SDK conversion and device execution are separate validations.");
#else
            throw new BuildFailedException("Use the pinned Tuanjie editor for MiniGame export.");
#endif
        }

        internal static void PrepareNative(bool weChat)
        {
#if TUANJIE_2022_3_OR_NEWER
            const BuildTarget target = BuildTarget.MiniGame;
            if (!NativeValidationBuild.IsIsolatedProject() || !Application.isBatchMode ||
                Application.unityVersion != "2022.3.62t16" || EditorUserBuildSettings.activeBuildTarget != target)
                throw new BuildFailedException("Run the pinned editor with tools/Invoke-Unity.ps1 -Action MiniGameNative.");
            FoundationSetup.Prepare();
            NativeValidationBuild.EnsureLocalToolchain();
            PlayerSettings.MiniGame.SetActiveSubplatform("WeChat", weChat);
            var group = BuildPipeline.GetBuildTargetGroup(target);
            PlayerSettings.SetScriptingBackend(group, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(group, ManagedStrippingLevel.Minimal);
            PlayerSettings.MiniGame.useSlimMetaFileFormat = false;
            PlayerSettings.MiniGame.exceptionSupport = MiniGameExceptionSupport.FullWithStacktrace;
            EditorUserBuildSettings.development = true;
            EditorUserBuildSettings.allowDebugging = false;
            EditorUserBuildSettings.buildScriptsOnly = false;
            InspectNativeConfiguration();
            var config = BootConfiguration.Load();
            config.editorSimulate = false;
            config.localSmokeMode = false;
            config.useWeChatSdk = weChat;
            config.resourceMode = ResourceMode.Offline;
            File.WriteAllText("Assets/InsectSpace/Resources/InsectSpaceBoot.json", JsonUtility.ToJson(config, true));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            HotUpdateBuild.PrepareAotMetadata();
            HotUpdateBuild.StagePlayerMetadata();
            ContentBuild.BuildPackages();
            HotUpdateBuild.BuildCorePackage();
#else
            throw new BuildFailedException("Use the pinned Tuanjie editor for MiniGame export.");
#endif
        }

        public static bool IsValidationExport(string outputPath, BuildTarget target, BuildOptions options,
            bool scoped, bool isolated, bool scriptsOnly)
        {
#if TUANJIE_2022_3_OR_NEWER
            if (!scoped || !isolated || scriptsOnly || target != BuildTarget.MiniGame ||
                (options & BuildOptions.Development) == 0 || string.IsNullOrWhiteSpace(outputPath)) return false;
            try
            {
                return string.Equals(Path.GetFullPath(outputPath).TrimEnd('\\', '/'),
                    Path.GetFullPath(NativeOutputRoot).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
            catch (PathTooLongException) { return false; }
#endif
            return false;
        }

        [MenuItem("InsectSpace/Build/Check MiniGame Managed Assemblies")]
        public static void Preflight()
        {
#if TUANJIE_2022_3_OR_NEWER
            InspectNativeConfiguration();
            if (PlayerSettings.MiniGame.GetActiveSubplatform() == MiniGameBuildSubtarget.WeChat)
            {
                if (Type.GetType("WeChatWASM.WX, Wx", false) == null ||
                    Type.GetType("WeChatWASM.WXConvertCore, WxEditor", false) == null)
                    throw new BuildFailedException("WeChat is selected but its runtime/editor SDK assemblies are not loaded.");
                Debug.Log("[InsectSpace] WECHAT_SDK_ASSEMBLIES_LOADED");
            }
            const BuildTarget target = BuildTarget.MiniGame;
            if (!BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(target), target))
                throw new BuildFailedException("Install the matching Tuanjie MiniGame support module.");
            FoundationSetup.Prepare();
            CompileDllCommand.CompileDll(target, true);
            string path = Path.Combine(SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target), HotUpdateLoader.AssemblyName + ".dll");
            if (!File.Exists(path)) throw new BuildFailedException("MiniGame gameplay assembly was not produced.");
            Debug.Log("[InsectSpace] MINIGAME_MANAGED_COMPILED " + path);
            Debug.Log("[InsectSpace] MiniGame native export, file-system SDK, transports and device execution still require platform validation.");
#else
            throw new BuildFailedException("Use the pinned Tuanjie editor for MiniGame compilation.");
#endif
        }

        public static void InspectNativeConfiguration()
        {
#if TUANJIE_2022_3_OR_NEWER
            Debug.Log("[InsectSpace] MINIGAME_CONFIGURATION unity=" + Application.unityVersion +
                " activeTarget=" + EditorUserBuildSettings.activeBuildTarget +
                " subtarget=" + EditorUserBuildSettings.miniGameBuildSubtarget +
                " activePlatform=" + PlayerSettings.MiniGame.GetActiveSubplatform() +
                " slimMetadata=" + PlayerSettings.MiniGame.useSlimMetaFileFormat);
            Debug.Log("[InsectSpace] MINIGAME_SUBTARGETS " + string.Join(",", Enum.GetNames(typeof(MiniGameBuildSubtarget))));
            Debug.Log("[InsectSpace] MINIGAME_RUNTIME " + (int)RuntimePlatform.MiniGamePlayer + "/" + RuntimePlatform.MiniGamePlayer);
            foreach (string feature in EditorBuildSettings.GetSlimFeaturesWeixinMiniGame())
                Debug.Log("[InsectSpace] MINIGAME_SLIM " + feature + "=" + EditorBuildSettings.GetSlimTypeWeixinMiniGame(feature));
            foreach (var method in typeof(EditorBuildSettings).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.Name.Contains("Slim")))
                Debug.Log("[InsectSpace] MINIGAME_SLIM_API " + method);
            foreach (var method in typeof(UnityEditor.Build.Profile.BuildProfile).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.Name.Contains("Create") || method.Name.Contains("Active")))
                Debug.Log("[InsectSpace] MINIGAME_PROFILE_API " + method);
#endif
        }
    }
}
