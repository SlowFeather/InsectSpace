using System;
using System.Net;
using Framework.Network;
using Framework.Network.Kcp;
using InsectSpace.Moonlight;
using InsectSpace.Simulation;

namespace InsectSpace.Gameplay.Economy
{
    // Explicit LOCAL PvE transport. The client only displays and validates
    // server snapshots; it never advances an authoritative battle locally.
    public sealed class LocalMoonlightBattleClient : IDisposable
    {
        private readonly INetworkManager manager;
        private readonly Action<MoonlightMessage> tcpSend;
        private readonly long actorId;
        private INetworkChannel channel;
        private long nextRequest, admissionRequest;
        private long pendingSequence = -1;
        private string ticket;
        private bool joined, awaitingCheckpoint;
        private float waiting, silence;
        public string RoomId { get; private set; }
        public MoonlightBattleReplica Replica { get; private set; }
        public MoonlightInputKind? Pending { get; private set; }
        public bool Busy => admissionRequest != 0 || (channel != null && (!joined || awaitingCheckpoint));
        public bool Ready => channel != null && channel.Connected && joined && !awaitingCheckpoint && Replica != null && !Replica.Finished && !Pending.HasValue;
        public bool Finished => Replica != null && Replica.Finished;
        public string Status { get; private set; } = "尚未进入月光蛊战斗";

        public LocalMoonlightBattleClient(INetworkManager manager, long actorId, Action<MoonlightMessage> tcpSend)
        { this.manager = manager; this.actorId = actorId; this.tcpSend = tcpSend; }

        public void RequestAdmission()
        {
#if UNITY_5_3_OR_NEWER && !UNITY_EDITOR
            throw new PlatformNotSupportedException("LOCAL moonlight KCP battle is Editor-only.");
#else
            if (Busy || Ready || Pending.HasValue) throw new InvalidOperationException("Moonlight battle request is already active.");
            DisposeChannel(); Replica = null; RoomId = null; waiting = 0;
            admissionRequest = checked(++nextRequest);
            Status = "TCP 请求月光蛊战斗房间…";
            try { tcpSend(new MoonlightMessage(MoonlightMessageKind.AdmissionRequest, requestId: admissionRequest)); }
            catch { Disconnect("月光战斗准入发送失败；没有本地战斗回退"); throw; }
#endif
        }

        public void ReceiveAdmission(MoonlightMessage message)
        {
            if (admissionRequest == 0 || message.Kind != MoonlightMessageKind.Admission || message.RequestId != admissionRequest) return;
            admissionRequest = 0;
            if (message.Result != 0) { Status = "月光战斗准入失败：" + message.Result; return; }
            if (!MoonlightPacketCodec.Valid(message) || message.State.ActorId != actorId)
            { Disconnect("月光战斗身份或结构不匹配"); return; }
            RoomId = message.RoomId; ticket = message.Ticket; waiting = 0; silence = 0; joined = false; awaitingCheckpoint = true;
            try
            {
                channel = manager.CreateNetworkChannel("Economy.MoonlightBattle." + Guid.NewGuid().ToString("N"), "kcp", new MoonlightPacketCodec());
                channel.SetDefaultHandler((_, packet) => { if (packet is MoonlightPacket p) Receive(p.Message); });
                channel.Connect(IPAddress.Loopback, message.Port, new KcpConnectData { AuthToken = ticket });
                Status = "等待月光蛊 KCP 校验点…";
            }
            catch { Disconnect("月光战斗 KCP 连接失败；没有本地战斗回退"); throw; }
        }

        public void CastMoonBlade() => Send(MoonlightInputKind.CastMoonBlade, false);
        public void SetAutoCast(bool enabled) => Send(MoonlightInputKind.SetAutoCast, enabled);
        public void Retreat() => Send(MoonlightInputKind.Retreat, false);

        private void Send(MoonlightInputKind input, bool enabled)
        {
            if (!Ready) throw new InvalidOperationException("没有可用的月光蛊权威帧。");
            Pending = input; waiting = 0;
            long sequence = checked(Replica.Snapshot.LastSequence + 1);
            pendingSequence = sequence;
            channel.Send(MoonlightPacket.Create(new MoonlightMessage(MoonlightMessageKind.Input, roomId: RoomId, sequence: sequence, input: input, enabled: enabled)));
            Status = "月光蛊输入已发送；等待服务器帧";
        }

