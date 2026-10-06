using System;
using System.IO;
using System.Linq;
using System.Text;
using Framework;
using Framework.Network;
using InsectSpace.Economy;

namespace InsectSpace.GuPaths
{
    public sealed class GuPacket : Packet
    {
        public override int Id => 2004;
        public GuRequest Request { get; private set; } public GuResponse Response { get; private set; }
        public override void Clear() { Request = null; Response = null; }
        public static GuPacket FromRequest(GuRequest r) { var p = ReferencePool.Acquire<GuPacket>(); p.Request = r; return p; }
        public static GuPacket FromResponse(GuResponse r) { var p = ReferencePool.Acquire<GuPacket>(); p.Response = r; return p; }
    }
    public sealed class GuPacketCodec : INetworkPacketCodec
    {
        public int MaxPacketSize => 4096;
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        public static bool ValidHash(string hash) => hash != null && hash.Length == 64 && hash.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f');
        public static bool ValidRequest(GuRequest r) => r != null && r.RequestId > 0 && EconomyPacketCodec.ValidId(r.OperationId) && r.ExpectedRevision >= 0 && r.RulesVersion > 0 && ValidHash(r.CatalogHash) &&
            (r.Command == GuCommand.Snapshot && r.Loadout.Count == 0 || r.Command == GuCommand.Equip && r.Loadout.Count <= GuCatalog.MaxSlots && r.Loadout.All(id => id > 0));
        public static bool ValidProfile(PathProfile p) => p != null && p.Formation >= PathFormation.Empty && p.Formation <= PathFormation.Mixed && p.DominantPath >= 0 && ((int)p.Roles & ~63) == 0 &&
            p.Scores.Count <= GuCatalog.MaxSlots * 2 && p.Scores.Select(s => s.Path).Distinct().Count() == p.Scores.Count && p.Scores.All(s => s.Path > 0 && s.Score > 0 && s.Score <= 18 && s.GuCount > 0 && s.GuCount <= GuCatalog.MaxSlots && ((int)s.Roles & ~63) == 0);
        public bool Encode<T>(T packet, out byte[] payload) where T : Packet
        {
            payload = null; if (!(packet is GuPacket p) || (p.Request == null) == (p.Response == null)) return false;
            try
            {
                using (var stream = new MemoryStream()) using (var w = new BinaryWriter(stream, Utf8))
                {
                    w.Write(1); w.Write(2004); w.Write((byte)(p.Request != null ? 1 : 2));
                    if (p.Request != null)
                    {
                        var r = p.Request; if (!ValidRequest(r)) return false;
                        w.Write((byte)r.Command); w.Write(r.RequestId); Text(w, r.OperationId); w.Write(r.ExpectedRevision); w.Write(r.RulesVersion); Text(w, r.CatalogHash); Ids(w, r.Loadout.ToArray());
                    }
                    else
                    {
                        var r = p.Response; var s = r.Snapshot;
                        if (r.RequestId <= 0 || !EconomyPacketCodec.ValidId(r.OperationId) || r.Result < GuResult.Ok || r.Result > GuResult.CapacityExceeded || s == null) return false;
                        w.Write(r.RequestId); Text(w, r.OperationId); w.Write((byte)r.Result); w.Write(r.Replayed);
                        w.Write(s.PlayerId); Text(w, s.HomeRealmId); w.Write(s.Revision); w.Write(s.RulesVersion); Text(w, s.CatalogHash);
                        w.Write((byte)s.PlayerRank); w.Write(s.CultivationRevision); Ids(w, s.Owned.ToArray()); Ids(w, s.Loadout.ToArray());
                        w.Write((byte)s.Profile.Formation); w.Write(s.Profile.DominantPath); w.Write((byte)s.Profile.Roles); w.Write((byte)s.Profile.Scores.Count);
                        foreach (var score in s.Profile.Scores) { w.Write(score.Path); w.Write((byte)score.Score); w.Write((byte)score.GuCount); w.Write((byte)score.Roles); }
                    }
                    if (stream.Length > MaxPacketSize) return false; payload = stream.ToArray(); return true;
                }
            }
            catch (Exception e) when (e is ArgumentException || e is IOException || e is InvalidDataException) { return false; }
        }
        public Packet Decode(byte[] payload, int offset, int count, out object customErrorData)
        {
            customErrorData = null;
            if (payload == null || count < 10 || count > MaxPacketSize || offset < 0 || offset > payload.Length - count) { customErrorData = "Invalid Gu packet bounds."; return null; }
            try
            {
                using (var stream = new MemoryStream(payload, offset, count, false)) using (var r = new BinaryReader(stream, Utf8))
                {
                    if (r.ReadInt32() != 1 || r.ReadInt32() != 2004) throw new InvalidDataException();
                    byte kind = r.ReadByte(); GuRequest request = null; GuResponse response = null;
                    if (kind == 1)
                    {
                        var command = (GuCommand)r.ReadByte(); long id = r.ReadInt64(); string op = Text(r); long revision = r.ReadInt64(); int version = r.ReadInt32(); string hash = Text(r);
                        request = new GuRequest(command, id, op, revision, hash, Ids(r, GuCatalog.MaxSlots), version);
                        if (!ValidRequest(request)) throw new InvalidDataException();
                    }
                    else if (kind == 2)
                    {
                        long id = r.ReadInt64(); string op = Text(r); var result = (GuResult)r.ReadByte(); byte replay = r.ReadByte();
                        long player = r.ReadInt64(); string home = Text(r); long revision = r.ReadInt64(); int version = r.ReadInt32(); string hash = Text(r);
                        int rank = r.ReadByte(); long cultivationRevision = r.ReadInt64(); var owned = Ids(r, GuCatalog.MaxOwned); var slots = Ids(r, GuCatalog.MaxSlots);
                        var formation = (PathFormation)r.ReadByte(); int dominant = r.ReadInt32(); var roles = (GuRole)r.ReadByte(); int n = r.ReadByte();
                        if (n > GuCatalog.MaxSlots * 2) throw new InvalidDataException();
                        var scores = new PathScore[n]; for (int i = 0; i < n; i++) scores[i] = new PathScore(r.ReadInt32(), r.ReadByte(), r.ReadByte(), (GuRole)r.ReadByte());
                        var s = new GuSnapshot(player, home, revision, version, hash, rank, cultivationRevision, owned, slots, new PathProfile(formation, dominant, roles, scores));
                        if (id <= 0 || !EconomyPacketCodec.ValidId(op) || result < GuResult.Ok || result > GuResult.CapacityExceeded || replay > 1) throw new InvalidDataException();
                        response = new GuResponse(id, op, result, replay == 1, s);
                    }
                    else throw new InvalidDataException();
                    if (stream.Position != stream.Length) throw new InvalidDataException();
                    return request != null ? GuPacket.FromRequest(request) : GuPacket.FromResponse(response);
                }
            }
            catch (Exception e) when (e is ArgumentException || e is IOException || e is InvalidDataException) { customErrorData = "Malformed or incompatible Gu packet."; return null; }
        }
        private static void Ids(BinaryWriter w, int[] ids) { w.Write((ushort)ids.Length); foreach (int id in ids) w.Write(id); }
        private static int[] Ids(BinaryReader r, int max) { int n = r.ReadUInt16(); if (n > max) throw new InvalidDataException(); var ids = new int[n]; for (int i = 0; i < n; i++) ids[i] = r.ReadInt32(); return ids; }
        private static void Text(BinaryWriter w, string text) { if (!EconomyPacketCodec.ValidId(text)) throw new InvalidDataException(); var bytes = Utf8.GetBytes(text); w.Write((byte)bytes.Length); w.Write(bytes); }
        private static string Text(BinaryReader r) { int n = r.ReadByte(); if (n > EconomyPacketCodec.MaxTextBytes) throw new InvalidDataException(); var bytes = r.ReadBytes(n); if (bytes.Length != n) throw new EndOfStreamException(); string text = Utf8.GetString(bytes); if (!EconomyPacketCodec.ValidId(text)) throw new InvalidDataException(); return text; }
    }
}
