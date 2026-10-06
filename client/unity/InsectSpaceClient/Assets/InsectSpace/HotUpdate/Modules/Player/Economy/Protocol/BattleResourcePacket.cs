using System;
using System.IO;
using System.Text;
using Framework;
using Framework.Network;
using InsectSpace.BattleEconomy;

namespace InsectSpace.Economy
{
    public enum BattleMessageKind : byte { AdmissionRequest = 1, Admission, Join, Checkpoint, Input, Frame, Finished, Rejected }
    // No currency grants, prices or settlement balances can be submitted by clients.
    public sealed class BattleResourceMessage
    {
        public BattleMessageKind Kind { get; }
        public long RequestId { get; }
        public string RoomId { get; }
        public string ReserveId { get; }
        public string Ticket { get; } // Ephemeral in-memory capability. Never log or serialize to client configuration.
        public int Port { get; }
        public long Sequence { get; }
        public ResourceAction Action { get; }
        public EconomyResult Result { get; }
        public BattleResourceSnapshot State { get; }
        public BattleResourceMessage(BattleMessageKind kind, long requestId = 0, string roomId = "", string reserveId = "",
            string ticket = "", int port = 0, long sequence = -1, ResourceAction action = 0,
            EconomyResult result = EconomyResult.Ok, BattleResourceSnapshot state = null)
        { Kind = kind; RequestId = requestId; RoomId = roomId; ReserveId = reserveId; Ticket = ticket; Port = port; Sequence = sequence; Action = action; Result = result; State = state; }
    }
    public sealed class BattleResourcePacket : Packet
    {
        public override int Id => 2002;
        public BattleResourceMessage Message { get; private set; }
        public override void Clear() { Message = null; }
        public static BattleResourcePacket Create(BattleResourceMessage message)
        { var packet = ReferencePool.Acquire<BattleResourcePacket>(); packet.Message = message; return packet; }
    }
    public sealed class BattleResourceCodec : INetworkPacketCodec
    {
        public int MaxPacketSize => 1024;
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        public static bool Valid(BattleResourceMessage m)
        {
            if (m == null || !EconomyPacketCodec.ValidId(m.RoomId, true) || !EconomyPacketCodec.ValidId(m.ReserveId, true) ||
                !EconomyPacketCodec.ValidId(m.Ticket, true) || m.Result < EconomyResult.Ok || m.Result > EconomyResult.Unauthorized) return false;
            bool room = m.RoomId.Length > 0;
            switch (m.Kind)
            {
                case BattleMessageKind.AdmissionRequest: return m.RequestId > 0;
                case BattleMessageKind.Admission: return m.RequestId > 0 && (m.Result != EconomyResult.Ok ||
                    (room && m.ReserveId.Length > 0 && m.Ticket.Length >= 32 && m.Port > 0 && m.Port <= 65535 && m.State != null));
                case BattleMessageKind.Join: return room && m.Ticket.Length >= 32;
                case BattleMessageKind.Input: return room && m.Sequence >= 0 && m.Action >= ResourceAction.CastSkill && m.Action <= ResourceAction.LeaveBattle;
                case BattleMessageKind.Checkpoint: return room && m.State != null;
                case BattleMessageKind.Frame: case BattleMessageKind.Finished:
                    return room && m.State != null && m.State.Frame >= 0 &&
                        ((m.Action == 0 && m.Sequence == -1 && m.Kind == BattleMessageKind.Frame) ||
                         (m.Sequence >= 0 && m.Action >= ResourceAction.CastSkill && m.Action <= ResourceAction.LeaveBattle && m.State.LastSequence == m.Sequence)) &&
                        (m.Kind != BattleMessageKind.Finished || m.Action == ResourceAction.LeaveBattle);
                case BattleMessageKind.Rejected: return m.Result != EconomyResult.Ok;
                default: return false;
            }
        }
        public bool Encode<T>(T packet, out byte[] payload) where T : Packet
        {
            payload = null;
            if (!(packet is BattleResourcePacket p) || !Valid(p.Message)) return false;
            try
            {
                using (var stream = new MemoryStream())
                using (var w = new BinaryWriter(stream, Utf8))
                {
                    var m = p.Message; w.Write(1); w.Write(p.Id); w.Write((byte)m.Kind);
                    switch (m.Kind)
                    {
                        case BattleMessageKind.AdmissionRequest: w.Write(m.RequestId); Text(w, m.ReserveId); break;
                        case BattleMessageKind.Admission:
                            w.Write(m.RequestId); w.Write((byte)m.Result);
                            if (m.Result == EconomyResult.Ok) { Text(w, m.RoomId); Text(w, m.ReserveId); Text(w, m.Ticket); w.Write(m.Port); State(w, m.State); } break;
                        case BattleMessageKind.Join: Text(w, m.RoomId); Text(w, m.Ticket); break;
                        case BattleMessageKind.Input: Text(w, m.RoomId); w.Write(m.Sequence); w.Write((byte)m.Action); break;
                        case BattleMessageKind.Checkpoint: Text(w, m.RoomId); State(w, m.State); break;
                        case BattleMessageKind.Frame: case BattleMessageKind.Finished:
                            Text(w, m.RoomId); w.Write(m.Sequence); w.Write((byte)m.Action); State(w, m.State); break;
                        case BattleMessageKind.Rejected: Text(w, m.RoomId); w.Write((byte)m.Result); break;
                    }
                    if (stream.Length > MaxPacketSize) return false; payload = stream.ToArray(); return true;
                }
            }
            catch (Exception e) when (e is IOException || e is ArgumentException || e is InvalidDataException) { return false; }
        }
        public Packet Decode(byte[] payload, int offset, int count, out object customErrorData)
        {
            customErrorData = null;
            if (payload == null || offset < 0 || count < 9 || count > MaxPacketSize || offset > payload.Length - count)
            { customErrorData = "Invalid battle packet bounds."; return null; }
            try
            {
                using (var stream = new MemoryStream(payload, offset, count, false))
                using (var r = new BinaryReader(stream, Utf8))
                {
                    if (r.ReadInt32() != 1 || r.ReadInt32() != 2002) throw new InvalidDataException();
                    var kind = (BattleMessageKind)r.ReadByte(); BattleResourceMessage m;
                    switch (kind)
                    {
                        case BattleMessageKind.AdmissionRequest: m = new BattleResourceMessage(kind, requestId: r.ReadInt64(), reserveId: Text(r)); break;
                        case BattleMessageKind.Admission:
                            long id = r.ReadInt64(); var result = (EconomyResult)r.ReadByte();
                            m = result == EconomyResult.Ok ? new BattleResourceMessage(kind, id, Text(r), Text(r), Text(r), r.ReadInt32(), state: State(r)) : new BattleResourceMessage(kind, id, result: result); break;
                        case BattleMessageKind.Join: m = new BattleResourceMessage(kind, roomId: Text(r), ticket: Text(r)); break;
                        case BattleMessageKind.Input: m = new BattleResourceMessage(kind, roomId: Text(r), sequence: r.ReadInt64(), action: (ResourceAction)r.ReadByte()); break;
                        case BattleMessageKind.Checkpoint: m = new BattleResourceMessage(kind, roomId: Text(r), state: State(r)); break;
                        case BattleMessageKind.Frame: case BattleMessageKind.Finished:
                            m = new BattleResourceMessage(kind, roomId: Text(r), sequence: r.ReadInt64(), action: (ResourceAction)r.ReadByte(), state: State(r)); break;
                        case BattleMessageKind.Rejected: m = new BattleResourceMessage(kind, roomId: Text(r), result: (EconomyResult)r.ReadByte()); break;
                        default: throw new InvalidDataException();
                    }
                    if (!Valid(m) || stream.Position != stream.Length) throw new InvalidDataException();
                    return BattleResourcePacket.Create(m);
                }
            }
            catch (Exception e) when (e is IOException || e is ArgumentException || e is InvalidDataException)
            { customErrorData = "Malformed battle packet or checkpoint."; return null; }
        }
        private static void Text(BinaryWriter w, string value)
        { var bytes = Utf8.GetBytes(value); w.Write((byte)bytes.Length); w.Write(bytes); }
        private static string Text(BinaryReader r)
        {
            int length = r.ReadByte(); if (length > EconomyPacketCodec.MaxTextBytes) throw new InvalidDataException();
            var bytes = r.ReadBytes(length); if (bytes.Length != length) throw new EndOfStreamException();
            var value = Utf8.GetString(bytes); if (!EconomyPacketCodec.ValidId(value, true)) throw new InvalidDataException(); return value;
        }
        private static void State(BinaryWriter w, BattleResourceSnapshot s)
        {
            w.Write(s.ActorId); w.Write(s.Frame); w.Write(s.LastSequence); w.Write(s.Essence); w.Write(s.Capacity);
            w.Write(s.Remaining.YuanShi); w.Write(s.Remaining.XianYuanShi);
            w.Write(s.Rules.SkillCost); w.Write(s.Rules.YuanShiRecovery); w.Write(s.Rules.XianYuanShiRecovery); w.Write(s.Hash);
        }
        private static BattleResourceSnapshot State(BinaryReader r)
        {
            var snapshot = new BattleResourceSnapshot(r.ReadInt64(), r.ReadInt64(), r.ReadInt64(), r.ReadInt64(), r.ReadInt64(),
                new StoneAmounts(r.ReadInt64(), r.ReadInt64()), new BattleResourceRules(r.ReadInt64(), r.ReadInt64(), r.ReadInt64()), r.ReadUInt64());
            BattleResources.Restore(snapshot); return snapshot;
        }
    }
}
