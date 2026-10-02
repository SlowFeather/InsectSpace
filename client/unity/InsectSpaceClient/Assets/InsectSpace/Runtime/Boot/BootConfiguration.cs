using System;
using UnityEngine;
using InsectSpace.Contracts;

namespace InsectSpace.Client
{
    public enum ResourceMode { Offline, Host, Web }

    [Serializable]
    public sealed class BootConfiguration
    {
        public bool editorSimulate = true;
        public bool localSmokeMode = true;
        public bool useWeChatSdk;
        public bool webDevelopment;
        public ResourceMode resourceMode = ResourceMode.Offline;
        public string packageName = "Core";
        public string packageVersion = "foundation-001";
        public string playerBuildId = "foundation-native-001";
        public bool discoverRemoteVersion = true;
        public ContentPackageVersion[] contentPackages = Array.Empty<ContentPackageVersion>();
        public string remoteRoot = "";
        public string fallbackRoot = "";
        public QualityTier quality = QualityTier.Medium;
        public string[] tableLocations = { "tbworldscene", "tbqualityprofile" };

        public static BootConfiguration Load()
        {
            var text = Resources.Load<TextAsset>("InsectSpaceBoot");
            if (text == null) throw new InvalidOperationException("Missing Resources/InsectSpaceBoot.json.");
            try
            {
                var config = JsonUtility.FromJson<BootConfiguration>(text.text);
#if DEVELOPMENT_BUILD && ENABLE_IL2CPP && !UNITY_EDITOR && !INSECTSPACE_WEB_DEVELOPMENT
                NativeValidationProbe.ConfigureRemoteRun(config);
#endif
                return config;
            }
            finally { Resources.UnloadAsset(text); }
        }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(packageName) || string.IsNullOrWhiteSpace(packageVersion) ||
                string.IsNullOrWhiteSpace(playerBuildId) ||
                tableLocations == null || tableLocations.Length == 0)
                throw new InvalidOperationException("Invalid bootstrap configuration.");
#if UNITY_WEBGL && INSECTSPACE_WEB_DEVELOPMENT && DEVELOPMENT_BUILD && !UNITY_EDITOR
            ValidateWebDevelopment();
#else
            if (webDevelopment)
                throw new InvalidOperationException("Web development configuration requires the explicit Development Web build.");
#if !UNITY_EDITOR
            if (editorSimulate || localSmokeMode)
                throw new InvalidOperationException("Editor/smoke settings cannot be used in a Player.");
#endif
#endif
            if ((!editorSimulate && resourceMode != ResourceMode.Offline) &&
                (!IsPermittedRemoteRoot(remoteRoot) ||
                 (!string.IsNullOrEmpty(fallbackRoot) && !IsPermittedRemoteRoot(fallbackRoot))))
                throw new InvalidOperationException("Remote resources require an HTTPS root.");
        }

        public void ValidateWebDevelopment()
        {
            if (!webDevelopment || editorSimulate || !localSmokeMode || useWeChatSdk ||
                resourceMode != ResourceMode.Offline || discoverRemoteVersion ||
                !string.IsNullOrEmpty(remoteRoot) || !string.IsNullOrEmpty(fallbackRoot))
                throw new InvalidOperationException("Web development requires explicit local mode and bundled resources; online failures never select this mode.");
        }

        private static bool IsPermittedRemoteRoot(string root)
        {
            if (!Uri.TryCreate(root, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo)) return false;
            if (uri.Scheme == Uri.UriSchemeHttps) return true;
#if DEVELOPMENT_BUILD && ENABLE_IL2CPP && !UNITY_EDITOR && !INSECTSPACE_WEB_DEVELOPMENT
            return NativeValidationProbe.IsLoopbackRun && uri.Scheme == Uri.UriSchemeHttp && uri.Host == "127.0.0.1";
#else
            return false;
#endif
        }
    }
}
