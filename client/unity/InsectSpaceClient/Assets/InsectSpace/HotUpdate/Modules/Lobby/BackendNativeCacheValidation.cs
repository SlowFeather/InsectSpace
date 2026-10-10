#if DEVELOPMENT_BUILD && ENABLE_IL2CPP && !UNITY_EDITOR && UNITY_STANDALONE_WIN
using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace InsectSpace.Gameplay.Modules
{
    internal static class BackendNativeCacheValidation
    {
        public static void RunWhenRequested()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-insectspace-validate") < 0) return;
            string directory = Path.Combine(Application.temporaryCachePath, "NativeSessionCache-" + Guid.NewGuid().ToString("N"));
            try
            {
                const string authority = "http://127.0.0.1:8081";
                // Synthetic credential, never accepted by Identity and never logged.
                string token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
                var cache = new BackendSessionCache(authority, directory);
                if (!cache.Supported || !cache.Save(token, DateTimeOffset.UtcNow.AddDays(30).ToString("o")))
                    throw new InvalidOperationException("Native DPAPI cache write failed.");
                foreach (string file in Directory.GetFiles(directory))
                    if (Encoding.UTF8.GetString(File.ReadAllBytes(file)).Contains(token))
                        throw new InvalidOperationException("Native cache contains plaintext.");
                var restored = new BackendSessionCache(authority, directory);
                if (!restored.TryLoad(out string loaded) || loaded != token)
                    throw new InvalidOperationException("Native DPAPI cache restore failed.");
                if (!restored.Clear() || restored.TryLoad(out loaded))
                    throw new InvalidOperationException("Native cache clear failed.");
                Debug.Log("[InsectSpace] NATIVE_SESSION_CACHE_PASSED protector=WindowsDPAPI");
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
    }
}
#endif
