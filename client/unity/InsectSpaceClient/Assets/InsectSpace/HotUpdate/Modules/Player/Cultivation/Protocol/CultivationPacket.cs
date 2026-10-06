using System;
using System.IO;
using System.Text;
using Framework;
using Framework.Network;
using InsectSpace.Economy;
using InsectSpace.GuPaths;

namespace InsectSpace.Cultivation
{
    public sealed class CultivationPacket : Packet
    {
        public override int Id => 2003;
        public CultivationRequest Request { get; private set; }
        public CultivationResponse Response { get; private set; }
        public override void Clear() { Request = null; Response = null; }
        public static CultivationPacket FromRequest(CultivationRequest request)
        { var p = ReferencePool.Acquire<CultivationPacket>(); p.Request = request; return p; }
        public static CultivationPacket FromResponse(CultivationResponse response)
        { var p = ReferencePool.Acquire<CultivationPacket>(); p.Response = response; return p; }
    }
    public sealed class CultivationPacketCodec : INetworkPacketCodec
    {
        public int MaxPacketSize => 1024;
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        public static bool ValidRequest(CultivationRequest r) => r != null && r.Command >= CultivationCommand.Snapshot && r.Command <= CultivationCommand.Breakthrough &&
            r.RequestId > 0 && EconomyPacketCodec.ValidId(r.OperationId) && r.ExpectedRevision >= 0 && r.RulesVersion > 0;
        public bool Encode<T>(T packet, out byte[] payload) where T : Packet
        {
            payload = null;
            if (!(packet is CultivationPacket p) || (p.Request == null) == (p.Response == null)) return false;
            try
            {
                using (var stream = new MemoryStream())
                using (var w = new BinaryWriter(stream, Utf8))
                {
                    w.Write(1); w.Write(p.Id); w.Write((byte)(p.Request != null ? 1 : 2));
                    if (p.Request != null)
                    {
                        var r = p.Request; if (!ValidRequest(r)) return false;
                        w.Write((byte)r.Command); w.Write(r.RequestId); Text(w, r.OperationId); w.Write(r.ExpectedRevision); w.Write(r.RulesVersion);
                    }
                    else
                    {
                        var r = p.Response; var s = r.Snapshot;
                        if (r.RequestId <= 0 || !EconomyPacketCodec.ValidId(r.OperationId) || s == null || r.Result < CultivationResult.Ok || r.Result > CultivationResult.CapacityExceeded) return false;
                        w.Write(r.RequestId); Text(w, r.OperationId); w.Write((byte)r.Result); w.Write(r.Replayed);
                        w.Write(s.PlayerId); Text(w, s.HomeRealmId); w.Write(s.Revision); w.Write(s.RulesVersion);
                        w.Write((byte)s.Rank); w.Write((byte)s.Stage); w.Write((byte)s.Aptitude.Grade); w.Write((byte)s.Aptitude.SeaPercent); w.Write((byte)s.Aptitude.StopStep);
                        w.Write(s.Experience); w.Write(s.MainPathDaoMarks); w.Write(s.HeavenlyTribulations); w.Write(s.GrandTribulations); w.Write(s.MyriadTribulations); w.Write((byte)s.Proofs);
                    }
                    if (stream.Length > MaxPacketSize) return false;
                    payload = stream.ToArray(); return true;
                }
            }
            catch (Exception e) when (e is IOException || e is ArgumentException || e is InvalidDataException) { return false; }
        }
        public Packet Decode(byte[] payload, int offset, int count, out object customErrorData)
        {
            customErrorData = null;
            if (payload == null || offset < 0 || count < 10 || count > MaxPacketSize || offset > payload.Length - count)
            { customErrorData = "Invalid cultivation packet bounds."; return null; }
            try
            {
                using (var stream = new MemoryStream(payload, offset, count, false))
                using (var r = new BinaryReader(stream, Utf8))
                {
                    if (r.ReadInt32() != 1 || r.ReadInt32() != 2003) throw new InvalidDataException();
                    byte kind = r.ReadByte(); CultivationRequest request = null; CultivationResponse response = null;
                    if (kind == 1)
                    {
                        request = new CultivationRequest((CultivationCommand)r.ReadByte(), r.ReadInt64(), Text(r), r.ReadInt64(), r.ReadInt32());
                        if (!ValidRequest(request)) throw new InvalidDataException();
                    }
                    else if (kind == 2)
                    {
                        long id = r.ReadInt64(); string op = Text(r); var result = (CultivationResult)r.ReadByte(); byte replay = r.ReadByte();
                        long player = r.ReadInt64(); string home = Text(r); long revision = r.ReadInt64(); int version = r.ReadInt32();
                        int rank = r.ReadByte(); var stage = (MortalStage)r.ReadByte();
                        var aptitude = new Aptitude((AptitudeGrade)r.ReadByte(), r.ReadByte(), r.ReadByte());
                        var s = new CultivationSnapshot(player, home, revision, version, rank, stage, aptitude, r.ReadInt64(), r.ReadInt64(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), (CultivationProof)r.ReadByte());
                        if (id <= 0 || !EconomyPacketCodec.ValidId(op) || result < CultivationResult.Ok || result > CultivationResult.CapacityExceeded || replay > 1) throw new InvalidDataException();
                        response = new CultivationResponse(id, op, result, replay == 1, s);
                    }
                    else throw new InvalidDataException();
                    if (stream.Position != stream.Length) throw new InvalidDataException();
                    return request != null ? CultivationPacket.FromRequest(request) : CultivationPacket.FromResponse(response);
                }
            }
            catch (Exception e) when (e is IOException || e is ArgumentException || e is InvalidDataException)
            { customErrorData = "Malformed cultivation packet or incompatible version."; return null; }
        }
        private static void Text(BinaryWriter w, string text)
        { if (!EconomyPacketCodec.ValidId(text)) throw new InvalidDataException(); var bytes = Utf8.GetBytes(text); w.Write((byte)bytes.Length); w.Write(bytes); }
        private static string Text(BinaryReader r)
        {
            int size = r.ReadByte(); if (size > EconomyPacketCodec.MaxTextBytes) throw new InvalidDataException();
            var bytes = r.ReadBytes(size); if (bytes.Length != size) throw new EndOfStreamException();
            string text = Utf8.GetString(bytes); if (!EconomyPacketCodec.ValidId(text)) throw new InvalidDataException(); return text;
        }
    }
    // Only the opt-in local player host multiplexes these independent versioned business protocols.
    public sealed class LocalPlayerPacketCodec : INetworkPacketCodec
    {
        private readonly InsectSpace.GuWorkshop.WorkshopPacketCodec workshop = new InsectSpace.GuWorkshop.WorkshopPacketCodec();
        private readonly EconomyPacketCodec economy = new EconomyPacketCodec();
        private readonly CultivationPacketCodec cultivation = new CultivationPacketCodec();
        private readonly GuPacketCodec gu = new GuPacketCodec();
        private readonly InsectSpace.Moonlight.MoonlightPacketCodec moonlight = new InsectSpace.Moonlight.MoonlightPacketCodec();
        public int MaxPacketSize => 4096;
        public bool Encode<T>(T packet, out byte[] payload) where T : Packet => packet is InsectSpace.GuWorkshop.WorkshopPacket ? workshop.Encode(packet, out payload) : packet is GuPacket ? gu.Encode(packet, out payload) : packet is CultivationPacket ? cultivation.Encode(packet, out payload) : packet is InsectSpace.Moonlight.MoonlightPacket ? moonlight.Encode(packet, out payload) : economy.Encode(packet, out payload);
        public Packet Decode(byte[] payload, int offset, int count, out object customErrorData)
        {
            if (payload != null && offset >= 0 && count >= 8 && offset <= payload.Length - count &&
                payload[offset + 4] == 213 && payload[offset + 5] == 7 && payload[offset + 6] == 0 && payload[offset + 7] == 0)
                return workshop.Decode(payload, offset, count, out customErrorData);
            if (payload != null && offset >= 0 && count >= 8 && offset <= payload.Length - count &&
                payload[offset + 4] == 211 && payload[offset + 5] == 7 && payload[offset + 6] == 0 && payload[offset + 7] == 0)
                return cultivation.Decode(payload, offset, count, out customErrorData);
            if (payload != null && offset >= 0 && count >= 8 && offset <= payload.Length - count &&
                payload[offset + 4] == 212 && payload[offset + 5] == 7 && payload[offset + 6] == 0 && payload[offset + 7] == 0)
                return gu.Decode(payload, offset, count, out customErrorData);
            if (payload != null && offset >= 0 && count >= 8 && offset <= payload.Length - count &&
                payload[offset + 4] == 214 && payload[offset + 5] == 7 && payload[offset + 6] == 0 && payload[offset + 7] == 0)
                return moonlight.Decode(payload, offset, count, out customErrorData);
            return economy.Decode(payload, offset, count, out customErrorData);
        }
    }
}
