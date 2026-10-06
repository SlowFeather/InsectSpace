using System;
using System.IO;
using System.Text;
using Framework;
using Framework.Network;
using InsectSpace.Simulation;

namespace InsectSpace.Moonlight
{
    public enum MoonlightMessageKind : byte { AdmissionRequest = 1, Admission, Join, Checkpoint, Input, Frame, Finished, Rejected }
    public enum MoonlightInputKind : byte { CastMoonBlade = 1, SetAutoCast = 2, Retreat = 3 }
    public sealed class MoonlightMessage
    {
        public MoonlightMessageKind Kind { get; }
        public long RequestId { get; }
        public string RoomId { get; }
        public string Ticket { get; }
        public int Port { get; }
        public long Sequence { get; }
        public MoonlightInputKind Input { get; }
        public bool Enabled { get; }
        public int Result { get; }
        public MoonlightBattleSnapshot State { get; }
        public MoonlightMessage(MoonlightMessageKind kind, long requestId = 0, string roomId = "", string ticket = "", int port = 0,
            long sequence = -1, MoonlightInputKind input = 0, bool enabled = false, int result = 0, MoonlightBattleSnapshot state = null)
        { Kind = kind; RequestId = requestId; RoomId = roomId; Ticket = ticket; Port = port; Sequence = sequence; Input = input; Enabled = enabled; Result = result; State = state; }
    }
    public sealed class MoonlightPacket : Packet
    {
        public override int Id => 2006;
        public MoonlightMessage Message { get; private set; }
        public override void Clear() => Message = null;
        public static MoonlightPacket Create(MoonlightMessage message) { var p = ReferencePool.Acquire<MoonlightPacket>(); p.Message = message; return p; }
    }
    public sealed class MoonlightPacketCodec : INetworkPacketCodec
    {
        public int MaxPacketSize => 2048;
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        public static bool Valid(MoonlightMessage m)
        {
            if (m == null || !InsectSpace.Economy.EconomyPacketCodec.ValidId(m.RoomId, true) || !InsectSpace.Economy.EconomyPacketCodec.ValidId(m.Ticket, true) || m.Result < 0 || m.Result > (int)InsectSpace.Economy.EconomyResult.Unauthorized) return false;
            bool room = m.RoomId.Length > 0;
            switch (m.Kind)
            {
                case MoonlightMessageKind.AdmissionRequest: return m.RequestId > 0;
                case MoonlightMessageKind.Admission: return m.RequestId > 0 && (m.Result != 0 || (room && m.Ticket.Length >= 32 && m.Port > 0 && m.Port <= 65535 && m.State != null));
                case MoonlightMessageKind.Join: return room && m.Ticket.Length >= 32;
                case MoonlightMessageKind.Input: return room && m.Sequence >= 0 && m.Input >= MoonlightInputKind.CastMoonBlade && m.Input <= MoonlightInputKind.Retreat;
                case MoonlightMessageKind.Checkpoint: return room && m.State != null;
                case MoonlightMessageKind.Frame: case MoonlightMessageKind.Finished:
                    return room && m.State != null && m.State.Frame >= 0 && ((m.Input == 0 && m.Sequence == -1) || (m.Sequence >= 0 && m.State.LastSequence == m.Sequence));
                case MoonlightMessageKind.Rejected: return m.Result != 0;
                default: return false;
            }
        }
        public bool Encode<T>(T packet, out byte[] payload) where T : Packet
        {
            payload = null; if (!(packet is MoonlightPacket p) || !Valid(p.Message)) return false;
            try { using var s = new MemoryStream(); using var w = new BinaryWriter(s, Utf8); var m = p.Message; w.Write(1); w.Write(p.Id); w.Write((byte)m.Kind);
                switch (m.Kind)
                {
                    case MoonlightMessageKind.AdmissionRequest: w.Write(m.RequestId); break;
                    case MoonlightMessageKind.Admission: w.Write(m.RequestId); w.Write(m.Result); if (m.Result == 0) { Text(w,m.RoomId); Text(w,m.Ticket); w.Write(m.Port); State(w,m.State); } break;
                    case MoonlightMessageKind.Join: Text(w,m.RoomId); Text(w,m.Ticket); break;
                    case MoonlightMessageKind.Input: Text(w,m.RoomId); w.Write(m.Sequence); w.Write((byte)m.Input); w.Write(m.Enabled); break;
                    case MoonlightMessageKind.Checkpoint: Text(w,m.RoomId); State(w,m.State); break;
                    case MoonlightMessageKind.Frame: case MoonlightMessageKind.Finished: Text(w,m.RoomId); w.Write(m.Sequence); w.Write((byte)m.Input); w.Write(m.Enabled); State(w,m.State); break;
                    case MoonlightMessageKind.Rejected: Text(w,m.RoomId); w.Write(m.Result); break;
                }
                if (s.Length > MaxPacketSize) return false; payload = s.ToArray(); return true;
            } catch (Exception e) when (e is IOException || e is ArgumentException || e is InvalidDataException) { return false; }
        }
        public Packet Decode(byte[] payload, int offset, int count, out object customErrorData)
        {
            customErrorData = null; if (payload == null || offset < 0 || count < 9 || count > MaxPacketSize || offset > payload.Length - count) { customErrorData = "Invalid moonlight packet bounds."; return null; }
            try { using var s = new MemoryStream(payload,offset,count,false); using var r = new BinaryReader(s,Utf8); if (r.ReadInt32()!=1 || r.ReadInt32()!=2006) throw new InvalidDataException(); var k=(MoonlightMessageKind)r.ReadByte(); MoonlightMessage m;
                switch(k)
                {
                    case MoonlightMessageKind.AdmissionRequest: m=new MoonlightMessage(k,requestId:r.ReadInt64()); break;
                    case MoonlightMessageKind.Admission: { long id=r.ReadInt64(); int result=r.ReadInt32(); m=result==0?new MoonlightMessage(k,id,Text(r),Text(r),r.ReadInt32(),state:State(r)):new MoonlightMessage(k,id,result:result); break; }
                    case MoonlightMessageKind.Join: m=new MoonlightMessage(k,roomId:Text(r),ticket:Text(r)); break;
                    case MoonlightMessageKind.Input: m=new MoonlightMessage(k,roomId:Text(r),sequence:r.ReadInt64(),input:(MoonlightInputKind)r.ReadByte(),enabled:r.ReadBoolean()); break;
                    case MoonlightMessageKind.Checkpoint: m=new MoonlightMessage(k,roomId:Text(r),state:State(r)); break;
                    case MoonlightMessageKind.Frame: case MoonlightMessageKind.Finished: m=new MoonlightMessage(k,roomId:Text(r),sequence:r.ReadInt64(),input:(MoonlightInputKind)r.ReadByte(),enabled:r.ReadBoolean(),state:State(r)); break;
                    case MoonlightMessageKind.Rejected: m=new MoonlightMessage(k,roomId:Text(r),result:r.ReadInt32()); break;
                    default: throw new InvalidDataException();
                }
                if (!Valid(m) || s.Position != s.Length) throw new InvalidDataException(); return MoonlightPacket.Create(m);
            } catch (Exception e) when (e is IOException || e is ArgumentException || e is InvalidDataException) { customErrorData="Malformed moonlight packet."; return null; }
        }
        private static void Text(BinaryWriter w,string v){var b=Utf8.GetBytes(v);w.Write((byte)b.Length);w.Write(b);}
        private static string Text(BinaryReader r){int n=r.ReadByte();if(n>InsectSpace.Economy.EconomyPacketCodec.MaxTextBytes)throw new InvalidDataException();var b=r.ReadBytes(n);if(b.Length!=n)throw new EndOfStreamException();return Utf8.GetString(b);}
        private static void State(BinaryWriter w,MoonlightBattleSnapshot s){w.Write(s.ActorId);w.Write(s.Frame);w.Write(s.LastSequence);w.Write(s.PlayerHp);w.Write(s.MonsterHp);w.Write(s.Resource);w.Write(s.MoonBladeCooldown);w.Write(s.AutoCast);w.Write((byte)s.Phase);w.Write(s.Hash); Rules(w,s.Rules);}
        private static MoonlightBattleSnapshot State(BinaryReader r){long actor=r.ReadInt64(),frame=r.ReadInt64(),seq=r.ReadInt64();int hp=r.ReadInt32(),mh=r.ReadInt32(),res=r.ReadInt32(),cd=r.ReadInt32();bool auto=r.ReadBoolean();var phase=(MoonlightBattlePhase)r.ReadByte();ulong hash=r.ReadUInt64();var rules=Rules(r);return new MoonlightBattleSnapshot(actor,frame,seq,hp,mh,res,cd,auto,phase,rules,hash);}
        private static void Rules(BinaryWriter w,MoonlightBattleRules x){w.Write(x.PlayerMaxHp);w.Write(x.MonsterMaxHp);w.Write(x.AutoAttackDamage);w.Write(x.AutoAttackIntervalFrames);w.Write(x.MonsterAttackDamage);w.Write(x.MonsterAttackIntervalFrames);w.Write(x.MaxResource);w.Write(x.ResourceRegenPerFrame);w.Write(x.MoonBladeDamage);w.Write(x.MoonBladeCooldownFrames);w.Write(x.MoonBladeResourceCost);w.Write(x.MoonlightGuId);Text(w,x.SkillId);Text(w,x.SkillName);}
        private static MoonlightBattleRules Rules(BinaryReader r)=>new MoonlightBattleRules(r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),Text(r),Text(r));
    }
}
