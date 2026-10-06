using System;
using System.IO;
using System.Text;
using Framework;
using Framework.Network;

namespace InsectSpace.Economy
{
    public sealed class EconomyPacket : Packet
    {
        public override int Id => 2001;
        public EconomyRequest Request { get; private set; }
        public EconomyResponse Response { get; private set; }
        public override void Clear() { Request = null; Response = null; }
        public static EconomyPacket FromRequest(EconomyRequest request)
        { var packet = ReferencePool.Acquire<EconomyPacket>(); packet.Request = request; return packet; }
        public static EconomyPacket FromResponse(EconomyResponse response)
        { var packet = ReferencePool.Acquire<EconomyPacket>(); packet.Response = response; return packet; }
    }

    // v1 little endian; GF TCP framing supplies the outer packet length. Never JSON/string commands.
    public sealed class EconomyPacketCodec : INetworkPacketCodec
    {
        public const int Version = 1;
        public const int MaxTextBytes = 96;
        public int MaxPacketSize => 1024;
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        private readonly BattleResourceCodec battle = new BattleResourceCodec();
        public static bool ValidId(string value, bool empty = false)
        {
            if (value == null || value.Length > MaxTextBytes || (!empty && value.Length == 0)) return false;
            foreach (char c in value)
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') && c != '-' && c != '_' && c != '.') return false;
            return true;
        }
        public static bool ValidRequest(EconomyRequest r)
        {
            if (r == null || r.RequestId <= 0 || r.ExpectedRevision < 0 || !ValidId(r.OperationId) ||
                !ValidId(r.TargetId, true) || !r.Amount.IsNonNegative) return false;
            switch (r.Command)
            {
                case EconomyCommand.Snapshot: return r.Amount.IsEmpty && r.TargetId.Length == 0 && r.WorldEpoch == 0;
                case EconomyCommand.PrepareReserve: return !r.Amount.IsEmpty && r.TargetId.Length == 0 && r.WorldEpoch == 0;
                case EconomyCommand.CancelReserve: return r.Amount.IsEmpty && r.TargetId.Length > 0 && r.WorldEpoch == 0;
                case EconomyCommand.WorldAction: return r.Amount.IsEmpty && r.TargetId.Length > 0 && r.WorldEpoch > 0;
                default: return false;
            }
        }
        public bool Encode<T>(T packet, out byte[] payload) where T : Packet
        {
            payload = null;
            if (packet is BattleResourcePacket) return battle.Encode(packet, out payload);
            if (!(packet is EconomyPacket p) || (p.Request == null) == (p.Response == null)) return false;
            try
            {
                using (var stream = new MemoryStream())
                using (var w = new BinaryWriter(stream, Utf8))
                {
                    w.Write(Version); w.Write(p.Id); w.Write((byte)(p.Request != null ? 1 : 2));
                    if (p.Request != null)
                    {
                        var r = p.Request; if (!ValidRequest(r)) return false;
                        w.Write((byte)r.Command); w.Write(r.RequestId); WriteText(w, r.OperationId); w.Write(r.ExpectedRevision);
                        WriteAmount(w, r.Amount); WriteText(w, r.TargetId); w.Write(r.WorldEpoch);
                    }
                    else
                    {
                        var r = p.Response; var s = r.Snapshot;
                        if (r.RequestId <= 0 || !ValidId(r.OperationId) || r.Result < EconomyResult.Ok || r.Result > EconomyResult.Unauthorized || s == null) return false;
                        w.Write(r.RequestId); WriteText(w, r.OperationId); w.Write((byte)r.Result); w.Write(r.Replayed);
                        w.Write(s.PlayerId); WriteText(w, s.HomeRealmId); w.Write(s.Revision); WriteAmount(w, s.Wallet); WriteAmount(w, s.Reserve);
                        WriteText(w, s.ReserveId); w.Write((byte)s.Phase); WriteText(w, s.RoomId); w.Write(s.ImmortalEssence); w.Write(s.EssenceCapacity);
                    }
                    if (stream.Length > MaxPacketSize) return false; payload = stream.ToArray(); return true;
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is InvalidDataException) { return false; }
        }
        public Packet Decode(byte[] payload, int offset, int count, out object customErrorData)
        {
            customErrorData = null;
            if (payload != null && offset >= 0 && count >= 8 && offset <= payload.Length - count &&
                payload[offset + 4] == 210 && payload[offset + 5] == 7 && payload[offset + 6] == 0 && payload[offset + 7] == 0)
                return battle.Decode(payload, offset, count, out customErrorData);
            if (payload == null || offset < 0 || count < 10 || count > MaxPacketSize || offset > payload.Length - count)
            { customErrorData = "Invalid economy packet bounds."; return null; }
            try
            {
                using (var stream = new MemoryStream(payload, offset, count, false))
                using (var r = new BinaryReader(stream, Utf8))
                {
                    if (r.ReadInt32() != Version || r.ReadInt32() != 2001) throw new InvalidDataException();
                    byte kind = r.ReadByte(); EconomyRequest request = null; EconomyResponse response = null;
                    if (kind == 1)
                    {
                        request = new EconomyRequest((EconomyCommand)r.ReadByte(), r.ReadInt64(), ReadText(r), r.ReadInt64(), ReadAmount(r), ReadText(r), r.ReadInt64());
                        if (!ValidRequest(request)) throw new InvalidDataException();
                    }
                    else if (kind == 2)
                    {
                        long requestId = r.ReadInt64(); string operation = ReadText(r); var result = (EconomyResult)r.ReadByte(); byte replay = r.ReadByte();
                        var snapshot = new EconomySnapshot(r.ReadInt64(), ReadText(r), r.ReadInt64(), ReadAmount(r), ReadAmount(r), ReadText(r),
                            (ReservePhase)r.ReadByte(), ReadText(r), r.ReadInt64(), r.ReadInt64());
                        if (requestId <= 0 || !ValidId(operation) || result < EconomyResult.Ok || result > EconomyResult.Unauthorized || replay > 1) throw new InvalidDataException();
                        response = new EconomyResponse(requestId, operation, result, replay == 1, snapshot);
                    }
                    else throw new InvalidDataException();
                    if (stream.Position != stream.Length) throw new InvalidDataException();
                    return request != null ? EconomyPacket.FromRequest(request) : EconomyPacket.FromResponse(response);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is InvalidDataException)
            { customErrorData = "Malformed economy packet or incompatible version."; return null; }
        }
        private static void WriteAmount(BinaryWriter writer, StoneAmounts amount) { writer.Write(amount.YuanShi); writer.Write(amount.XianYuanShi); }
        private static StoneAmounts ReadAmount(BinaryReader reader) => new StoneAmounts(reader.ReadInt64(), reader.ReadInt64());
        private static void WriteText(BinaryWriter writer, string value)
        {
            if (!ValidId(value, true)) throw new InvalidDataException();
            byte[] bytes = Utf8.GetBytes(value); writer.Write((byte)bytes.Length); writer.Write(bytes);
        }
        private static string ReadText(BinaryReader reader)
        {
            int length = reader.ReadByte(); if (length > MaxTextBytes) throw new InvalidDataException();
            byte[] bytes = reader.ReadBytes(length); if (bytes.Length != length) throw new EndOfStreamException();
            string value = Utf8.GetString(bytes); if (!ValidId(value, true)) throw new InvalidDataException(); return value;
        }
    }
}
