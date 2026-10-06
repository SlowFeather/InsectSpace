using System;
using System.Net;
using Framework.Network;
using Framework.Network.Kcp;
using InsectSpace.BattleEconomy;
using InsectSpace.Economy;

namespace InsectSpace.Gameplay.Economy
{
    // Explicit LOCAL DEVELOPMENT transport. Host owns GF tick. No offline frames or refund fallback.
    public sealed class LocalEconomyBattleClient : IDisposable
    {
        private readonly INetworkManager manager;
        private readonly Action<BattleResourceMessage> tcpSend;
        private readonly Action refreshWallet;
        private readonly long actorId;
        private INetworkChannel channel;
        private long nextRequest, admissionRequest;
        private string ticket;
        private bool joined, awaitingCheckpoint;
        private float waiting, silence;
        public string RoomId { get; private set; }
        public string ReserveId { get; private set; }
        public BattleResourceReplica Replica { get; private set; }
        public ResourceCommand? Pending { get; private set; }
        public bool Busy => admissionRequest != 0 || (channel != null && (!joined || awaitingCheckpoint));
        public bool Ready => channel != null && channel.Connected && joined && !awaitingCheckpoint && Replica != null && !Replica.Finished && Pending == null;
        public bool Finished => Replica != null && Replica.Finished;
        public string Status { get; private set; } = "尚未进入 KCP 资源战斗";
        public LocalEconomyBattleClient(INetworkManager manager, long actorId, Action<BattleResourceMessage> tcpSend, Action refreshWallet)
        { this.manager = manager; this.actorId = actorId; this.tcpSend = tcpSend; this.refreshWallet = refreshWallet; }
        public void RequestAdmission(string reserveId)
        {
#if UNITY_5_3_OR_NEWER && !UNITY_EDITOR
            throw new PlatformNotSupportedException("LOCAL KCP economy battle is Editor-only.");
#else
            if (Busy || Ready || Pending != null) throw new InvalidOperationException("Battle request is already active; disconnect to recover first.");
            DisposeChannel(); Replica = null; RoomId = null; ReserveId = null; waiting = 0;
            admissionRequest = checked(++nextRequest); Status = "TCP 请求服务器分配战斗房间…";
            try { tcpSend(new BattleResourceMessage(BattleMessageKind.AdmissionRequest, requestId: admissionRequest, reserveId: reserveId)); }
            catch { Disconnect("战斗准入发送失败；请重新获取服务器快照"); throw; }
#endif
        }
        public void ReceiveAdmission(BattleResourceMessage message)
        {
            if (admissionRequest == 0 || message.Kind != BattleMessageKind.Admission || message.RequestId != admissionRequest) return;
            admissionRequest = 0;
            if (message.Result != EconomyResult.Ok) { Status = "战斗准入失败：" + message.Result; refreshWallet(); return; }
            if (!BattleResourceCodec.Valid(message) || message.State.ActorId != actorId)
            { Disconnect("战斗准入身份或结构不匹配"); return; }
            RoomId = message.RoomId; ReserveId = message.ReserveId; ticket = message.Ticket;
            waiting = 0; silence = 0; joined = false; awaitingCheckpoint = true;
            try
            {
                channel = manager.CreateNetworkChannel("Economy.ResourceBattle." + Guid.NewGuid().ToString("N"), "kcp", new BattleResourceCodec());
                channel.SetDefaultHandler((_, packet) => { if (packet is BattleResourcePacket p) Receive(p.Message); });
                channel.Connect(IPAddress.Loopback, message.Port, new KcpConnectData { AuthToken = ticket });
                Status = "等待 KCP 连接和服务器校验点…";
            }
            catch { Disconnect("KCP 连接失败；没有本地战斗回退"); throw; }
            refreshWallet();
        }
        public void Send(ResourceAction action)
        {
            if (!Ready || action < ResourceAction.CastSkill || action > ResourceAction.LeaveBattle) throw new InvalidOperationException("No current authority frame, or action already pending.");
            var command = new ResourceCommand(actorId, checked(Replica.Snapshot.LastSequence + 1), action);
            Pending = command; waiting = 0;
            channel.Send(BattleResourcePacket.Create(new BattleResourceMessage(BattleMessageKind.Input, roomId: RoomId, sequence: command.Sequence, action: action)));
            Status = "输入已发送；等待服务器帧，不预扣仙元或储备";
        }
        private void Receive(BattleResourceMessage message)
        {
            if (message.RoomId != RoomId) return;
            if (message.Kind == BattleMessageKind.Rejected)
            { Disconnect("服务器拒绝战斗输入：" + message.Result + "；重连核对托管"); return; }
            if (message.Kind == BattleMessageKind.Checkpoint)
            {
                if (!awaitingCheckpoint || message.State == null || message.State.ActorId != actorId) return;
                try { Replica = new BattleResourceReplica(RoomId, message.State); }
                catch (ArgumentException) { Disconnect("服务器校验点验证失败"); return; }
                awaitingCheckpoint = false; Pending = null; ticket = null; silence = 0;
                Status = "KCP 已准入；资源状态 hash 一致"; return;
            }
            if (Replica == null || awaitingCheckpoint || (message.Kind != BattleMessageKind.Frame && message.Kind != BattleMessageKind.Finished)) return;
            if (!Replica.Receive(message)) { Disconnect("战斗帧或 hash 不一致；已停止，不使用本地回退"); return; }
            silence = 0;
            if (Pending.HasValue && Replica.Snapshot.LastSequence >= Pending.Value.Sequence) Pending = null;
            if (Replica.Finished)
            {
                Status = "服务器已结算，剩余储备返还；正在刷新钱包";
                DisposeChannel(); refreshWallet();
            }
            else Status = "KCP 帧 " + Replica.Snapshot.Frame + " · hash 已验证";
        }
        public void Tick(float elapsed)
        {
            if (elapsed < 0 || float.IsNaN(elapsed) || float.IsInfinity(elapsed)) throw new ArgumentOutOfRangeException(nameof(elapsed));
            if (admissionRequest != 0 || Busy || Pending.HasValue) waiting += elapsed;
            if (waiting >= 8 && (admissionRequest != 0 || Busy || Pending.HasValue))
            { Disconnect("战斗通讯超时，状态未知；重连获取校验点，储备不自动退款"); return; }
            if (channel == null) return;
            if (channel.Connected && !joined)
            {
                joined = true;
                channel.Send(BattleResourcePacket.Create(new BattleResourceMessage(BattleMessageKind.Join, roomId: RoomId, ticket: ticket)));
            }
            else if (!channel.Connected && joined) { Disconnect("KCP 已断线；等待服务器恢复，不生成空帧"); return; }
            if (joined && !awaitingCheckpoint) { silence += elapsed; if (silence >= 8) Disconnect("权威帧超时；停止推进并保留托管"); }
        }
        public void Disconnect(string reason)
        {
            DisposeChannel(); admissionRequest = 0; Pending = null; Replica = null; ticket = null; Status = reason;
        }
        private void DisposeChannel()
        { if (channel != null) { manager.DestroyNetworkChannel(channel.Name); channel = null; } joined = false; awaitingCheckpoint = false; }
        public void Dispose() => Disconnect("战斗连接已关闭；服务器托管不自动退款");
    }
}
