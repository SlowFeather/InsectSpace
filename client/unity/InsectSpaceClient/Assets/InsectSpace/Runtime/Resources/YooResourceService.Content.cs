using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;

namespace InsectSpace.Client
{
    public sealed partial class YooResourceService
    {
        private BootConfiguration settings;
        private bool disposed;
        private bool catalogBound;
        private readonly Dictionary<string, string> contentVersions = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, ContentState> content = new Dictionary<string, ContentState>(StringComparer.Ordinal);

        private sealed class ContentState
        {
            public ResourcePackage Package;
            public bool Done;
            public Exception Error;
        }

        public void BindContentCatalog(ContentPackageVersion[] packages)
        {
            if (!Ready || disposed) throw new InvalidOperationException("Initialize Core before binding content.");
            if (catalogBound) throw new InvalidOperationException("Content versions cannot change inside an active session.");
            if (packages == null) throw new ArgumentNullException(nameof(packages));
            var validated = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in packages)
            {
                if (entry == null) throw new InvalidOperationException("Null content package.");
                entry.Validate(settings.packageName);
                validated.Add(entry.name, entry.version);
            }
            foreach (var pair in validated) contentVersions.Add(pair.Key, pair.Value);
            catalogBound = true;
        }

        public IEnumerator PrepareContentPackage(string packageName)
        {
            if (disposed) throw new ObjectDisposedException(nameof(YooResourceService));
            if (!Ready || !catalogBound || !contentVersions.TryGetValue(packageName, out var version))
                throw new InvalidOperationException("Content package is not part of this Core release: " + packageName);
            if (!content.TryGetValue(packageName, out var state))
            {
                state = new ContentState { Package = YooAssets.CreatePackage(packageName) };
                content.Add(packageName, state);
                bool completed = false;
                try
                {
                    yield return CoroutineGuard.Run(PrepareContent(state.Package, packageName, version), error => state.Error = error);
                    completed = state.Error == null;
                }
                finally
                {
                    state.Done = true;
                    if (!completed && state.Error == null)
                        state.Error = new OperationCanceledException("Content preparation was interrupted.");
                }
            }
            else
                while (!state.Done && !disposed) yield return null;
            if (disposed) throw new ObjectDisposedException(nameof(YooResourceService));
            if (state.Error != null) throw new InvalidOperationException("Content preparation failed: " + packageName, state.Error);
        }

        private IEnumerator PrepareContent(ResourcePackage target, string name, string version)
        {
            Progress = 0f; ProgressStage = "Preparing " + name;
            var initialize = target.InitializePackageAsync(CreateOptions(name, false));
            yield return initialize;
            Check(initialize);
#if UNITY_EDITOR
            if (settings.editorSimulate)
            {
                var request = target.RequestPackageVersionAsync(new RequestPackageVersionOptions(true, 30));
                yield return request;
                Check(request);
                version = request.PackageVersion;
            }
#endif
            var manifest = target.LoadPackageManifestAsync(new LoadPackageManifestOptions(version, 30));
            ProgressStage = "Loading " + name + " manifest";
            yield return manifest;
            Check(manifest);
            var download = target.CreateResourceDownloader(new ResourceDownloaderOptions((string[])null, 4, 2));
            if (download.TotalDownloadCount > 0)
            {
                download.StartDownload();
                ProgressStage = "Downloading " + name;
                while (!download.IsDone) { Progress = download.Progress; yield return null; }
                Check(download);
            }
            Progress = 1f; ProgressStage = name + " ready";
            Debug.Log("[InsectSpace] CONTENT_PACKAGE_READY package=" + name + " version=" + version);
        }

        // The caller owns these native YooAsset handles. Destroy prefab instances before
        // releasing asset handles; await SceneHandle.UnloadSceneAsync before shutting down Core.
        public AssetHandle LoadAsset<T>(string packageName, string address) where T : UnityEngine.Object =>
            GetContent(packageName).LoadAssetAsync<T>(address);

        public YooAsset.SceneHandle LoadScene(string packageName, string address) =>
            GetContent(packageName).LoadSceneAsync(address, LoadSceneMode.Additive);

        private ResourcePackage GetContent(string name)
        {
            if (disposed) throw new ObjectDisposedException(nameof(YooResourceService));
            if (!content.TryGetValue(name, out var state) || !state.Done || state.Error != null)
                throw new InvalidOperationException("Await PrepareContentPackage before loading: " + name);
            return state.Package;
        }

        private InitializePackageOptions CreateOptions(string name, bool raw)
        {
            if (platformOptions != null)
                return platformOptions(settings, name, raw) ??
                    throw new InvalidOperationException("The platform adapter returned no resource file system.");
#if UNITY_EDITOR
            if (settings.editorSimulate)
            {
                var result = EditorSimulateBuildInvoker.Build(name,
                    (int)(raw ? EBundleType.VirtualRawBundle : EBundleType.VirtualAssetBundle));
                return new EditorSimulateModeOptions
                {
                    EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(result.PackageRootDirectory)
                };
            }
#endif
#if UNITY_WEBGL && !UNITY_EDITOR
            return CreateWebOptions(settings, name, false);
#else
            if (settings.resourceMode == ResourceMode.Offline)
                return new OfflinePlayModeOptions
                {
                    BuiltinFileSystemParameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters()
                };
            if (settings.resourceMode == ResourceMode.Host)
                return new HostPlayModeOptions
                {
                    BuiltinFileSystemParameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters(),
                    CacheFileSystemParameters = FileSystemParameters.CreateDefaultSandboxFileSystemParameters(new Remote(settings, name))
                };
            return CreateWebOptions(settings, name, false);
#endif
        }

        public static WebPlayModeOptions CreateWebOptions(BootConfiguration config, string name, bool disableUnityCache)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Package name is required.", nameof(name));
            return new WebPlayModeOptions
            {
                WebServerFileSystemParameters = FileSystemParameters.CreateDefaultWebServerFileSystemParameters(disableUnityCache),
                WebNetworkFileSystemParameters = config.resourceMode == ResourceMode.Offline ? null :
                    FileSystemParameters.CreateDefaultWebNetworkFileSystemParameters(new Remote(config, name), disableUnityCache)
            };
        }
    }
}
