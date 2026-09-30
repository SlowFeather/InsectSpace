using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using YooAsset;

namespace InsectSpace.Client
{
    public sealed partial class YooResourceService : IDisposable
    {
        private ResourcePackage package;
        private bool ownsYoo;
        private readonly Func<BootConfiguration, string, bool, InitializePackageOptions> platformOptions;
        public bool Ready => !disposed && ActiveVersion != null && package != null && package.PackageValid;
        public string ActiveVersion { get; private set; }
        public int PendingDownloadCount { get; private set; }

        public YooResourceService(Func<BootConfiguration, string, bool, InitializePackageOptions> platformOptions = null)
        {
            this.platformOptions = platformOptions;
        }

        public IEnumerator Initialize(BootConfiguration config)
        {
            if (disposed) throw new ObjectDisposedException(nameof(YooResourceService));
            if (package != null) throw new InvalidOperationException("Resource service already initialized.");
            if (YooAssets.IsInitialized) throw new InvalidOperationException("Bootstrap must own the YooAsset lifetime.");
            settings = JsonUtility.FromJson<BootConfiguration>(JsonUtility.ToJson(config));
            config = settings;
            YooAssets.Initialize();
            ownsYoo = true;
            package = YooAssets.CreatePackage(config.packageName);
            var initialize = package.InitializePackageAsync(CreateOptions(config.packageName, true));
            yield return initialize;
            Check(initialize);
            string version = config.packageVersion;
            bool discover = ShouldDiscoverVersion(config);
#if UNITY_EDITOR
            discover |= config.editorSimulate;
#endif
            if (discover)
            {
                var request = package.RequestPackageVersionAsync(new RequestPackageVersionOptions(true, 30));
                yield return request;
                Check(request);
                version = request.PackageVersion;
            }
            // Resolve once, then pin code and tables to this same immutable Core manifest.
            var manifest = package.LoadPackageManifestAsync(new LoadPackageManifestOptions(version, 30));
            yield return manifest;
            Check(manifest);
            var download = package.CreateResourceDownloader(new ResourceDownloaderOptions((string[])null, 4, 2));
            PendingDownloadCount = download.TotalDownloadCount;
            if (download.TotalDownloadCount > 0)
            {
                download.StartDownload();
                yield return download;
                Check(download);
            }
            ActiveVersion = version;
#if UNITY_EDITOR
            if (config.editorSimulate) BindContentCatalog(config.contentPackages);
#endif
            Debug.Log("[InsectSpace] RESOURCE_PACKAGE_READY package=" + config.packageName +
                " version=" + version + " downloaded=" + PendingDownloadCount);
        }

        public static bool ShouldDiscoverVersion(BootConfiguration config) =>
            config.discoverRemoteVersion && config.resourceMode != ResourceMode.Offline && !config.editorSimulate;

        public IEnumerator ReadBytes(string address, Action<byte[]> completed)
        {
            if (!Ready) throw new InvalidOperationException("Resources have not initialized.");
            var handle = package.LoadAssetAsync<RawFileObject>(address);
            try
            {
                yield return handle;
                if (handle.Status != EOperationStatus.Succeeded) throw new InvalidOperationException(handle.Error);
                var bytes = handle.GetAssetObject<RawFileObject>()?.GetBytes();
                if (bytes == null || bytes.Length == 0) throw new InvalidOperationException("Empty resource: " + address);
                completed(bytes);
            }
            finally { handle.Release(); }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            content.Clear();
            package = null;
            ActiveVersion = null;
            if (ownsYoo) { ownsYoo = false; YooAssets.Destroy(); }
        }

        private static void Check(AsyncOperationBase operation)
        {
            if (operation.Status != EOperationStatus.Succeeded)
                throw new InvalidOperationException(operation.Error);
        }

        private sealed class Remote : IRemoteService
        {
            private readonly string primary;
            private readonly string fallback;
            public Remote(BootConfiguration config, string packageName)
            {
                primary = config.remoteRoot.TrimEnd('/') + "/" + packageName + "/";
                fallback = string.IsNullOrEmpty(config.fallbackRoot) ? primary :
                    config.fallbackRoot.TrimEnd('/') + "/" + packageName + "/";
            }
            public IReadOnlyList<string> GetRemoteUrls(string fileName) =>
                primary == fallback ? new[] { primary + fileName } : new[] { primary + fileName, fallback + fileName };
        }
    }
}
