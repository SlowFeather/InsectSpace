using System;
using System.Linq;
using Framework;
using InsectSpace.Cultivation;
using InsectSpace.Gameplay.GuPaths;
using InsectSpace.GuPaths;
using NUnit.Framework;
using UnityEngine;

namespace InsectSpace.Tests
{
    public sealed class GuPathTests
    {
        private GuCatalog catalog;
        [SetUp] public void Setup() { catalog = new GuCatalog(name => Resources.Load<TextAsset>("LocalGuPaths/" + name).bytes); }
        private GuSnapshot State(int[] loadout, long revision = 1, long player = 1) => new GuSnapshot(player, "local-home", revision, 1, catalog.Fingerprint, 5, 5,
            catalog.Entries.Select(g => g.Id), loadout, catalog.Evaluate(loadout));
        [Test] public void ResourceCatalogAndComplementaryGuProduceExplainablePaths()
        {
            Assert.AreEqual(43, catalog.Entries.Count); Assert.IsTrue(catalog.Entries.All(g => g.Rank >= 1 && g.Rank <= 5 && !string.IsNullOrEmpty(g.Source)));
            var moon = catalog.Evaluate(new[] { 1001, 1002 }); Assert.AreEqual(PathFormation.Formed, moon.Formation); Assert.AreEqual("月道", catalog.PathName(moon.DominantPath));
            Assert.AreEqual(PathFormation.Inclination, catalog.Evaluate(new[] { 1003, 1004 }).Formation);
            Assert.AreEqual(PathFormation.Mixed, catalog.Evaluate(new[] { 1001, 1008 }).Formation);
            Assert.AreEqual("力道", catalog.PathName(catalog.Evaluate(new[] { 3004, 3005 }).DominantPath));
            Assert.Throws<ArgumentException>(() => catalog.Evaluate(new[] { 1001, 2001 }));
        }
        [Test] public void PacketRoundtripPreservesAuthorityAndRejectsTruncation()
        {
            var codec = new LocalPlayerPacketCodec(); var packet = GuPacket.FromResponse(new GuResponse(9, "gu-9", GuResult.Ok, false, State(new[] { 3009, 3010, 4006 })));
            Assert.IsTrue(codec.Encode(packet, out var bytes)); ReferencePool.Release(packet);
            var decoded = (GuPacket)codec.Decode(bytes, 0, bytes.Length, out _); Assert.AreEqual(PathFormation.Formed, decoded.Response.Snapshot.Profile.Formation);
            CollectionAssert.AreEqual(new[] { 3009, 3010, 4006 }, decoded.Response.Snapshot.Loadout); ReferencePool.Release(decoded);
            for (int i = 0; i < bytes.Length; i++) Assert.IsNull(codec.Decode(bytes, 0, i, out _));
            bytes[0] = 2; Assert.IsNull(codec.Decode(bytes, 0, bytes.Length, out _));
        }
        [Test] public void ClientRejectsForeignIdentityAndRetriesTheOriginalCombination()
        {
            GuRequest sent = null; var c = new GuPathClient(1, "local-home", catalog, r => sent = r); c.TransportConnected(); c.Refresh();
            Assert.IsFalse(c.Receive(new GuResponse(sent.RequestId, sent.OperationId, GuResult.Ok, false, State(Array.Empty<int>(), 1, 2))));
            Assert.IsTrue(c.Receive(new GuResponse(sent.RequestId, sent.OperationId, GuResult.Ok, false, State(Array.Empty<int>()))));
            var ids = new[] { 1001, 1002 }; c.Equip(ids); ids[0] = 5001; string op = sent.OperationId;
            Assert.AreEqual(0, c.Snapshot.Loadout.Count); c.Tick(9); c.Disconnect("test"); c.TransportConnected(); c.Refresh();
            Assert.IsTrue(c.Receive(new GuResponse(sent.RequestId, sent.OperationId, GuResult.Ok, false, State(new[] { 1001, 1002 }, 2))));
            Assert.Throws<InvalidOperationException>(() => c.Equip(new[] { 1008 })); c.Retry(); Assert.AreEqual(op, sent.OperationId);
            CollectionAssert.AreEqual(new[] { 1001, 1002 }, sent.Loadout);
            Assert.IsTrue(c.Receive(new GuResponse(sent.RequestId, op, GuResult.Ok, true, State(new[] { 1001, 1002 }, 2)))); Assert.IsNull(c.RetryableRequest);
        }
        [Test] public void ProtocolCannotCarryClientRewardRankOrPathOverrides()
        {
            Assert.IsFalse(GuPacketCodec.ValidRequest(new GuRequest((GuCommand)3, 1, "grant", 0, catalog.Fingerprint)));
            Assert.IsFalse(GuPacketCodec.ValidRequest(new GuRequest(GuCommand.Snapshot, 1, "snapshot", 0, catalog.Fingerprint, new[] { 1001 })));
            Assert.IsFalse(GuPacketCodec.ValidRequest(new GuRequest(GuCommand.Equip, 1, "oversized", 0, catalog.Fingerprint, Enumerable.Range(1001, 7))));
        }
    }
}