        private void Receive(MoonlightMessage message)
        {
            if (message.RoomId != RoomId) return;
            if (message.Kind == MoonlightMessageKind.Rejected)
            { Disconnect("服务器拒绝月光蛊输入：" + message.Result); return; }
            if (message.Kind == MoonlightMessageKind.Checkpoint)
            {
                if (!awaitingCheckpoint || message.State == null || message.State.ActorId != actorId) return;
                try { Replica = new MoonlightBattleReplica(message.State); }
                catch (ArgumentException) { Disconnect("月光战斗校验点 hash 不一致"); return; }
                awaitingCheckpoint = false; Pending = null; pendingSequence = -1; ticket = null; silence = 0; Status = "月光蛊战斗已准入；hash 一致"; return;
            }
            if (Replica == null || awaitingCheckpoint || (message.Kind != MoonlightMessageKind.Frame && message.Kind != MoonlightMessageKind.Finished)) return;
            if (!Replica.Receive(message)) { Disconnect("月光战斗帧或 hash 不一致；已停止"); return; }
            silence = 0;
            if (Pending.HasValue && Replica.Snapshot.LastSequence >= pendingSequence)
            { Pending = null; pendingSequence = -1; }
            Status = Replica.Finished ? "月光蛊战斗结束：" + Replica.Snapshot.Phase : "服务器帧 " + Replica.Snapshot.Frame + " · hash 已验证";
        }

        public void Tick(float elapsed)
        {
            if (elapsed < 0 || float.IsNaN(elapsed) || float.IsInfinity(elapsed)) throw new ArgumentOutOfRangeException(nameof(elapsed));
            if (admissionRequest != 0 || Busy || Pending.HasValue) waiting += elapsed;
            if (waiting >= 8 && (admissionRequest != 0 || Busy || Pending.HasValue)) { Disconnect("月光战斗通讯超时；不使用本地回退"); return; }
            if (channel == null) return;
            if (channel.Connected && !joined)
            {
                joined = true;
                channel.Send(MoonlightPacket.Create(new MoonlightMessage(MoonlightMessageKind.Join, roomId: RoomId, ticket: ticket)));
            }
            else if (!channel.Connected && joined) { Disconnect("月光战斗 KCP 已断线；不生成空帧"); return; }
            if (joined && !awaitingCheckpoint) { silence += elapsed; if (silence >= 8) Disconnect("月光战斗权威帧超时"); }
        }

        public void Disconnect(string reason) { DisposeChannel(); admissionRequest = 0; Pending = null; pendingSequence = -1; Replica = null; ticket = null; Status = reason; }
        private void DisposeChannel() { if (channel != null) { manager.DestroyNetworkChannel(channel.Name); channel = null; } joined = false; awaitingCheckpoint = false; }
        public void Dispose() => Disconnect("月光战斗连接已关闭；服务器内存房间保留");
    }

    public sealed class MoonlightBattleReplica
    {
        public MoonlightBattleSnapshot Snapshot { get; private set; }
        public bool Finished => Snapshot.Phase != MoonlightBattlePhase.Active;
        public long ExpectedSequence => Snapshot.LastSequence + 1;
        public MoonlightBattleReplica(MoonlightBattleSnapshot snapshot)
        { MoonlightBattleState.Restore(snapshot); Snapshot = snapshot; }
        public bool Receive(MoonlightMessage message)
        {
            if (message == null || message.State == null || message.State.ActorId != Snapshot.ActorId || message.State.Frame != Snapshot.Frame + 1) return false;
            if (message.Input == 0 && message.Sequence != -1)
            {
                if (message.State.LastSequence != message.Sequence) return false;
            }
            else if (message.Input != 0 && (message.Sequence != Snapshot.LastSequence + 1 || message.State.LastSequence != message.Sequence)) return false;
            try { MoonlightBattleState.Restore(message.State); } catch (ArgumentException) { return false; }
            Snapshot = message.State; return true;
        }
    }
}
