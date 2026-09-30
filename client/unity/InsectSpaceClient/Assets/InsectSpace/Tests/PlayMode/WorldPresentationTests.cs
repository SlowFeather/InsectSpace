using System.Collections;
using InsectSpace.Client;
using InsectSpace.Contracts;
using InsectSpace.Gameplay.Modules;
using InsectSpace.Foundation;
using InsectSpace.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using YooAsset;
using Object = UnityEngine.Object;

namespace InsectSpace.Tests
{
    public sealed class WorldPresentationTests
    {
        private sealed class Sink : IWorldMovementSink
        {
            public WorldMoveIntent Last;
            public bool Submit(WorldMoveIntent intent) { Last = intent; return true; }
        }

        private static WorldRoute Route(string instance, long epoch) => new WorldRoute
        {
            HomeRealmId = "home", WorldClusterId = "shared", InstanceId = instance,
            SceneId = "foundation", Epoch = epoch
        };

        [UnityTest]
        public IEnumerator RouteChangeClearsViewsAndRejectsOldSnapshots()
        {
            var root = new GameObject("World presentation test");
            var boot = root.AddComponent<InsectSpaceBootstrap>();
            GameObject views = null;
            AssetHandle prefab = null;
            try
            {
                float deadline = Time.realtimeSinceStartup + 30;
                while (!boot.Ready && boot.LastError == null && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.IsTrue(boot.Ready, boot.LastError ?? boot.Stage);
                yield return boot.Context.Resources.PrepareContentPackage("WorldCommon");
                prefab = boot.Context.Resources.LoadAsset<GameObject>("WorldCommon", "WorldActor");
                yield return prefab;
                Assert.AreEqual(EOperationStatus.Succeeded, prefab.Status, prefab.Error);
                var store = new WorldPresenceStore();
                var session = new SessionCoordinator();
                session.Authenticated(11);
                views = new GameObject("Owned world views");
                var presenter = views.AddComponent<WorldActorPresenter>();
                presenter.Bind(store, _ => Object.Instantiate(prefab.GetAssetObject<GameObject>()), Object.Destroy);
                session.EnterWorld(Route("instance-a", 1));
                Assert.IsTrue(store.ApplyForSession(session, "instance-a", 1, 1, new[]
                {
                    new WorldActorSnapshot { ActorId = 11, XMillimeters = 1000 },
                    new WorldActorSnapshot { ActorId = 12, ZMillimeters = 2000 }
                }));
                Assert.AreEqual(2, presenter.VisibleCount);
                store.Bind(session.Route);
                Assert.AreEqual(2, presenter.VisibleCount, "The module tick must not clear the first accepted snapshot.");
                Assert.AreEqual(new Vector3(1, 0, 0), presenter.FindActor(11).position);
                var oldView = presenter.FindActor(12);
                session.EnterWorld(Route("friend-instance", 2));
                store.Bind(session.Route);
                Assert.AreEqual(0, presenter.VisibleCount);
                yield return null;
                Assert.IsTrue(oldView == null);
                Assert.IsFalse(store.ApplyForSession(session, "instance-a", 1, 2, new[] { new WorldActorSnapshot { ActorId = 12 } }));
                Assert.IsTrue(store.ApplyForSession(session, "friend-instance", 2, 1, new[] { new WorldActorSnapshot { ActorId = 13 } }));
                Assert.AreEqual(1, presenter.VisibleCount);
                presenter.Unbind();
                Object.Destroy(views);
                yield return null;
                prefab.Release();
            }
            finally
            {
                if (views != null) Object.DestroyImmediate(views);
                if (prefab != null && prefab.IsValid) prefab.Release();
                Object.Destroy(root);
            }
            yield return null;
        }

        [Test]
        public void MovementSendsVersionedIntentWithoutLocalAuthority()
        {
            var sink = new Sink();
            var emitter = new WorldMovementEmitter(sink);
            Assert.IsFalse(emitter.TryMove(Vector3.one));
            emitter.Bind(Route("friend-instance", 2));
            Assert.IsTrue(emitter.TryMove(new Vector3(1.25f, 0, 2.5f)));
            Assert.AreEqual(1250, sink.Last.XMillimeters);
            Assert.AreEqual(2500, sink.Last.ZMillimeters);
            Assert.AreEqual("friend-instance", sink.Last.InstanceId);
            Assert.AreEqual(2, sink.Last.RouteEpoch);
            Assert.AreEqual(1, sink.Last.Sequence);
            Assert.Throws<System.InvalidOperationException>(() => emitter.Bind(Route("old", 1)));
            Assert.IsFalse(emitter.TryMove(new Vector3(float.NaN, 0, 0)));
            emitter.Suspend();
            Assert.IsFalse(emitter.TryMove(Vector3.zero));
            Assert.Throws<System.InvalidOperationException>(() => emitter.Bind(Route("old", 1)));
        }
    }
}
