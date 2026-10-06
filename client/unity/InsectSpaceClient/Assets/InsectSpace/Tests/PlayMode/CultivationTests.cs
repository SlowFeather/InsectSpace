using System;
using Framework;
using InsectSpace.Cultivation;
using InsectSpace.Gameplay.Cultivation;
using NUnit.Framework;

namespace InsectSpace.Tests
{
    public sealed class CultivationTests
    {
        private static CultivationSnapshot State(long revision = 0, int rank = 0, long player = 1) => new CultivationSnapshot(player, "local-home", revision, 1,
            rank, rank == 0 || rank >= 6 ? MortalStage.None : MortalStage.Initial, rank == 0 ? new Aptitude(AptitudeGrade.None, 0, 0) : new Aptitude(AptitudeGrade.Jia, 89, 44));
        [Test]
        public void AptitudeWeightTableCoversAllRollsWithoutExtremeOrFailedPlayers()
        {
            int[] counts = new int[5];
            for (int i = 0; i < 10000; i++) counts[(int)CultivationRules.DrawAptitude(i, 9, 19).Grade]++;
            CollectionAssert.AreEqual(new[] { 0, 2000, 5000, 2500, 500 }, counts);
            Assert.AreEqual(99, CultivationRules.DrawAptitude(9999, 9, 19).SeaPercent);
            Assert.Throws<ArgumentException>(() => new Aptitude(AptitudeGrade.Yi, 80, 39));
        }
        [Test]
        public void RanksSeparateMortalStagesAndImmortalEnergy()
        {
            Assert.AreEqual("翠绿真元", CultivationRules.EnergyName(State(1, 1)));
            Assert.AreEqual("青提仙元", CultivationRules.EnergyName(State(1, 6)));
            Assert.AreEqual("红枣仙元", CultivationRules.EnergyName(State(1, 7)));
            Assert.AreEqual("白荔仙元", CultivationRules.EnergyName(State(1, 8)));
            Assert.AreEqual("黄杏仙元", CultivationRules.EnergyName(State(1, 9)));
            Assert.IsFalse(CultivationRules.EvidenceSatisfied(State(1, 8)));
            Assert.Throws<ArgumentException>(() => new CultivationSnapshot(1, "local-home", 1, 1, 6, MortalStage.Peak, new Aptitude(AptitudeGrade.Jia, 89, 44)));
        }
        [Test]
        public void ClientWaitsForAuthorityAndKeepsOriginalOperationAcrossReconnect()
        {
            CultivationRequest sent = null; var client = new CultivationClient(1, "local-home", r => sent = r);
            client.TransportConnected(); client.Refresh();
            Assert.IsTrue(client.Receive(new CultivationResponse(sent.RequestId, sent.OperationId, CultivationResult.Ok, false, State())));
            client.Awaken(); string op = sent.OperationId;
            Assert.AreEqual(0, client.Snapshot.Rank); client.Tick(9); client.Disconnect("test"); client.TransportConnected(); client.Refresh();
            Assert.IsFalse(client.Receive(new CultivationResponse(sent.RequestId, sent.OperationId, CultivationResult.Ok, false, State(1, 1, 2))));
            Assert.IsTrue(client.Receive(new CultivationResponse(sent.RequestId, sent.OperationId, CultivationResult.Ok, false, State(1, 1))));
            Assert.Throws<InvalidOperationException>(() => client.Awaken()); client.Retry(); Assert.AreEqual(op, sent.OperationId);
            Assert.IsTrue(client.Receive(new CultivationResponse(sent.RequestId, op, CultivationResult.Ok, true, State(1, 1))));
            Assert.AreEqual(AptitudeGrade.Jia, client.Snapshot.Aptitude.Grade); Assert.IsNull(client.RetryableRequest);
        }
        [Test]
        public void ProtocolRoundtripsAndRejectsTruncationVersionAndClientOverrides()
        {
            var codec = new LocalPlayerPacketCodec(); var packet = CultivationPacket.FromResponse(new CultivationResponse(7, "op-7", CultivationResult.Ok, false, State(2, 8)));
            Assert.IsTrue(codec.Encode(packet, out var bytes)); ReferencePool.Release(packet);
            var decoded = (CultivationPacket)codec.Decode(bytes, 0, bytes.Length, out _);
            Assert.AreEqual(8, decoded.Response.Snapshot.Rank); Assert.AreEqual(89, decoded.Response.Snapshot.Aptitude.SeaPercent); ReferencePool.Release(decoded);
            for (int i = 0; i < bytes.Length; i++) Assert.IsNull(codec.Decode(bytes, 0, i, out _));
            bytes[0] = 2; Assert.IsNull(codec.Decode(bytes, 0, bytes.Length, out _));
            Assert.IsFalse(CultivationPacketCodec.ValidRequest(new CultivationRequest((CultivationCommand)99, 1, "set-grade", 0)));
        }
    }
}
