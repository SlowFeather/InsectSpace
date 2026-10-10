using System;
using InsectSpace.Economy;

namespace InsectSpace.Gameplay.Economy
{
    // Transport-independent request correlation and immutable authoritative read model.
    public sealed class EconomyClient
    {
        private readonly long playerId;
        private readonly string homeRealm;
        private readonly Action<EconomyRequest> send;
        private long sequence;
        private float waiting;
        public EconomySnapshot Snapshot { get; private set; }
        public EconomyRequest Pending { get; private set; }
        public EconomyRequest RetryableRequest { get; private set; }
        public EconomyResponse LastResponse { get; private set; }
        public bool Connected { get; private set; }
        public bool Ready => Connected && Snapshot != null && Pending == null;
        public string Status { get; private set; } = "未连接；不使用本地余额回退";
        public EconomyClient(long playerId, string homeRealm, Action<EconomyRequest> send)
        {
            if (playerId <= 0 || !EconomyPacketCodec.ValidId(homeRealm)) throw new ArgumentException("Invalid bound identity.");
            this.playerId = playerId; this.homeRealm = homeRealm; this.send = send ?? throw new ArgumentNullException(nameof(send));
        }
        public void TransportConnected() { Connected = true; Status = "TCP 已连接，等待经济快照"; }
        public void Disconnect(string reason)
        {
            if (Pending != null && Pending.Command != EconomyCommand.Snapshot) RetryableRequest = Pending;
            Connected = false; Snapshot = null; Pending = null; LastResponse = null; Status = reason;
        }
        public EconomyRequest Refresh() => Request(EconomyCommand.Snapshot);
        public EconomyRequest Prepare(StoneAmounts amount) => Request(EconomyCommand.PrepareReserve, amount);
        public EconomyRequest Cancel() => Request(EconomyCommand.CancelReserve, default, Snapshot?.ReserveId ?? "");
        public EconomyRequest WorldAction(string actionId, long epoch) => Request(EconomyCommand.WorldAction, default, actionId, epoch);
        private EconomyRequest Request(EconomyCommand command, StoneAmounts amount = default, string target = "", long epoch = 0)
        {
            if (!Connected || Pending != null || (command != EconomyCommand.Snapshot && Snapshot == null)) throw new InvalidOperationException("No current server snapshot or request already pending.");
            if (command != EconomyCommand.Snapshot && RetryableRequest != null) throw new InvalidOperationException("Resolve the uncertain operation by retrying it first.");
            return Send(new EconomyRequest(command, checked(++sequence), Guid.NewGuid().ToString("N"), Snapshot?.Revision ?? 0, amount, target, epoch));
        }
        public EconomyRequest Retry()
        {
            if (!Connected || Pending != null || RetryableRequest == null) throw new InvalidOperationException("No retryable request.");
            var old = RetryableRequest;
            return Send(new EconomyRequest(old.Command, checked(++sequence), old.OperationId, old.ExpectedRevision, old.Amount, old.TargetId, old.WorldEpoch));
        }
        private EconomyRequest Send(EconomyRequest request)
        {
            if (!EconomyPacketCodec.ValidRequest(request)) throw new ArgumentException("Invalid economy request.");
            Pending = request; waiting = 0; Status = "等待服务器确认；余额未预扣";
            try { send(request); }
            catch { Disconnect("发送失败；需重新连接并核对；无本地回退"); throw; }
            return request;
        }
        public bool Receive(EconomyResponse response)
        {
            if (!Connected || Pending == null || response == null || response.RequestId != Pending.RequestId ||
                response.OperationId != Pending.OperationId || response.Snapshot == null || response.Snapshot.PlayerId != playerId ||
                response.Snapshot.HomeRealmId != homeRealm || response.Result < EconomyResult.Ok || response.Result > EconomyResult.Unauthorized ||
                (Snapshot != null && response.Snapshot.Revision < Snapshot.Revision)) return false;
            Snapshot = response.Snapshot; LastResponse = response;
            if (RetryableRequest != null && RetryableRequest.OperationId == response.OperationId) RetryableRequest = null;
            Pending = null; Status = response.Result + (response.Replayed ? " / 已去重重放" : " / 服务器已确认"); return true;
        }
        public void Tick(float seconds)
        {
            if (seconds < 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (Pending == null) return; waiting += seconds;
            if (waiting < 8) return;
            if (Pending.Command != EconomyCommand.Snapshot) RetryableRequest = Pending;
            Pending = null; Status = "请求超时，结果未知；刷新快照后用原操作号重试";
        }
    }
}
