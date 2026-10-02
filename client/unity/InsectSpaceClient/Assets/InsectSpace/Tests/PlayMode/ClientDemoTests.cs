using System;
using System.Collections;
using System.Linq;
using System.Net;
using Framework;
using Framework.Network;
using InsectSpace.Client;
using InsectSpace.Contracts;
using InsectSpace.Gameplay.Demo;
using InsectSpace.Network;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace InsectSpace.Tests
{
    public sealed class ClientDemoTests
    {
        [Test]
        public void LessonModulesSortAndRollbackIncludingFailedModule()
        {
            var trace = DemoModuleLesson.Run(false);
            CollectionAssert.AreEqual(new[] { "Start config", "Start data", "Start view" }, trace.Take(3));
            CollectionAssert.AreEqual(new[] { "Stop view", "Stop data", "Stop config" }, trace.Skip(trace.Length - 3));
            var failure = DemoModuleLesson.Run(true);
            CollectionAssert.AreEqual(new[] { "Stop view", "Stop data", "Stop config" }, failure.Skip(3).Take(3));
            Assert.That(failure.Last(), Does.Contain("frozen"));
        }

        [Test]
        public void PlayerRejectsOldRevisionAndCopiesInputAndOutput()
        {
            var model = new LocalPlayerLesson();
            var source = LocalPlayerLesson.Fixture(2);
            Assert.IsTrue(model.Apply(source));
            source.DisplayName = "mutated";
            var returned = model.Player; returned.DisplayName = "mutated again";
            Assert.AreEqual("本地学员", model.Player.DisplayName);
            Assert.IsFalse(model.Apply(LocalPlayerLesson.Fixture(1)));
            var wrongPlayer = LocalPlayerLesson.Fixture(3); wrongPlayer.PlayerId++;
            Assert.IsFalse(model.Apply(wrongPlayer));
            model.PreviewLoadout(101, 201);
            Assert.AreEqual(2, model.Player.Revision);
        }

        [Test]
        public void QuestRequiresOrderedConditionsAndIdempotentReceipt()
        {
            var quest = new LocalQuestLesson();
            Assert.IsFalse(quest.Submit());
            Assert.IsFalse(quest.ApplyFixtureReceipt());
            Assert.IsFalse(quest.Advance(DemoQuestStage.FinishPractice));
            Assert.IsTrue(quest.Advance(DemoQuestStage.MeetGuide));
            Assert.IsTrue(quest.Advance(DemoQuestStage.VisitGarden));
            Assert.IsTrue(quest.Advance(DemoQuestStage.FinishPractice));
            Assert.IsTrue(quest.Submit());
            Assert.AreEqual(0, quest.DemonstrationReceipts);
            Assert.IsTrue(quest.ApplyFixtureReceipt());
            Assert.IsFalse(quest.ApplyFixtureReceipt());
            Assert.AreEqual(1, quest.DemonstrationReceipts);
            quest.Reset(); Assert.AreEqual(DemoQuestStage.MeetGuide, quest.Stage);
        }

        [Test]
        public void WorldIntentWaitsForSnapshotAndRouteInvalidatesOldTraffic()
        {
            var world = new LocalWorldLesson(); world.Spawn();
            int originalX = world.Presence.Actors.First(a => a.ActorId == 1001).XMillimeters;
            Assert.IsTrue(world.RequestMove());
            Assert.AreEqual(originalX, world.Presence.Actors.First(a => a.ActorId == 1001).XMillimeters);
            int targetX = world.Pending.XMillimeters;
            Assert.IsTrue(world.ConfirmMove());
            Assert.AreEqual(targetX, world.Presence.Actors.First(a => a.ActorId == 1001).XMillimeters);
            string home = world.Session.Route.HomeRealmId;
            world.SwitchRoute();
            Assert.AreEqual(home, world.Session.Route.HomeRealmId);
            Assert.AreEqual(0, world.Presence.Actors.Count);
            Assert.IsFalse(world.TryStaleSnapshot());
            world.SuspendMovement(); Assert.IsFalse(world.RequestMove());
            world.Clear();
        }

        [Test]
        public void BattleWaitsForMissingFrameAndMatchesReversedCommandDelivery()
        {
            using (var local = new DemoBattleLab())
            using (var ordered = new DemoBattleLab())
            {
                ordered.Reset(true);
                Assert.IsTrue(ordered.Push(1));
                var before = ordered.Hash;
                Assert.IsFalse(ordered.Step());
                Assert.AreEqual(-1, ordered.Frame); Assert.AreEqual(before, ordered.Hash);
                Assert.IsTrue(ordered.Push(0)); Assert.IsFalse(ordered.Push(0));
                for (int i = 0; i < 2; i++)
                {
                    Assert.IsTrue(local.Step()); Assert.IsTrue(ordered.Step());
                    Assert.AreEqual(local.Hash, ordered.Hash);
                }
                Assert.IsFalse(ordered.Push(0));
                Assert.IsTrue(ordered.VerifyReplay(out int verified)); Assert.AreEqual(2, verified);
            }
        }

        [Test]
        public void BattleHasStatefulDamageAndReplaysEveryFrameAfterReset()
        {
            using (var lab = new DemoBattleLab())
            {
                ulong initial = lab.Hash;
                int frames = 0;
                while (lab.Step() && frames < 100) frames++;
                Assert.Less(frames, 100); Assert.Greater(frames, 1);
                Assert.IsTrue(lab.Finished); Assert.AreNotEqual(initial, lab.Hash);
                Assert.IsTrue(lab.VerifyReplay(out int count)); Assert.AreEqual(frames, count);
                lab.Reset(false);
                Assert.AreEqual(initial, lab.Hash); Assert.AreEqual(100, lab.PlayerHp);
            }
        }

        [UnityTest]
        public IEnumerator HubBootsAllLessonsAndCleansRealResources()
        {
            var cameraRoot = new GameObject("Demo test camera"); cameraRoot.tag = "MainCamera";
            cameraRoot.AddComponent<Camera>().orthographic = true;
            var root = new GameObject("Demo hub test");
            var hub = root.AddComponent<ClientDemoHub>();
            try
            {
                float deadline = Time.realtimeSinceStartup + 45;
                while (!hub.Ready && hub.Error == null && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.IsTrue(hub.Ready, hub.Error);
                Assert.AreEqual(2, hub.VisibleActors);
                for (int i = 0; i < ClientDemoHub.LessonCount; i++) { hub.SelectLesson(i); yield return null; }
                hub.SelectLesson(3);
                hub.RunResourceOperation(FailingResourceOperation());
                Assert.IsTrue(hub.Busy); Assert.AreEqual(1, hub.CompletedCount);
                while (hub.Busy) yield return null;
                Assert.AreEqual(1, hub.CompletedCount, "Failed operation must not mark the lesson as experienced.");
                StringAssert.Contains("操作失败", hub.LastResult);
                for (int i = 0; i < 3; i++)
                {
                    hub.RunResourceOperation(hub.LoadResource());
                    hub.SelectLesson(6); // Completion belongs to the resource lesson even after navigation.
                    while (hub.Busy) yield return null;
                    Assert.IsTrue(hub.ResourceLoaded, hub.LastResult); Assert.AreEqual(2, hub.CompletedCount);
                    yield return hub.LoadResource(); Assert.IsTrue(hub.ResourceLoaded);
                    yield return hub.ReleaseResource(); Assert.IsFalse(hub.ResourceLoaded);
                    yield return hub.LoadAdditiveScene(); Assert.IsTrue(hub.AdditiveSceneLoaded);
                    yield return hub.LoadAdditiveScene(); Assert.IsTrue(hub.AdditiveSceneLoaded);
                    yield return hub.UnloadAdditiveScene(); Assert.IsFalse(hub.AdditiveSceneLoaded);
                }
                hub.World.SwitchRoute(); Assert.AreEqual(0, hub.VisibleActors);
                hub.World.Spawn(); Assert.AreEqual(2, hub.VisibleActors);
                for (int i = 0; i < 3; i++) { hub.ApplyQuality(i); Assert.AreEqual(i, QualitySettings.GetQualityLevel()); }
                yield return hub.LoadResource(); yield return hub.LoadAdditiveScene();
                hub.Battle.Step(); hub.Quest.Advance(DemoQuestStage.MeetGuide);
                yield return hub.ResetLessons();
                Assert.IsTrue(hub.Ready); Assert.AreEqual(1, hub.CompletedCount);
                Assert.AreEqual(0, hub.SelectedLesson); Assert.AreEqual(-1, hub.Battle.Frame);
                Assert.AreEqual(DemoQuestStage.MeetGuide, hub.Quest.Stage);
                Assert.AreEqual(2, hub.VisibleActors);
                Assert.IsFalse(hub.ResourceLoaded); Assert.IsFalse(hub.AdditiveSceneLoaded);
                yield return hub.LoadResource(); yield return hub.LoadAdditiveScene();
                yield return hub.Shutdown();
                Assert.IsTrue(hub.Stopped); Assert.IsFalse(hub.Ready);
                Assert.IsFalse(hub.ResourceLoaded); Assert.IsFalse(hub.AdditiveSceneLoaded);
                Assert.AreEqual(0, hub.VisibleActors);
            }
            finally { Object.Destroy(root); Object.Destroy(cameraRoot); }
            yield return null; yield return null;
            Assert.IsNull(Object.FindObjectOfType<ClientDemoHub>());
            Assert.IsNull(Object.FindObjectOfType<InsectSpaceBootstrap>());
        }

        private static IEnumerator FailingResourceOperation()
        {
            yield return null;
            throw new InvalidOperationException("Expected demo failure fixture");
        }

        [UnityTest]
        public IEnumerator NetworkProbeUsesRealTcpWithoutRegisteringTransportsOrAuthenticating()
        {
            var root = new GameObject("Demo network test Bootstrap");
            var boot = root.AddComponent<InsectSpaceBootstrap>();
            var probe = new LocalNetworkProbe(_ => { });
            var server = new TcpServerChannelProvider().CreateChannel("demo-loopback-test", new FoundationPacketCodec());
            try
            {
                float deadline = Time.realtimeSinceStartup + 45;
                while (!boot.Ready && boot.LastError == null && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.IsTrue(boot.Ready, boot.LastError);
                var manager = GameFrameworkEntry.GetModule<INetworkManager>();
                int baseline = manager.GetAllNetworkChannels().Length;
                server.PacketReceived += (peer, packet) =>
                {
                    if (packet is FoundationPacket p && p.Message == "foundation.hello")
                        peer.Send(FoundationPacket.Create("foundation.ready"));
                };
                server.Start(IPAddress.Loopback, 0);
                int port = ((IPEndPoint)server.LocalEndPoint).Port;
                for (int i = 0; i < 2; i++)
                {
                    probe.Connect(port);
                    deadline = Time.realtimeSinceStartup + 8;
                    while (probe.Busy && Time.realtimeSinceStartup < deadline)
                    {
                        server.Update(Time.unscaledDeltaTime, Time.unscaledDeltaTime);
                        probe.Tick(Time.unscaledDeltaTime);
                        yield return null;
                    }
                    Assert.IsTrue(probe.Received, probe.Status); Assert.IsFalse(probe.Busy);
                    Assert.AreEqual(SessionPhase.SignedOut, probe.Session.Phase);
                    Assert.AreEqual(baseline, manager.GetAllNetworkChannels().Length);
                }
                server.Dispose(); server = null;
                probe.Connect(port);
                deadline = Time.realtimeSinceStartup + 8;
                while (probe.Busy && Time.realtimeSinceStartup < deadline)
                { probe.Tick(Time.unscaledDeltaTime); yield return null; }
                Assert.IsFalse(probe.Busy); Assert.IsFalse(probe.Received);
                StringAssert.Contains("失败", probe.Status);
                Assert.AreEqual(SessionPhase.SignedOut, probe.Session.Phase);
                Assert.AreEqual(baseline, manager.GetAllNetworkChannels().Length);
                Assert.IsTrue(boot.Ready); Assert.IsFalse(boot.Context.Connections.LobbyAuthenticated);
                Assert.AreEqual(SessionPhase.World, boot.Context.Session.Phase);
            }
            finally { probe.Dispose(); server?.Dispose(); Object.Destroy(root); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator DestroyingLoadedHubReleasesSceneBeforeBootstrapAndCanRestart()
        {
            int quality = QualitySettings.GetQualityLevel();
            int fps = Application.targetFrameRate, vsync = QualitySettings.vSyncCount;
            var cameraRoot = new GameObject("Demo lifetime test camera"); cameraRoot.tag = "MainCamera";
            var camera = cameraRoot.AddComponent<Camera>(); camera.orthographicSize = 9;
            var existingTarget = new RenderTexture(64, 64, 16); camera.targetTexture = existingTarget;
            GameObject root = null;
            try
            {
                for (int iteration = 0; iteration < 2; iteration++)
                {
                    root = new GameObject("Demo lifetime test");
                    var hub = root.AddComponent<ClientDemoHub>();
                    float deadline = Time.realtimeSinceStartup + 45;
                    while (!hub.Ready && hub.Error == null && Time.realtimeSinceStartup < deadline) yield return null;
                    Assert.IsTrue(hub.Ready, hub.Error);
                    yield return hub.LoadResource(); yield return hub.LoadAdditiveScene();
                    hub.ApplyQuality(2);
                    Object.Destroy(root); root = null;
                    yield return null;
                    deadline = Time.realtimeSinceStartup + 10;
                    while ((Object.FindObjectOfType<ClientDemoCleanup>() != null || Object.FindObjectOfType<InsectSpaceBootstrap>() != null)
                           && Time.realtimeSinceStartup < deadline) yield return null;
                    Assert.IsNull(Object.FindObjectOfType<ClientDemoCleanup>());
                    Assert.IsNull(Object.FindObjectOfType<InsectSpaceBootstrap>());
                    Assert.IsNull(GameObject.Find("YooAsset WorldActor - handle retained"));
                    Assert.AreSame(existingTarget, camera.targetTexture);
                    Assert.AreEqual(9, camera.orthographicSize);
                    Assert.AreEqual(quality, QualitySettings.GetQualityLevel());
                    Assert.AreEqual(fps, Application.targetFrameRate); Assert.AreEqual(vsync, QualitySettings.vSyncCount);
                }
            }
            finally
            {
                if (root != null) Object.Destroy(root);
                Object.Destroy(cameraRoot); existingTarget.Release(); Object.Destroy(existingTarget);
            }
            yield return null;
        }
    }
}
