using System.IO;
using HybridCLR.Editor;
using InsectSpace.Editor;
using NUnit.Framework;
using UnityEditor;

namespace InsectSpace.Tests
{
    public sealed class BuildIntentTests
    {
        private static string StagingRoot => Path.Combine(SettingsUtil.HybridCLRDataDir, "StrippedAOTDllsTempProj",
            BuildTarget.StandaloneWindows64.ToString());

        [Test]
        public void SdkMetadataBuildDoesNotRequireItsOwnFutureMetadata()
        {
            Assert.IsTrue(ReleaseGate.IsAotMetadataBuild(Path.Combine(StagingRoot, "Foundation.exe"),
                BuildTarget.StandaloneWindows64, true, true));
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        public void BothSdkContextAndScriptsOnlyAreRequired(bool sdkContext, bool scriptsOnly)
        {
            Assert.IsFalse(ReleaseGate.IsAotMetadataBuild(Path.Combine(StagingRoot, "Foundation.exe"),
                BuildTarget.StandaloneWindows64, sdkContext, scriptsOnly));
        }

        [Test]
        public void RegularPlayerOutputCannotBypassReleaseChecks()
        {
            Assert.IsFalse(ReleaseGate.IsAotMetadataBuild("Build/Player.exe",
                BuildTarget.StandaloneWindows64, true, true));
        }

        [Test]
        public void SiblingPrefixCannotBypassReleaseChecks()
        {
            Assert.IsFalse(ReleaseGate.IsAotMetadataBuild(StagingRoot + "-release/Player.exe",
                BuildTarget.StandaloneWindows64, true, true));
        }

        [Test]
        public void RelativeTraversalCannotEscapeMetadataRoot()
        {
            Assert.IsFalse(ReleaseGate.IsAotMetadataBuild(Path.Combine(StagingRoot, "..", "Player.exe"),
                BuildTarget.StandaloneWindows64, true, true));
        }

        [Test]
        public void WrongTargetCannotBypassReleaseChecks()
        {
            Assert.IsFalse(ReleaseGate.IsAotMetadataBuild(Path.Combine(StagingRoot, "Foundation.exe"),
                BuildTarget.Android, true, true));
        }

        [Test]
        public void MissingOutputCannotBypassReleaseChecks()
        {
            Assert.IsFalse(ReleaseGate.IsAotMetadataBuild(null, BuildTarget.StandaloneWindows64, true, true));
        }

        [Test]
        public void IsolatedDevelopmentPlayerHasItsOwnValidationIntent()
        {
            Assert.IsTrue(NativeValidationBuild.IsValidationBuild(
                Path.Combine(NativeValidationBuild.OutputRoot, "InsectSpace.Validation.exe"),
                BuildTarget.StandaloneWindows64, BuildOptions.Development, true, true, false));
        }

        [TestCase(false, true, false, BuildOptions.Development)]
        [TestCase(true, false, false, BuildOptions.Development)]
        [TestCase(true, true, true, BuildOptions.Development)]
        [TestCase(true, true, false, BuildOptions.None)]
        public void ValidationCannotGrantProductionBuildApproval(bool scope, bool isolated, bool scriptsOnly, BuildOptions options)
        {
            Assert.IsFalse(NativeValidationBuild.IsValidationBuild(
                Path.Combine(NativeValidationBuild.OutputRoot, "InsectSpace.Validation.exe"),
                BuildTarget.StandaloneWindows64, options, scope, isolated, scriptsOnly));
        }

        [Test]
        public void ValidationRejectsOtherOutputsAndPlatforms()
        {
            Assert.IsFalse(NativeValidationBuild.IsValidationBuild(
                Path.Combine(NativeValidationBuild.OutputRoot, "..", "Release.exe"),
                BuildTarget.StandaloneWindows64, BuildOptions.Development, true, true, false));
            Assert.IsFalse(NativeValidationBuild.IsValidationBuild(
                Path.Combine(NativeValidationBuild.OutputRoot, "InsectSpace.Validation.exe"),
                BuildTarget.Android, BuildOptions.Development, true, true, false));
        }

#if TUANJIE_2022_3_OR_NEWER
        [Test]
        public void MiniGameExportRequiresItsExactIsolatedDevelopmentRoot()
        {
            Assert.IsTrue(MiniGameBuild.IsValidationExport(MiniGameBuild.NativeOutputRoot,
                BuildTarget.MiniGame, BuildOptions.Development, true, true, false));
            foreach (string path in new[] { null, "", MiniGameBuild.NativeOutputRoot + "-release",
                Path.Combine(MiniGameBuild.NativeOutputRoot, "child"),
                Path.Combine(MiniGameBuild.NativeOutputRoot, "..", "Release") })
                Assert.IsFalse(MiniGameBuild.IsValidationExport(path,
                    BuildTarget.MiniGame, BuildOptions.Development, true, true, false));
            Assert.IsFalse(MiniGameBuild.IsValidationExport(MiniGameBuild.NativeOutputRoot,
                BuildTarget.WebGL, BuildOptions.Development, true, true, false));
        }

        [TestCase(false, true, false, BuildOptions.Development)]
        [TestCase(true, false, false, BuildOptions.Development)]
        [TestCase(true, true, true, BuildOptions.Development)]
        [TestCase(true, true, false, BuildOptions.None)]
        public void MiniGameExportCannotGrantProductionApproval(bool scope, bool isolated,
            bool scriptsOnly, BuildOptions options)
        {
            Assert.IsFalse(MiniGameBuild.IsValidationExport(MiniGameBuild.NativeOutputRoot,
                BuildTarget.MiniGame, options, scope, isolated, scriptsOnly));
        }

        [Test]
        public void WeChatConversionOnlyPermitsItsOwnIntermediatePlayer()
        {
            string expected = Path.Combine(WeChatBuild.OutputRoot, "webgl");
            Assert.IsTrue(WeChatBuild.IsValidationExport(expected,
                BuildTarget.MiniGame, BuildOptions.Development, true, true, false));
            foreach (string path in new[] { null, "", expected + "-release", MiniGameBuild.NativeOutputRoot,
                Path.Combine(expected, "..", "minigame"), Path.Combine(expected, "child") })
                Assert.IsFalse(WeChatBuild.IsValidationExport(path,
                    BuildTarget.MiniGame, BuildOptions.Development, true, true, false));
            Assert.IsFalse(WeChatBuild.IsValidationExport(expected,
                BuildTarget.WebGL, BuildOptions.Development, true, true, false));
        }

        [TestCase(false, true, false, BuildOptions.Development)]
        [TestCase(true, false, false, BuildOptions.Development)]
        [TestCase(true, true, true, BuildOptions.Development)]
        [TestCase(true, true, false, BuildOptions.None)]
        public void WeChatConversionCannotGrantProductionApproval(bool scope, bool isolated,
            bool scriptsOnly, BuildOptions options)
        {
            Assert.IsFalse(WeChatBuild.IsValidationExport(Path.Combine(WeChatBuild.OutputRoot, "webgl"),
                BuildTarget.MiniGame, options, scope, isolated, scriptsOnly));
        }
#endif
    }
}
