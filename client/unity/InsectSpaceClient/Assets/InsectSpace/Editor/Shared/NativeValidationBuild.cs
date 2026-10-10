using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HybridCLR.Editor;
using HybridCLR.Editor.Installer;
using HybridCLR.Editor.Settings;
using InsectSpace.Client;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Build.Player;
using UnityEngine;

namespace InsectSpace.Editor
{
    // This development-only build is deliberately separate from platform release approval.
    public static class NativeValidationBuild
    {
        internal static bool IsBuilding { get; private set; }
        public static string OutputRoot => Path.Combine(SettingsUtil.HybridCLRDataDir, "ValidationPlayer");

        public static void Build()
        {
            if (!IsIsolatedProject() || !Application.isBatchMode)
                throw new BuildFailedException("Run tools/Invoke-Unity.ps1 -Action Native in the isolated validation project.");
            if (Application.unityVersion != "2022.3.62t16" ||
                EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
                throw new BuildFailedException("Native validation requires the pinned Tuanjie editor and Windows64 target.");
            FoundationSetup.Prepare();
            EnsureLocalToolchain();
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Standalone, ManagedStrippingLevel.Minimal);
            PlayerSettings.SetIl2CppCompilerConfiguration(BuildTargetGroup.Standalone, Il2CppCompilerConfiguration.Debug);
            EditorUserBuildSettings.development = true;
            EditorUserBuildSettings.allowDebugging = false;
            EditorUserBuildSettings.buildScriptsOnly = false;
#if UNITY_EDITOR_WIN
            UnityEditor.WindowsStandalone.UserBuildSettings.createSolution = false;
#endif
            var config = BootConfiguration.Load();
            config.editorSimulate = false;
            config.localSmokeMode = false;
            config.resourceMode = ResourceMode.Offline;
            File.WriteAllText("Assets/InsectSpace/Resources/InsectSpaceBoot.json", JsonUtility.ToJson(config, true));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            HotUpdateBuild.PrepareAotMetadata();
            HotUpdateBuild.StagePlayerMetadata();
            ContentBuild.BuildPackages();
            HotUpdateBuild.BuildCorePackage();
            Directory.CreateDirectory(OutputRoot);
            BuildReport report;
            IsBuilding = true;
            try
            {
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { FoundationSetup.ScenePath },
                    target = BuildTarget.StandaloneWindows64,
                    targetGroup = BuildTargetGroup.Standalone,
                    locationPathName = Path.Combine(OutputRoot, "InsectSpace.Validation.exe"),
                    options = BuildOptions.Development
                });
            }
            finally { IsBuilding = false; }
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("Native validation Player failed to build: " + report.summary.result);
            if (!File.Exists(Path.Combine(OutputRoot, "InsectSpace.Validation.exe")) ||
                !File.Exists(Path.Combine(OutputRoot, "GameAssembly.dll")))
                throw new BuildFailedException("Native validation requires an executable Player, not an exported solution.");
            File.WriteAllText(Path.Combine(OutputRoot, "aot-source.sha256"), AotSourceFingerprint());
            Debug.Log("[InsectSpace] NATIVE_PLAYER_BUILT " + report.summary.outputPath);
        }

        internal static void EnsureLocalToolchain()
        {
            var installer = new InstallerController();
            if (installer.PackageVersion != "8.5.0" || HybridCLRSettings.Instance.useGlobalIl2cpp)
                throw new BuildFailedException("Native validation requires the pinned, project-local HybridCLR toolchain.");
            if (!installer.HasInstalledHybridCLR() || installer.InstalledLibil2cppVersion != installer.PackageVersion)
            {
                string overlay = Environment.GetEnvironmentVariable("INSECTSPACE_NATIVE_OVERLAY");
                if (string.IsNullOrEmpty(overlay) || !Directory.Exists(Path.Combine(overlay, "hybridclr")))
                    throw new BuildFailedException("Run tools/Install-NativeToolchain.ps1 before native validation.");
                installer.InstallFromLocal(overlay);
                if (!installer.HasInstalledHybridCLR() || installer.InstalledLibil2cppVersion != installer.PackageVersion)
                    throw new BuildFailedException("Project-local HybridCLR installation failed.");
            }
        }

        public static void BuildPatch()
        {
            if (!IsIsolatedProject() || !Application.isBatchMode ||
                EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64 ||
                !File.Exists(Path.Combine(OutputRoot, "GameAssembly.dll")))
                throw new BuildFailedException("Build the isolated native validation Player first.");
            string fingerprintPath = Path.Combine(OutputRoot, "aot-source.sha256");
            if (!File.Exists(fingerprintPath) || File.ReadAllText(fingerprintPath) != AotSourceFingerprint())
                throw new BuildFailedException("AOT sources changed. Rebuild with -Action Native before testing a gameplay-only patch.");
            FoundationSetup.Prepare();
            EditorUserBuildSettings.development = true;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.IL2CPP);
            string output = SettingsUtil.GetHotUpdateDllsOutputDirByTarget(BuildTarget.StandaloneWindows64);
            PlayerBuildInterface.CompilePlayerScripts(new ScriptCompilationSettings
            {
                group = BuildTargetGroup.Standalone,
                target = BuildTarget.StandaloneWindows64,
                options = ScriptCompilationOptions.DevelopmentBuild,
                extraScriptingDefines = new[] { "INSECTSPACE_NATIVE_PATCH" }
            }, output);
            var config = BootConfiguration.Load();
            config.editorSimulate = false;
            config.localSmokeMode = false;
            config.packageVersion = "native-patch-" + Guid.NewGuid().ToString("N");
            File.WriteAllText("Assets/InsectSpace/Resources/InsectSpaceBoot.json", JsonUtility.ToJson(config, true));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            HotUpdateBuild.StagePlayerMetadata(false);
            HotUpdateBuild.BuildCorePackage();
            File.WriteAllText(Path.Combine(SettingsUtil.HybridCLRDataDir, "native-patch-output.json"),
                JsonUtility.ToJson(new PatchOutput { packageDirectory = HotUpdateBuild.LastPackageOutput, version = config.packageVersion }));
            Debug.Log("[InsectSpace] NATIVE_PATCH_BUILT version=" + config.packageVersion);
        }

        [Serializable]
        private sealed class PatchOutput
        {
            public string packageDirectory;
            public string version;
        }

        private static string AotSourceFingerprint()
        {
            string root = Path.GetFullPath(Path.Combine(SettingsUtil.ProjectDir, "../../.."));
            string[] directories =
            {
                Path.Combine(SettingsUtil.ProjectDir, "Assets/InsectSpace/Runtime"),
                Path.Combine(SettingsUtil.ProjectDir, "Assets/InsectSpace/Rendering"),
                Path.Combine(SettingsUtil.ProjectDir, "Assets/InsectSpace/LubanRuntime"),
                Path.Combine(root, "shared")
            };
            using (var sha = SHA256.Create())
            {
                var files = directories.SelectMany(path => Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                    .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".asmdef", StringComparison.Ordinal) ||
                        path.EndsWith(".xml", StringComparison.Ordinal))
                    .Concat(new[] { Path.Combine(root, "vendor/dependencies.lock.json") })
                    .OrderBy(path => path, StringComparer.Ordinal);
                var text = new StringBuilder();
                foreach (string file in files)
                    text.Append(file.Substring(root.Length).Replace('\\', '/')).Append(':')
                        .Append(BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file)))).Append('\n');
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", "").ToLowerInvariant();
            }
        }

        internal static bool IsIsolatedProject()
        {
            string expected = Path.GetFullPath(Path.Combine(SettingsUtil.ProjectDir, "../../../.artifacts/unity/ValidationClient"));
            return string.Equals(Path.GetFullPath(SettingsUtil.ProjectDir).TrimEnd('\\', '/'),
                expected.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsValidationBuild(string outputPath, BuildTarget target, BuildOptions options,
            bool scoped, bool isolated, bool scriptsOnly)
        {
            if (!scoped || !isolated || scriptsOnly || target != BuildTarget.StandaloneWindows64 ||
                (options & BuildOptions.Development) == 0 || string.IsNullOrWhiteSpace(outputPath))
                return false;
            try
            {
                return string.Equals(Path.GetFullPath(outputPath),
                    Path.GetFullPath(Path.Combine(OutputRoot, "InsectSpace.Validation.exe")),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
            catch (PathTooLongException) { return false; }
        }
    }
}
