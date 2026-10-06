using System;
using System.IO;
using System.Linq;
using System.Text;
using Framework;
using Framework.Network;
using InsectSpace.Economy;
using InsectSpace.GuPaths;

namespace InsectSpace.GuWorkshop
{
    public sealed class WorkshopPacket : Packet
    {
        public override int Id => 2005;
        public WorkshopRequest Request { get; private set; }
        public WorkshopResponse Response { get; private set; }
        public override void Clear() { Request = null; Response = null; }
        public static WorkshopPacket FromRequest(WorkshopRequest r) { var p = ReferencePool.Acquire<WorkshopPacket>(); p.Request = r; return p; }
        public static WorkshopPacket FromResponse(WorkshopResponse r) { var p = ReferencePool.Acquire<WorkshopPacket>(); p.Response = r; return p; }
    }
    public sealed class WorkshopPacketCodec : INetworkPacketCodec
    {
        public int MaxPacketSize => 4096;
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        public static bool ValidRequest(WorkshopRequest r)
        {
            if (r == null || r.RequestId <= 0 || !EconomyPacketCodec.ValidId(r.OperationId) || r.Revision < 0 || r.EconomyRevision < 0 ||
                !GuPacketCodec.ValidHash(r.CatalogHash) || r.Instances.Count > 6 || r.Instances.Any(i => i <= 0) || r.Instances.Distinct().Count() != r.Instances.Count) return false;
            switch (r.Command)
            {
                case WorkshopCommand.Snapshot: return r.Target == 0 && r.Instances.Count == 0;
                case WorkshopCommand.Buy: return r.Target > 0 && r.Instances.Count == 0;
                case WorkshopCommand.Feed: return r.Target > 0 && r.Instances.Count == 1;
                case WorkshopCommand.Refine: return r.Target == 0 && r.Instances.Count == 1;
                case WorkshopCommand.Fuse: return r.Target > 0 && r.Instances.Count >= 2;
                case WorkshopCommand.Equip: return r.Target == 0;
                default: return false;
            }
        }
        private static bool ValidResponse(WorkshopResponse r) => r != null && r.RequestId > 0 && EconomyPacketCodec.ValidId(r.OperationId) &&
            r.Result >= WorkshopResult.Ok && r.Result <= WorkshopResult.AlreadyFed && r.Snapshot != null && r.ProducedInstance >= 0 &&
            r.RemovedInstances.Count <= 6 && r.RemovedInstances.All(i => i > 0) && r.RemovedInstances.Distinct().Count() == r.RemovedInstances.Count;
        public bool Encode<T>(T packet, out byte[] payload) where T : Packet
        {
            payload = null;
            if (!(packet is WorkshopPacket p) || (p.Request == null) == (p.Response == null)) return false;
            try
            {
                using (var stream = new MemoryStream()) using (var w = new BinaryWriter(stream, Utf8))
                {
                    w.Write(1); w.Write(2005); w.Write((byte)(p.Request != null ? 1 : 2));
                    if (p.Request != null)
                    {
                        var r = p.Request; if (!ValidRequest(r)) return false;
                        w.Write((byte)r.Command); w.Write(r.RequestId); Text(w, r.OperationId); w.Write(r.Revision);
                        w.Write(r.EconomyRevision); Text(w, r.CatalogHash); w.Write(r.Target); Ids(w, r.Instances.ToArray());
                    }
                    else
                    {
                        var r = p.Response; if (!ValidResponse(r)) return false; var s = r.Snapshot;
                        w.Write(r.RequestId); Text(w, r.OperationId); w.Write((byte)r.Result); w.Write(r.Replayed);
                        w.Write(r.ProducedInstance); Ids(w, r.RemovedInstances.ToArray());
                        w.Write(s.PlayerId); Text(w, s.HomeRealmId); w.Write(s.Revision); w.Write(s.EconomyRevision); w.Write(s.CultivationRevision);
                        Text(w, s.CatalogHash); w.Write((byte)s.Rank); w.Write(s.ServerSeconds); w.Write(s.Wallet.YuanShi); w.Write(s.Wallet.XianYuanShi); w.Write(s.BattleActive);
                        w.Write((byte)s.Inventory.Count);
                        foreach (var g in s.Inventory) { w.Write(g.Id); w.Write(g.DefinitionId); w.Write(g.Refined); w.Write(g.FedUntil); }
                        w.Write((byte)s.Materials.Count);
                        foreach (var m in s.Materials) { w.Write(m.Id); w.Write(m.Count); }
                        Ids(w, s.Loadout.ToArray());
                    }
                    if (stream.Length > MaxPacketSize) return false;
                    payload = stream.ToArray(); return true;
                }
            }
            catch (Exception e) when (e is ArgumentException || e is IOException || e is InvalidDataException) { return false; }
        }
        public Packet Decode(byte[] payload, int offset, int count, out object customErrorData)
        {
            customErrorData = null;
            if (payload == null || offset < 0 || count < 10 || count > MaxPacketSize || offset > payload.Length - count) { customErrorData = "Invalid workshop bounds."; return null; }
            try
            {
                using (var stream = new MemoryStream(payload, offset, count, false)) using (var r = new BinaryReader(stream, Utf8))
                {
                    if (r.ReadInt32() != 1 || r.ReadInt32() != 2005) throw new InvalidDataException();
                    int kind = r.ReadByte(); WorkshopRequest request = null; WorkshopResponse response = null;
                    if (kind == 1)
                    {
                        var command = (WorkshopCommand)r.ReadByte(); long id = r.ReadInt64(); string operation = Text(r);
                        long revision = r.ReadInt64(), economyRevision = r.ReadInt64(); string hash = Text(r); int target = r.ReadInt32();
                        request = new WorkshopRequest(command, id, operation, revision, economyRevision, hash, target, Ids(r));
                        if (!ValidRequest(request)) throw new InvalidDataException();
                    }
                    else if (kind == 2)
                    {
                        long id = r.ReadInt64(); string operation = Text(r); var result = (WorkshopResult)r.ReadByte(); bool replayed = Bool(r);
                        long produced = r.ReadInt64(); var removed = Ids(r); long player = r.ReadInt64(); string home = Text(r);
                        long revision = r.ReadInt64(), economyRevision = r.ReadInt64(), cultivationRevision = r.ReadInt64(); string hash = Text(r);
                        int rank = r.ReadByte(); long seconds = r.ReadInt64(); var wallet = new StoneAmounts(r.ReadInt64(), r.ReadInt64()); bool battle = Bool(r);
                        int n = r.ReadByte(); if (n > WorkshopSnapshot.MaxInstances) throw new InvalidDataException();
                        var items = new GuInstance[n]; for (int i = 0; i < n; i++) items[i] = new GuInstance(r.ReadInt64(), r.ReadInt32(), Bool(r), r.ReadInt64());
                        n = r.ReadByte(); if (n > WorkshopSnapshot.MaxMaterials) throw new InvalidDataException();
                        var stacks = new WorkshopMaterial[n]; for (int i = 0; i < n; i++) stacks[i] = new WorkshopMaterial(r.ReadInt32(), r.ReadInt32());
                        var s = new WorkshopSnapshot(player, home, revision, economyRevision, cultivationRevision, hash, rank, seconds, wallet, battle, items, stacks, Ids(r));
                        response = new WorkshopResponse(id, operation, result, replayed, s, produced, removed);
                        if (!ValidResponse(response)) throw new InvalidDataException();
                    }
                    else throw new InvalidDataException();
                    if (stream.Position != stream.Length) throw new InvalidDataException();
                    return request != null ? WorkshopPacket.FromRequest(request) : WorkshopPacket.FromResponse(response);
                }
            }
            catch (Exception e) when (e is ArgumentException || e is IOException || e is InvalidDataException) { customErrorData = "Malformed or incompatible workshop packet."; return null; }
        }
        private static bool Bool(BinaryReader r) { int b = r.ReadByte(); if (b > 1) throw new InvalidDataException(); return b == 1; }
        private static void Ids(BinaryWriter w, long[] ids) { w.Write((byte)ids.Length); foreach (long id in ids) w.Write(id); }
        private static long[] Ids(BinaryReader r) { int n = r.ReadByte(); if (n > 6) throw new InvalidDataException(); var ids = new long[n]; for (int i = 0; i < n; i++) ids[i] = r.ReadInt64(); return ids; }
        private static void Text(BinaryWriter w, string text) { if (!EconomyPacketCodec.ValidId(text)) throw new InvalidDataException(); var bytes = Utf8.GetBytes(text); w.Write((byte)bytes.Length); w.Write(bytes); }
        private static string Text(BinaryReader r) { int n = r.ReadByte(); if (n > EconomyPacketCodec.MaxTextBytes) throw new InvalidDataException(); var b = r.ReadBytes(n); if (b.Length != n) throw new EndOfStreamException(); string value = Utf8.GetString(b); if (!EconomyPacketCodec.ValidId(value)) throw new InvalidDataException(); return value; }
    }
}
