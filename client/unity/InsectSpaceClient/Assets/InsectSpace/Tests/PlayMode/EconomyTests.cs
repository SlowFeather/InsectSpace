using System;
using System.Collections;
using System.Net;
using Framework;
using Framework.Network;
using InsectSpace.Economy;
using InsectSpace.BattleEconomy;
using InsectSpace.Gameplay.Economy;
using InsectSpace.Client;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace InsectSpace.Tests
{
    public sealed class EconomyTests
    {
        private static EconomySnapshot Snapshot(long revision = 2, long player = 1, string home = "local-home") =>
            new EconomySnapshot(player, home, revision, new StoneAmounts(100, 10), default, "", ReservePhase.None, "", 100, 100);
        [Test]
        public void ClientAcceptsOnlyCurrentIdentityAndCorrelatedResponses()
        {
            EconomyRequest request = null; var client = new EconomyClient(1, "local-home", r => request = r);
            Assert.Throws<InvalidOperationException>(() => client.Prepare(new StoneAmounts(1, 0)));
            client.TransportConnected(); client.Refresh();
            Assert.IsFalse(client.Receive(new EconomyResponse(request.RequestId + 1, request.OperationId, EconomyResult.Ok, false, Snapshot())));
            Assert.IsFalse(client.Receive(new EconomyResponse(request.RequestId, request.OperationId, EconomyResult.Ok, false, Snapshot(player: 2))));
            Assert.IsFalse(client.Receive(new EconomyResponse(request.RequestId, request.OperationId, EconomyResult.Ok, false, Snapshot(home: "other-home"))));
            Assert.IsTrue(client.Receive(new EconomyResponse(request.RequestId, request.OperationId, EconomyResult.Ok, false, Snapshot())));
            client.Prepare(new StoneAmounts(20, 2)); Assert.AreEqual(100, client.Snapshot.Wallet.YuanShi);
            Assert.IsFalse(client.Receive(new EconomyResponse(request.RequestId, request.OperationId, EconomyResult.Ok, false, Snapshot(1))));
            Assert.IsTrue(client.Receive(new EconomyResponse(request.RequestId, request.OperationId, EconomyResult.InsufficientFunds, false, Snapshot())));
            Assert.AreEqual(100, client.Snapshot.Wallet.YuanShi); Assert.AreEqual(EconomyResult.InsufficientFunds, client.LastResponse.Result);
        }
        [Test]
        public void UnknownOutcomePreservesOperationIdAcrossReconnect()
        {
            EconomyRequest request = null; var client = new EconomyClient(1, "local-home", r => request = r);
            client.TransportConnected(); client.Refresh();
            client.Receive(new EconomyResponse(request.RequestId, request.OperationId, EconomyResult.Ok, false, Snapshot()));
            client.Prepare(new StoneAmounts(20, 2)); string operation = request.OperationId; long sequence = request.RequestId;
            client.Tick(9); Assert.IsNotNull(client.RetryableRequest);
            Assert.Throws<InvalidOperationException>(() => client.WorldAction("local-world-skill", 1));
            client.Disconnect("test"); Assert.IsNull(client.Snapshot);
            client.TransportConnected(); client.Refresh();
            client.Receive(new EconomyResponse(request.RequestId, request.OperationId, EconomyResult.Ok, false, Snapshot(3)));
            client.Retry(); Assert.AreEqual(operation, request.OperationId); Assert.Greater(request.RequestId, sequence);
            Assert.IsTrue(client.Receive(new EconomyResponse(request.RequestId, request.OperationId, EconomyResult.Ok, true, Snapshot(3))));
            Assert.IsNull(client.RetryableRequest); Assert.IsTrue(client.LastResponse.Replayed);
        }
        [Test]
        public void ProtocolRejectsBadVersionTruncationOversizeAndClientGrants()
        {
            var codec = new EconomyPacketCodec(); var packet = EconomyPacket.FromRequest(new EconomyRequest(EconomyCommand.PrepareReserve, 1, "op-1", 0, new StoneAmounts(3, 2)));
            try
            {
                Assert.IsTrue(codec.Encode(packet, out var bytes));
                for (int i = 0; i < bytes.Length; i++) Assert.IsNull(codec.Decode(bytes, 0, i, out _));
                bytes[0] = 99; Assert.IsNull(codec.Decode(bytes, 0, bytes.Length, out _));
                Assert.IsNull(codec.Decode(new byte[1025], 0, 1025, out _));
                Assert.IsFalse(EconomyPacketCodec.ValidRequest(new EconomyRequest((EconomyCommand)100, 2, "grant", 0, new StoneAmounts(1, 1))));
                Assert.IsFalse(EconomyPacketCodec.ValidRequest(new EconomyRequest(EconomyCommand.WorldAction, 3, "forged-price", 0, new StoneAmounts(1, 0), "cast", 1)));
            }
            finally { ReferencePool.Release(packet); }
        }
        [Test]
        public void OrdinaryFramesConsumeEssenceAndExplicitRecoveryConsumesOnlyReserve()
        {
            var rules = new BattleResourceRules(30, 10, 50);
            var state = new BattleResources(1, 100, 100, new StoneAmounts(2, 1), rules);
            Assert.IsTrue(state.ApplyFrame(0, new[] { new ResourceCommand(1, 0, ResourceAction.CastSkill) }));
            Assert.AreEqual(70, state.Essence); Assert.AreEqual(new StoneAmounts(2, 1), state.Remaining);
            Assert.IsTrue(state.ApplyFrame(1, new[] { new ResourceCommand(1, 2, ResourceAction.UseXianYuanShiReserve), new ResourceCommand(1, 1, ResourceAction.UseYuanShiReserve) }));
            Assert.AreEqual(100, state.Essence); Assert.AreEqual(new StoneAmounts(1, 0), state.Remaining);
            Assert.IsTrue(state.ApplyFrame(2, new[] { new ResourceCommand(1, 3, ResourceAction.UseYuanShiReserve) }));
            Assert.AreEqual(new StoneAmounts(1, 0), state.Remaining, "No stone consumed when essence is already full.");
        }
        [Test]
        public void FramesAreDeterministicAndMalformedBatchIsAtomic()
        {
            var rules = new BattleResourceRules(30, 10, 50);
            var a = new BattleResources(1, 100, 100, new StoneAmounts(2, 1), rules);
            var b = new BattleResources(1, 100, 100, new StoneAmounts(2, 1), rules);
            var cast = new ResourceCommand(1, 0, ResourceAction.CastSkill); var recover = new ResourceCommand(1, 1, ResourceAction.UseYuanShiReserve);
            Assert.IsTrue(a.ApplyFrame(0, new[] { recover, cast })); Assert.IsTrue(b.ApplyFrame(0, new[] { cast, recover }));
            Assert.AreEqual(a.StateHash, b.StateHash); ulong hash = a.StateHash;
            Assert.IsFalse(a.ApplyFrame(2, Array.Empty<ResourceCommand>()));
            Assert.IsFalse(a.ApplyFrame(1, new[] { new ResourceCommand(1, 2, ResourceAction.CastSkill), new ResourceCommand(99, 3, ResourceAction.CastSkill) }));
            Assert.AreEqual(hash, a.StateHash);
            var empty = new BattleResources(1, 20, 100, default, rules);
            Assert.IsTrue(empty.ApplyFrame(0, new[] { cast, recover })); Assert.AreEqual(20, empty.Essence); Assert.IsTrue(empty.Remaining.IsEmpty);
        }
        [Test]
        public void KcpResourceReplicaWaitsForMissingFramesAndVerifiesHash()
        {
            var state = new BattleResources(1, 100, 100, new StoneAmounts(2, 1), new BattleResourceRules(30, 10, 50));
            var replica = new BattleResourceReplica("local-room", state.Capture());
            state.ApplyFrame(0, new[] { new ResourceCommand(1, 0, ResourceAction.CastSkill) });
            var first = new BattleResourceMessage(BattleMessageKind.Frame, roomId: "local-room", sequence: 0, action: ResourceAction.CastSkill, state: state.Capture());
            state.ApplyFrame(1, new[] { new ResourceCommand(1, 1, ResourceAction.UseYuanShiReserve) });
            var second = new BattleResourceMessage(BattleMessageKind.Frame, roomId: "local-room", sequence: 1, action: ResourceAction.UseYuanShiReserve, state: state.Capture());
            Assert.IsTrue(replica.Receive(second)); Assert.AreEqual(-1, replica.Snapshot.Frame);
            Assert.AreEqual(100, replica.Snapshot.Essence); Assert.AreEqual(1, replica.BufferedFrames);
            Assert.IsTrue(replica.Receive(first)); Assert.AreEqual(state.StateHash, replica.Snapshot.Hash);
            Assert.AreEqual(80, replica.Snapshot.Essence); Assert.AreEqual(new StoneAmounts(1, 1), replica.Snapshot.Remaining);
            Assert.IsTrue(replica.Receive(first)); Assert.AreEqual(state.StateHash, replica.Snapshot.Hash);
            var source = BattleResourcePacket.Create(second); var codec = new BattleResourceCodec();
            Assert.IsTrue(codec.Encode(source, out byte[] bytes)); ReferencePool.Release(source);
            for (int i = 0; i < bytes.Length; i++) Assert.IsNull(codec.Decode(bytes, 0, i, out _));
            bytes[bytes.Length - 1] ^= 1; Assert.IsNull(codec.Decode(bytes, 0, bytes.Length, out _));
        }
        [Test]
        public void SnapshotRejectsInvalidEscrowOrNegativeBalances()
        {
            Assert.Throws<ArgumentException>(() => new EconomySnapshot(1, "local-home", 0, new StoneAmounts(-1, 0), default, "", ReservePhase.None, "", 0, 10));
            Assert.Throws<ArgumentException>(() => new EconomySnapshot(1, "local-home", 0, default, new StoneAmounts(1, 0), "", ReservePhase.None, "", 0, 10));
            Assert.Throws<ArgumentException>(() => new EconomySnapshot(1, "local-home", 0, default, default, "id", ReservePhase.InBattle, "", 0, 10));
        }
        [UnityTest]
        public IEnumerator LocalTransportPerformsActualTcpRoundtripAndReleasesChannel()
        {
            var root = new GameObject("Economy transport test bootstrap");
            var boot = root.AddComponent<InsectSpaceBootstrap>();
            var server = new TcpServerChannelProvider().CreateChannel("economy-unity-test", new EconomyPacketCodec());
            LocalEconomyTcpClient tcp = null;
            try
            {
                float deadline = Time.realtimeSinceStartup + 45;
                while (!boot.Ready && boot.LastError == null && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.IsTrue(boot.Ready, boot.LastError);
                var manager = GameFrameworkEntry.GetModule<INetworkManager>(); int baseline = manager.GetAllNetworkChannels().Length;
                server.PacketReceived += (peer, packet) =>
                {
                    if (packet is EconomyPacket p && p.Request != null)
                        peer.Send(EconomyPacket.FromResponse(new EconomyResponse(p.Request.RequestId, p.Request.OperationId, EconomyResult.Ok, false, Snapshot())));
                };
                server.Start(IPAddress.Loopback, 0); int port = ((IPEndPoint)server.LocalEndPoint).Port;
                tcp = new LocalEconomyTcpClient(manager); tcp.Connect(port); deadline = Time.realtimeSinceStartup + 10;
                while (!tcp.Client.Ready && Time.realtimeSinceStartup < deadline)
                { server.Update(Time.unscaledDeltaTime, Time.unscaledDeltaTime); tcp.Tick(Time.unscaledDeltaTime); yield return null; }
                Assert.IsTrue(tcp.Client.Ready, tcp.Client.Status); Assert.AreEqual(100, tcp.Client.Snapshot.Wallet.YuanShi);
                server.Dispose(); server = null; deadline = Time.realtimeSinceStartup + 10;
                while (tcp.Active && Time.realtimeSinceStartup < deadline) { tcp.Tick(Time.unscaledDeltaTime); yield return null; }
                Assert.IsFalse(tcp.Active); Assert.IsNull(tcp.Client.Snapshot);
                Assert.AreEqual(baseline, manager.GetAllNetworkChannels().Length);
            }
            finally { tcp?.Dispose(); server?.Dispose(); Object.Destroy(root); }
            yield return null;
        }
    }
}
