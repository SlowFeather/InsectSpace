using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using HybridCLR.Editor.Settings;
using InsectSpace.Client;
using UnityEditor;
using UnityEditor.Build;
using BuildReport = UnityEditor.Build.Reporting.BuildReport;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace InsectSpace.Editor
{
    public static class HotUpdateBuild
    {
        private const string CodeDirectory = "Assets/InsectSpace/Content/Code";
        internal static bool IsPreparingAot { get; private set; }

        [MenuItem("InsectSpace/Build/Prepare Native AOT Metadata")]
        public static void PrepareAotMetadata()
        {
            FoundationSetup.Prepare();
            if (HybridCLRSettings.Instance.useGlobalIl2cpp)
                throw new BuildFailedException("Use a project-local HybridCLR toolchain; global editor replacement is not allowed by this workflow.");
            var target = EditorUserBuildSettings.activeBuildTarget;
            var group = BuildPipeline.GetBuildTargetGroup(target);
            if (PlayerSettings.GetScriptingBackend(group) != ScriptingImplementation.IL2CPP)
                throw new BuildFailedException("Select IL2CPP for the intended target before preparing native AOT metadata.");
            // The SDK verifies its local installation before generating stripped DLLs and bridges.
            // Ordinary Player builds still pass through all release checks below.
            if (IsPreparingAot) throw new BuildFailedException("AOT metadata preparation is already running.");
            IsPreparingAot = true;
            try { PrebuildCommand.GenerateAll(); }
            finally { IsPreparingAot = false; }
            Debug.Log("[InsectSpace] AOT_METADATA_GENERATED target=" + target);
        }

        [MenuItem("InsectSpace/Build/Compile Hot Update DLL")]
        public static void CompileHotUpdate()
        {
            FoundationSetup.Prepare();
            CompileDllCommand.CompileDll(EditorUserBuildSettings.activeBuildTarget);
            string source = Path.Combine(SettingsUtil.GetHotUpdateDllsOutputDirByTarget(EditorUserBuildSettings.activeBuildTarget),
                HotUpdateLoader.AssemblyName + ".dll");
            if (!File.Exists(source)) throw new FileNotFoundException("HybridCLR compilation produced no gameplay DLL.", source);
            Directory.CreateDirectory(CodeDirectory);
            string stagedManifest = Path.Combine(CodeDirectory, "code_manifest.json");
            if (File.Exists(stagedManifest)) File.Delete(stagedManifest);
            File.Copy(source, Path.Combine(CodeDirectory, HotUpdateLoader.AssemblyName + ".dll.bytes"), true);
            AssetDatabase.Refresh();
            Debug.Log("[InsectSpace] HOT_UPDATE_COMPILED " + source);
        }

        [MenuItem("InsectSpace/Build/Stage Matching Player Metadata")]
        public static void StagePlayerMetadata() => StagePlayerMetadata(true);

        internal static void StagePlayerMetadata(bool compile)
        {
            // A platform engineer must generate native bridges and stripped AOT DLLs
            // with the approved target toolchain before invoking this operation.
            var target = EditorUserBuildSettings.activeBuildTarget;
            var names = HybridCLRSettings.Instance.patchAOTAssemblies;
            string directory = SettingsUtil.GetAssembliesPostIl2CppStripDir(target);
            foreach (string name in names)
                if (!File.Exists(Path.Combine(directory, name + ".dll")))
                    throw new FileNotFoundException("Missing matching Player AOT metadata.", Path.Combine(directory, name + ".dll"));
            if (compile) CompileHotUpdate();
            var manifest = new CodeManifest
            {
                target = RuntimeTarget(target),
                playerBuildId = BootConfiguration.Load().playerBuildId,
                contentPackages = BootConfiguration.Load().contentPackages,
                aot = names.Select(name => CopyPayload(name, Path.Combine(directory, name + ".dll"))).ToArray(),
                hotUpdate = new[]
                {
                    CopyPayload(HotUpdateLoader.AssemblyName, Path.Combine(
                        SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target), HotUpdateLoader.AssemblyName + ".dll"))
                }
            };
            File.WriteAllText(Path.Combine(CodeDirectory, "code_manifest.json"), JsonUtility.ToJson(manifest, true));
            AssetDatabase.Refresh();
        }

        [MenuItem("InsectSpace/Build/Build Core Resource Package")]
        public static void BuildCorePackage()
        {
            FoundationSetup.Prepare();
            var config = BootConfiguration.Load();
            var parameters = new RawFileBuildParameters
            {
                BuildOutputRoot = Environment.GetEnvironmentVariable("INSECTSPACE_BUILD_OUTPUT_ROOT")
                    ?? Path.GetFullPath("../../../.artifacts/yoo"),
                BundledFileRoot = Path.Combine(Application.streamingAssetsPath, "yoo"),
                BuildPipeline = nameof(RawFileBuildPipeline),
                BuildBundleType = (int)EBundleType.RawBundle,
                BuildTarget = EditorUserBuildSettings.activeBuildTarget,
                PackageName = config.packageName,
                PackageVersion = config.packageVersion,
                VerifyBuildingResult = true,
                BundledCopyOption = EBundledCopyOption.ClearAndCopyAll
            };
            var result = new RawFileBuildPipeline().Run(parameters, true);
            if (!result.Success) throw new InvalidOperationException(result.ErrorInfo);
            LastPackageOutput = result.OutputPackageDirectory;
            AssetDatabase.Refresh();
            Debug.Log("[InsectSpace] CORE_PACKAGE_BUILT " + result.OutputPackageDirectory);
        }

        public static void ValidateBuildArtifacts()
        {
            CompileHotUpdate();
            ContentBuild.BuildPackages();
            BuildCorePackage();
        }

        public static string LastPackageOutput { get; private set; }

        private static CodePayload CopyPayload(string name, string source)
        {
            var bytes = File.ReadAllBytes(source);
            File.WriteAllBytes(Path.Combine(CodeDirectory, name + ".dll.bytes"), bytes);
            using (var sha = SHA256.Create())
                return new CodePayload
                {
                    assemblyName = name, address = name + ".dll",
                    sha256 = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant()
                };
        }

        private static string RuntimeTarget(BuildTarget target)
        {
            if (target == BuildTarget.StandaloneWindows64) return RuntimePlatform.WindowsPlayer.ToString();
            if (target == BuildTarget.Android) return RuntimePlatform.Android.ToString();
            if (target == BuildTarget.iOS) return RuntimePlatform.IPhonePlayer.ToString();
            if (target == BuildTarget.WebGL) return RuntimePlatform.WebGLPlayer.ToString();
#if TUANJIE_2022_3_OR_NEWER
            if (target == BuildTarget.MiniGame) return nameof(RuntimePlatform.MiniGamePlayer);
#endif
            throw new BuildFailedException("Add an approved runtime-platform mapping for this Tuanjie target before release.");
        }
    }

    public sealed class ReleaseGate : IPreprocessBuildWithReport
    {
        public int callbackOrder => -2000;
        public void OnPreprocessBuild(BuildReport report)
        {
            if (WebDevelopmentBuild.IsDevelopmentBuild(report.summary.outputPath, report.summary.platform,
                report.summary.options, WebDevelopmentBuild.IsBuilding, WebDevelopmentBuild.IsIsolatedProject(),
                EditorUserBuildSettings.buildScriptsOnly))
            {
                BootConfiguration.Load().ValidateWebDevelopment();
                return;
            }
            if (IsAotMetadataBuild(report.summary.outputPath, report.summary.platform,
                HotUpdateBuild.IsPreparingAot, EditorUserBuildSettings.buildScriptsOnly))
                return;
            if (IsAotMetadataBuild(report.summary.outputPath, report.summary.platform, true, EditorUserBuildSettings.buildScriptsOnly))
                throw new BuildFailedException("Use InsectSpace/Build/Prepare Native AOT Metadata for the scoped intermediate build.");
            var config = BootConfiguration.Load();
            if (config.editorSimulate || config.localSmokeMode || config.webDevelopment)
                throw new BuildFailedException("Disable editorSimulate/localSmokeMode before building a Player.");
            if (!File.Exists("Assets/InsectSpace/Content/Code/code_manifest.json"))
                throw new BuildFailedException("Stage target-matched HybridCLR AOT metadata before building a Player.");
            if (NativeValidationBuild.IsValidationBuild(report.summary.outputPath, report.summary.platform,
                report.summary.options, NativeValidationBuild.IsBuilding, NativeValidationBuild.IsIsolatedProject(),
                EditorUserBuildSettings.buildScriptsOnly))
                return;
            if (MiniGameBuild.IsValidationExport(report.summary.outputPath, report.summary.platform,
                report.summary.options, MiniGameBuild.IsExporting, NativeValidationBuild.IsIsolatedProject(),
                EditorUserBuildSettings.buildScriptsOnly))
                return;
            if (WeChatBuild.IsValidationExport(report.summary.outputPath, report.summary.platform,
                report.summary.options, WeChatBuild.IsExporting, NativeValidationBuild.IsIsolatedProject(),
                EditorUserBuildSettings.buildScriptsOnly))
                return;
            if (!File.Exists("ProjectSettings/InsectSpacePlatformApproval.json"))
                throw new BuildFailedException("Platform team approval is required. See docs/WeChat-Release-Gates.md.");
        }

        public static bool IsAotMetadataBuild(string outputPath, BuildTarget target, bool sdkPreparation, bool scriptsOnly)
        {
            if (!sdkPreparation || !scriptsOnly || target == BuildTarget.NoTarget || string.IsNullOrWhiteSpace(outputPath))
                return false;
            try
            {
                string root = Path.GetFullPath(Path.Combine(SettingsUtil.HybridCLRDataDir, "StrippedAOTDllsTempProj", target.ToString()))
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string candidate = Path.GetFullPath(outputPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                return string.Equals(root, candidate, comparison) ||
                    candidate.StartsWith(root + Path.DirectorySeparatorChar, comparison);
            }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
            catch (PathTooLongException) { return false; }
        }
    }
}
