using System;
using InsectSpace.Cultivation;
using InsectSpace.Economy;

namespace InsectSpace.Gameplay.Cultivation
{
    public sealed class CultivationClient
    {
        private readonly long player;
        private readonly string home;
        private readonly Action<CultivationRequest> send;
        private long sequence;
        private float waiting;
        public CultivationSnapshot Snapshot { get; private set; }
        public CultivationRequest Pending { get; private set; }
        public CultivationRequest RetryableRequest { get; private set; }
        public CultivationResponse LastResponse { get; private set; }
        public bool Connected { get; private set; }
        public bool Ready => Connected && Snapshot != null && Pending == null;
        public string Status { get; private set; } = "未连接；等待服务器角色资料";
        public CultivationClient(long player, string home, Action<CultivationRequest> send)
        {
            if (player <= 0 || !EconomyPacketCodec.ValidId(home)) throw new ArgumentException("Invalid bound identity.");
            this.player = player; this.home = home; this.send = send ?? throw new ArgumentNullException(nameof(send));
        }
        public void TransportConnected() { Connected = true; Status = "TCP 已连接，读取转数与资质…"; }
        public void Disconnect(string reason)
        {
            if (Pending != null && Pending.Command != CultivationCommand.Snapshot) RetryableRequest = Pending;
            Connected = false; Pending = null; Snapshot = null; LastResponse = null; Status = reason;
        }
        public CultivationRequest Refresh() => Request(CultivationCommand.Snapshot);
        public CultivationRequest Awaken() => Request(CultivationCommand.Awaken);
        public CultivationRequest Breakthrough() => Request(CultivationCommand.Breakthrough);
        private CultivationRequest Request(CultivationCommand command)
        {
            if (!Connected || Pending != null || (command != CultivationCommand.Snapshot && (Snapshot == null || RetryableRequest != null))) throw new InvalidOperationException("Get the authority snapshot and resolve pending operations first.");
            return Send(new CultivationRequest(command, checked(++sequence), Guid.NewGuid().ToString("N"), Snapshot?.Revision ?? 0));
        }
        public CultivationRequest Retry()
        {
            if (!Connected || Pending != null || RetryableRequest == null) throw new InvalidOperationException("No uncertain operation to retry.");
            var r = RetryableRequest; return Send(new CultivationRequest(r.Command, checked(++sequence), r.OperationId, r.ExpectedRevision, r.RulesVersion));
        }
        private CultivationRequest Send(CultivationRequest r)
        {
            Pending = r; waiting = 0; Status = "等待服务器确认；不在客户端随机或预升阶";
            try { send(r); } catch { Disconnect("发送失败；重连后核对原操作"); throw; }
            return r;
        }
        public bool Receive(CultivationResponse r)
        {
            if (!Connected || Pending == null || r == null || r.RequestId != Pending.RequestId || r.OperationId != Pending.OperationId ||
                r.Snapshot == null || r.Snapshot.PlayerId != player || r.Snapshot.HomeRealmId != home || r.Snapshot.RulesVersion != CultivationRules.Version ||
                r.Result < CultivationResult.Ok || r.Result > CultivationResult.CapacityExceeded || (Snapshot != null && r.Snapshot.Revision < Snapshot.Revision)) return false;
            Snapshot = r.Snapshot; LastResponse = r; Pending = null;
            if (RetryableRequest?.OperationId == r.OperationId) RetryableRequest = null;
            Status = ResultText(r.Result) + (r.Replayed ? "（同一操作已去重）" : ""); return true;
        }
        public void Tick(float seconds)
        {
            if (seconds < 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (Pending == null) return;
            waiting += seconds; if (waiting < 8) return;
            if (Pending.Command != CultivationCommand.Snapshot) RetryableRequest = Pending;
            Pending = null; Status = "通讯超时；刷新后重试原操作，不重新随机";
        }
        public static string ResultText(CultivationResult result)
        {
            switch (result)
            {
                case CultivationResult.Ok: return "服务器已确认";
                case CultivationResult.AlreadyAwakened: return "本角色已开窍，资质不会重抽";
                case CultivationResult.NotAwakened: return "请先完成开窍";
                case CultivationResult.ExperienceRequired: return "修炼经验不足";
                case CultivationResult.EvidenceRequired: return "突破证明尚未齐备";
                case CultivationResult.MaximumRank: return "已达九转上限";
                case CultivationResult.RevisionConflict: return "资料已更新，请核对后重新提交";
                case CultivationResult.BattleActive: return "战斗期间不能开窍或突破";
                case CultivationResult.RuleMismatch: return "成长规则版本不兼容";
                default: return "服务器拒绝：" + result;
            }
        }
    }
}
