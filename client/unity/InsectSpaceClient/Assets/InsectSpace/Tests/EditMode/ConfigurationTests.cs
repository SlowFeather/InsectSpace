using System.Linq;
using HybridCLR.Editor.Settings;
using InsectSpace.Client;
using InsectSpace.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;

namespace InsectSpace.Tests
{
    public sealed class ConfigurationTests
    {
        [Test]
        public void BootstrapIsFirstBuildScene()
        {
            FoundationSetup.Prepare();
            Assert.AreEqual(FoundationSetup.ScenePath, EditorBuildSettings.scenes[0].path);
            Assert.IsTrue(EditorBuildSettings.scenes[0].enabled);
            Assert.AreEqual(typeof(SceneAsset), AssetDatabase.GetMainAssetTypeAtPath(FoundationSetup.ScenePath));
            Assert.AreEqual(typeof(SceneAsset), AssetDatabase.GetMainAssetTypeAtPath(
                "Assets/InsectSpace/Content/WorldCommon/WorldSandbox" + FoundationSetup.SceneExtension));
        }
        [Test]
        public void HotUpdateIsSeparateAndExplicitlyConfigured()
        {
            FoundationSetup.Prepare();
            CollectionAssert.Contains(HybridCLRSettings.Instance.hotUpdateAssemblies, HotUpdateLoader.AssemblyName);
        }
        [Test]
        public void ResourceCollectorsContainCodeAndTables()
        {
            FoundationSetup.Prepare();
            var core = BundleCollectorSettingData.Setting.Packages.Single(p => p.PackageName == "Core");
            CollectionAssert.AreEquivalent(new[] { "Tables", "Code" }, core.Groups.Select(g => g.GroupName));
            Assert.IsTrue(core.EnableAddressable);
        }
        [Test]
        public void BootstrapConfigLoadsAndValidates()
        {
            var config = BootConfiguration.Load();
            config.Validate();
            Assert.IsTrue(config.localSmokeMode);
            CollectionAssert.AreEquivalent(new[] { "tbworldscene", "tbqualityprofile", "tbbattlerule", "tbbattleskill" }, config.tableLocations);
        }
        [Test]
        public void RemoteResourcesRequireHttps()
        {
            var config = new BootConfiguration { editorSimulate = false, resourceMode = ResourceMode.Host, remoteRoot = "http://example.invalid" };
            Assert.Throws<System.InvalidOperationException>(() => config.Validate());
        }
        [Test]
        public void FallbackResourcesAlsoRequireHttps()
        {
            var config = new BootConfiguration { editorSimulate = false, resourceMode = ResourceMode.Host,
                remoteRoot = "https://example.invalid", fallbackRoot = "http://example.invalid" };
            Assert.Throws<System.InvalidOperationException>(() => config.Validate());
        }
        [Test]
        public void ResourceDiscoveryIsOnlyForRemoteColdBoots()
        {
            var config = new BootConfiguration { editorSimulate = false, resourceMode = ResourceMode.Offline };
            Assert.IsFalse(YooResourceService.ShouldDiscoverVersion(config));
            config.resourceMode = ResourceMode.Host;
            Assert.IsTrue(YooResourceService.ShouldDiscoverVersion(config));
            config.discoverRemoteVersion = false;
            Assert.IsFalse(YooResourceService.ShouldDiscoverVersion(config));
            config.discoverRemoteVersion = true;
            config.editorSimulate = true;
            Assert.IsFalse(YooResourceService.ShouldDiscoverVersion(config));
        }
        [Test]
        public void MotherPackageIdentityIsRequired()
        {
            Assert.Throws<System.InvalidOperationException>(() => new BootConfiguration { playerBuildId = "" }.Validate());
        }
        [TestCase("Core", "v1")]
        [TestCase("../World", "v1")]
        [TestCase("World", "../v1")]
        [TestCase("World", "")]
        public void ContentPackageIdentityRejectsTraversalAndCoreAliases(string name, string version)
        {
            Assert.Throws<System.InvalidOperationException>(() =>
                new ContentPackageVersion { name = name, version = version }.Validate("Core"));
        }
        [Test]
        public void ThreeQualityProfilesExist()
        {
            Assert.AreEqual(3, QualitySettings.names.Length);
        }

        [Test]
        public void CodeTargetMustBeAnExactNamedRuntime()
        {
            Assert.IsTrue(HotUpdateLoader.MatchesRuntime("WindowsPlayer", RuntimePlatform.WindowsPlayer));
            foreach (string target in new[] { null, "", "windowsplayer", "2", "WindowsPlayer ", "Android" })
                Assert.IsFalse(HotUpdateLoader.MatchesRuntime(target, RuntimePlatform.WindowsPlayer));
        }

        [Test]
        public void OfflineWebResourcesDoNotConstructARemoteFileSystem()
        {
            var options = YooResourceService.CreateWebOptions(new BootConfiguration
                { editorSimulate = false, resourceMode = ResourceMode.Offline }, "Core", true);
            Assert.IsNotNull(options.WebServerFileSystemParameters);
            Assert.IsNull(options.WebNetworkFileSystemParameters);
        }

        [TestCase(ResourceMode.Web)]
        [TestCase(ResourceMode.Host)]
        public void RemoteWebResourcesKeepBothPackageSources(ResourceMode mode)
        {
            var options = YooResourceService.CreateWebOptions(new BootConfiguration
                { editorSimulate = false, resourceMode = mode, remoteRoot = "https://example.invalid" }, "WorldCommon", true);
            Assert.IsNotNull(options.WebServerFileSystemParameters);
            Assert.IsNotNull(options.WebNetworkFileSystemParameters);
        }

#if TUANJIE_2022_3_OR_NEWER
        [Test]
        public void MiniGameRuntimeAcceptsNamedEngineAliasesButNotWebGl()
        {
            var runtime = RuntimePlatform.MiniGamePlayer;
            Assert.IsTrue(HotUpdateLoader.MatchesRuntime(nameof(RuntimePlatform.MiniGamePlayer), runtime));
            foreach (string name in System.Enum.GetNames(typeof(RuntimePlatform)))
                if ((RuntimePlatform)System.Enum.Parse(typeof(RuntimePlatform), name) == runtime)
                    Assert.IsTrue(HotUpdateLoader.MatchesRuntime(name, runtime));
            Assert.IsFalse(HotUpdateLoader.MatchesRuntime(nameof(RuntimePlatform.WebGLPlayer), runtime));
            Assert.IsFalse(HotUpdateLoader.MatchesRuntime(((int)runtime).ToString(), runtime));
        }
#endif
    }
}
