using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using InsectSpace.Client;
using UnityEngine;
using YooAsset;

namespace InsectSpace.Tests
{
    // Invoked only by Test-WSLResources.ps1 in an empty, temporary Play session.
    public sealed class BackendResourceProbe : MonoBehaviour
    {
        public static string Root = "http://127.0.0.1:18088";
        public bool Done { get; private set; }
        public string Error { get; private set; }
        public string Version { get; private set; }
        public int Downloads { get; private set; }
        public int TableBytes { get; private set; }
        public int RemoteTableReads { get; private set; }
        public float Progress => resources?.Progress ?? 0f;
        private YooResourceService resources;
        private string cacheRoot;

        private IEnumerator Start()
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(Verify());
            try
            {
                while (stack.Count > 0)
                {
                    var routine = stack.Peek();
                    object current = null; bool moved = false;
                    try { moved = routine.MoveNext(); if (moved) current = routine.Current; }
                    catch (Exception exception) { Error = exception.Message; }
                    if (Error != null) yield break;
                    if (!moved) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                    if (current is IEnumerator nested && !(current is AsyncOperationBase) && !(current is HandleBase) && !(current is CustomYieldInstruction)) stack.Push(nested);
                    else yield return current;
                }
            }
            finally { while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose(); Done = true; }
        }

        private IEnumerator Verify()
        {
            cacheRoot = Path.Combine(Application.temporaryCachePath, "WslResources-" + Guid.NewGuid().ToString("N"));
            resources = new YooResourceService((config, name, raw) =>
            {
#if UNITY_WEBGL
                // Only remote files are registered: no StreamingAssets or local fallback.
                var web = new CustomPlayModeOptions();
                web.FileSystemParameterList.Add(FileSystemParameters.CreateDefaultWebNetworkFileSystemParameters(new Remote(Root + "/" + name), true));
                return web;
#else
                return new HostPlayModeOptions
                {
                BuiltinFileSystemParameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters(Path.Combine(cacheRoot, "empty-builtin")),
                CacheFileSystemParameters = FileSystemParameters.CreateDefaultSandboxFileSystemParameters(new Remote(Root + "/" + name), Path.Combine(cacheRoot, "download"))
                };
#endif
            });
            var config = new BootConfiguration { editorSimulate = false, localSmokeMode = false, localBackendMode = true, resourceMode = ResourceMode.Host, remoteRoot = Root };
            config.Validate();
            yield return resources.Initialize(config);
            Version = resources.ActiveVersion; Downloads = resources.PendingDownloadCount;
#if !UNITY_WEBGL
            if (Downloads < 1) throw new InvalidOperationException("Expected downloads from the WSL host into an empty cache.");
#endif
            // WebGL downloads RawBundle data on demand; PendingDownloadCount can be zero.
            yield return resources.ReadBytes("tbworldscene", bytes => { TableBytes += bytes.Length; RemoteTableReads++; });
            yield return resources.ReadBytes("tbqualityprofile", bytes => { TableBytes += bytes.Length; RemoteTableReads++; });
            if (TableBytes <= 0 || Progress != 1f) throw new InvalidOperationException("Downloaded tables or progress are incomplete.");
        }

        private void OnDestroy()
        {
            resources?.Dispose();
            if (!string.IsNullOrEmpty(cacheRoot) && Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, true);
        }

        private sealed class Remote : IRemoteService
        {
            private readonly string root;
            public Remote(string value) { root = value; }
            public IReadOnlyList<string> GetRemoteUrls(string fileName) => new[] { root + "/" + fileName };
        }
    }
}
