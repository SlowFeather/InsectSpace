#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Framework.Network;
using InsectSpace.Client;
using InsectSpace.Network;
using UnityEngine;
using YooAsset;

namespace InsectSpace.Gameplay.Modules
{
    // Explicit Editor development route: remote assets, already compiled Editor gameplay.
    // Native HybridCLR execution is verified separately in the isolated IL2CPP Player.
    internal sealed class BackendEditorRemoteResources : IClientPlatformAdapter
    {
        private readonly string root;
        private readonly string cache = Path.Combine(Application.temporaryCachePath, "BackendRemote-" + Guid.NewGuid().ToString("N"));

        private BackendEditorRemoteResources(string root) { this.root = root.TrimEnd('/'); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InstallWhenRequested()
        {
            if (Environment.GetEnvironmentVariable("INSECTSPACE_EDITOR_BACKEND") != "true") return;
            string endpoint = Environment.GetEnvironmentVariable("INSECTSPACE_EDITOR_RESOURCE_ROOT");
            if (string.IsNullOrEmpty(endpoint)) return;
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp ||
                uri.Host != "127.0.0.1" || !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath != "/")
                throw new InvalidOperationException("Editor WSL resources require an explicit loopback root.");
            PlatformServices.Install(new BackendEditorRemoteResources(uri.AbsoluteUri));
            Debug.Log("[InsectSpace] EDITOR_REMOTE_RESOURCES cold-cache; compiled Editor gameplay, no native injection.");
        }

        public IPlatformChannelFactory CreateChannels(INetworkManager manager) => new DesktopChannelFactory(manager);
        public IChannelAddressResolver CreateAddressResolver() => new DesktopAddressResolver();

        public InitializePackageOptions CreateResourceOptions(BootConfiguration configuration, string packageName, bool rawFiles)
        {
            var remote = new Remote(root + "/" + packageName);
#if UNITY_WEBGL
            var options = new CustomPlayModeOptions();
            options.FileSystemParameterList.Add(FileSystemParameters.CreateDefaultWebNetworkFileSystemParameters(remote, true));
            return options;
#else
            var options = new CustomPlayModeOptions();
            options.FileSystemParameterList.Add(FileSystemParameters.CreateDefaultSandboxFileSystemParameters(remote, Path.Combine(cache, "download")));
            return options;
#endif
        }

        private sealed class Remote : IRemoteService
        {
            private readonly string root;
            public Remote(string root) { this.root = root; }
            public IReadOnlyList<string> GetRemoteUrls(string fileName) => new[] { root + "/" + fileName };
        }
    }
}
#endif
