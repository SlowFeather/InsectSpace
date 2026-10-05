using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using InsectSpace.Client;
using InsectSpace.Gameplay.Modules;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace InsectSpace.Tests
{
    public sealed class BackendSessionTests
    {
        private string directory;
        [SetUp] public void SetUp() { directory = Path.Combine(Path.GetTempPath(), "InsectSpace-SessionTest-" + Guid.NewGuid().ToString("N")); }
        [TearDown] public void TearDown() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        [Test]
        public void DpapiCacheSurvivesNewInstanceAndDoesNotStorePlaintext()
        {
            string token = NewToken();
            var cache = new BackendSessionCache("http://127.0.0.1:18081", directory);
            Assert.IsTrue(cache.Save(token, DateTimeOffset.UtcNow.AddDays(30).ToString("O")));
            Assert.IsFalse(Encoding.UTF8.GetString(File.ReadAllBytes(Directory.GetFiles(directory).Single())).Contains(token));
            Assert.IsTrue(new BackendSessionCache("http://127.0.0.1:18081", directory).TryLoad(out var recovered));
            Assert.IsTrue(recovered == token, "Recovered cache differs; credentials suppressed.");
            Assert.IsFalse(new BackendSessionCache("http://127.0.0.1:18082", directory).TryLoad(out _));
            Assert.IsTrue(cache.Clear()); Assert.IsFalse(cache.Exists);
        }

        [Test]
        public void TamperedCacheIsClearedWithoutAuthenticating()
        {
            var cache = new BackendSessionCache("http://127.0.0.1:18081", directory);
            Assert.IsTrue(cache.Save(NewToken(), DateTimeOffset.UtcNow.AddDays(30).ToString("O")));
            File.WriteAllBytes(Directory.GetFiles(directory).Single(), new byte[] { 1, 2, 3 });
            Assert.IsFalse(cache.TryLoad(out _)); Assert.IsFalse(cache.Exists);
        }
#endif
        [Test]
        public void ExpiredOrMalformedCacheIsRejected()
        {
            var protector = new TestOnlyProtector();
            var cache = new BackendSessionCache("http://127.0.0.1:18081", directory, protector);
            Assert.IsFalse(cache.Save(NewToken(), DateTimeOffset.UtcNow.AddDays(-1).ToString("O")));
            Assert.IsTrue(cache.Save(NewToken(), DateTimeOffset.UtcNow.AddDays(30).ToString("O")));
            protector.DecodedOverride = "{\"token\":\"" + NewToken() + "\",\"expiresAt\":\"2000-01-01T00:00:00Z\",\"authority\":\"http://127.0.0.1:18081\"}";
            Assert.IsFalse(cache.TryLoad(out _)); Assert.IsFalse(cache.Exists);
        }

        [Test]
        public void FailedReplacementNeverLeavesPreviousAccountCached()
        {
            var protector = new TestOnlyProtector();
            var cache = new BackendSessionCache("http://127.0.0.1:18081", directory, protector);
            Assert.IsTrue(cache.Save(NewToken(), DateTimeOffset.UtcNow.AddDays(30).ToString("O")));
            protector.FailProtection = true;
            Assert.IsFalse(cache.Save(NewToken(), DateTimeOffset.UtcNow.AddDays(30).ToString("O")));
            Assert.IsFalse(cache.TryLoad(out _));
        }

        [UnityTest]
        public IEnumerator BootProgressCompletesWithoutDestroyingBootstrapOrLeavingCanvas()
        {
            var root = new GameObject("Backend progress lifecycle test");
            var boot = root.AddComponent<InsectSpaceBootstrap>();
            try
            {
                var deadline = Time.realtimeSinceStartup + 30;
                while (!boot.Ready && boot.LastError == null && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.IsTrue(boot.Ready, boot.LastError ?? boot.Stage);
                yield return null; yield return null; yield return null;
                Assert.IsTrue(root != null && boot.Ready);
                Assert.IsNull(root.transform.Find("InsectSpace Bootstrap Canvas"));
                Assert.AreEqual(1f, boot.Progress);
            }
            finally { if (root != null) Object.Destroy(root); }
            yield return null;
        }

        private static string NewToken()
        {
            using (var rng = RandomNumberGenerator.Create())
            { var bytes = new byte[32]; rng.GetBytes(bytes); return BitConverter.ToString(bytes).Replace("-", ""); }
        }
        private sealed class TestOnlyProtector : ISessionCacheProtector
        {
            public string DecodedOverride;
            public bool FailProtection;
            public byte[] Protect(byte[] value) => FailProtection ? throw new CryptographicException("TEST ONLY failure") : value;
            public byte[] Unprotect(byte[] value) => DecodedOverride == null ? value : Encoding.UTF8.GetBytes(DecodedOverride);
        }
    }
}
