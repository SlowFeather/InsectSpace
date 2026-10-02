using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using HybridCLR;
using UnityEngine;

namespace InsectSpace.Client
{
    [Serializable]
    public sealed class CodePayload
    {
        public string address;
        public string assemblyName;
        public string sha256;
    }

    [Serializable]
    public sealed class CodeManifest
    {
        public int contractVersion = 1;
        public string target;
        public string playerBuildId;
        public ContentPackageVersion[] contentPackages = Array.Empty<ContentPackageVersion>();
        public CodePayload[] aot;
        public CodePayload[] hotUpdate;
    }

    public sealed class HotUpdateLoader
    {
        public const string AssemblyName = "InsectSpace.Gameplay.HotUpdate";
        public const string EntryType = "InsectSpace.Gameplay.HotUpdateEntry";
        // Loaded assemblies cannot be unloaded by rebuilding the bootstrap object.
        private static bool injected;

        public IEnumerator Load(YooResourceService resources, BootConfiguration config, Action<Assembly> complete)
        {
#if UNITY_WEBGL && INSECTSPACE_WEB_DEVELOPMENT && !UNITY_EDITOR
            config.ValidateWebDevelopment();
            var compiled = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(a => a.GetName().Name == AssemblyName);
            if (compiled == null) throw new InvalidOperationException("Web development gameplay assembly is missing.");
            resources.BindContentCatalog(config.contentPackages);
            Debug.Log("[InsectSpace] WEB_DEVELOPMENT_CODE: compiled gameplay; HybridCLR injection is not exercised.");
            complete(compiled);
            yield break;
#else
#if UNITY_EDITOR
            if (config.editorSimulate)
            {
                var loaded = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(a => a.GetName().Name == AssemblyName);
                if (loaded == null) throw new InvalidOperationException("Editor gameplay assembly is missing.");
                Debug.Log("[InsectSpace] Editor assembly mode. Native HybridCLR injection is not exercised.");
                complete(loaded);
                yield break;
            }
#endif
            if (injected) throw new InvalidOperationException("Code cannot be injected twice. Restart the application.");
            if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == AssemblyName))
                throw new InvalidOperationException("Gameplay was included in the Player instead of being loaded as a patch.");
            byte[] json = null;
            yield return resources.ReadBytes("code_manifest", value => json = value);
            var manifest = JsonUtility.FromJson<CodeManifest>(Encoding.UTF8.GetString(json));
            if (manifest == null || manifest.contractVersion != Contracts.ProtocolVersion.Current ||
                manifest.hotUpdate == null || manifest.hotUpdate.Length != 1 || manifest.aot == null ||
                manifest.hotUpdate[0].assemblyName != AssemblyName ||
                manifest.playerBuildId != config.playerBuildId ||
                !MatchesRuntime(manifest.target, Application.platform))
                throw new InvalidOperationException("Incompatible code manifest.");

            // Read and hash every payload before making irreversible runtime changes.
            var payloads = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var entry in manifest.aot.Concat(manifest.hotUpdate))
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.assemblyName) ||
                    string.IsNullOrWhiteSpace(entry.address) || entry.sha256 == null || entry.sha256.Length != 64)
                    throw new InvalidOperationException("Invalid code payload descriptor.");
                byte[] bytes = null;
                yield return resources.ReadBytes(entry.address, value => bytes = value);
                using (var sha = SHA256.Create())
                {
                    string actual = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                    if (!string.Equals(actual, entry.sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Code payload checksum mismatch: " + entry.assemblyName);
                }
                payloads.Add(entry.assemblyName, bytes);
            }
            resources.BindContentCatalog(manifest.contentPackages);
            injected = true;
            foreach (var entry in manifest.aot)
            {
                var result = RuntimeApi.LoadMetadataForAOTAssembly(payloads[entry.assemblyName], HomologousImageMode.SuperSet);
                if (result != LoadImageErrorCode.OK)
                    throw new InvalidOperationException("AOT metadata rejected: " + entry.assemblyName + " / " + result);
            }
            var assembly = Assembly.Load(payloads[AssemblyName]);
            if (assembly.GetName().Name != AssemblyName) throw new InvalidOperationException("Unexpected gameplay assembly.");
            Debug.Log("[InsectSpace] NATIVE_CODE_LOADED aot=" + manifest.aot.Length +
                " assembly=" + assembly.GetName().Name + " sha256=" + manifest.hotUpdate[0].sha256);
            complete(assembly);
#endif
        }

        public static IHotUpdateApplication CreateApplication(Assembly assembly, BootContext context)
        {
            var method = assembly.GetType(EntryType, true).GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
            if (method == null) throw new MissingMethodException(EntryType, "Create");
            var application = method.Invoke(null, new object[] { context }) as IHotUpdateApplication;
            return application ?? throw new InvalidOperationException("Hot-update entry did not implement the AOT contract.");
        }

        public static bool MatchesRuntime(string target, RuntimePlatform current) =>
            !string.IsNullOrEmpty(target) && Enum.IsDefined(typeof(RuntimePlatform), target) &&
            Enum.TryParse(target, out RuntimePlatform parsed) && parsed == current;
    }
}
