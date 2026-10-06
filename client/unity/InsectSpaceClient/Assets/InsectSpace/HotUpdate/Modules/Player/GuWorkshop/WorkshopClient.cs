using System;
using System.Collections.Generic;
using InsectSpace.GuWorkshop;

namespace InsectSpace.Gameplay.GuWorkshop
{
    public sealed class WorkshopClient
    {
        private readonly long player; private readonly string home; private readonly Action<WorkshopRequest> send;
        private long sequence; private float elapsed;
        public WorkshopCatalog Catalog { get; }
        public WorkshopSnapshot Snapshot { get; private set; }
        public WorkshopRequest Pending { get; private set; }
        public WorkshopRequest RetryableRequest { get; private set; }
        public WorkshopResponse LastResponse { get; private set; }
        public bool Connected { get; private set; }
        public bool Ready => Connected && Snapshot != null && Pending == null;
        public string Status { get; private set; } = "等待连接养炼服务器";
        public WorkshopClient(long player, string home, WorkshopCatalog catalog, Action<WorkshopRequest> send)
        {
            if (player <= 0 || !InsectSpace.Economy.EconomyPacketCodec.ValidId(home)) throw new ArgumentException("Invalid workshop identity.");
            this.player = player; this.home = home; Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog)); this.send = send ?? throw new ArgumentNullException(nameof(send));
        }
        public void TransportConnected() { Connected = true; Status = "正在读取养炼资料"; }
        public void Disconnect(string reason)
        { if (Pending != null && Pending.Command != WorkshopCommand.Snapshot) RetryableRequest = Pending; Pending = null; Snapshot = null; LastResponse = null; Connected = false; Status = reason; }
        public WorkshopRequest Refresh() => Request(WorkshopCommand.Snapshot);
        public WorkshopRequest Request(WorkshopCommand command, int target = 0, IEnumerable<long> instances = null)
        {
            if (!Connected || Pending != null || (command != WorkshopCommand.Snapshot && (Snapshot == null || RetryableRequest != null))) throw new InvalidOperationException("请先连接、刷新资料并处理未确认操作。");
            var request = new WorkshopRequest(command, checked(++sequence), Guid.NewGuid().ToString("N"), Snapshot?.Revision ?? 0,
                Snapshot?.EconomyRevision ?? 0, Catalog.Fingerprint, target, instances);
            if (!WorkshopPacketCodec.ValidRequest(request)) throw new ArgumentException("养炼请求参数无效。");
            return Send(request);
        }
        public WorkshopRequest Retry()
        {
            if (!Connected || Pending != null || RetryableRequest == null) throw new InvalidOperationException("没有待重试操作。");
            var r = RetryableRequest; return Send(new WorkshopRequest(r.Command, checked(++sequence), r.OperationId, r.Revision, r.EconomyRevision, r.CatalogHash, r.Target, r.Instances));
        }
        private WorkshopRequest Send(WorkshopRequest r)
        { Pending = r; elapsed = 0; Status = "等待服务器结算…"; try { send(r); } catch { Disconnect("发送失败；重连后查询原操作"); throw; } return r; }
        public bool Receive(WorkshopResponse r)
        {
            if (!Connected || Pending == null || r == null || r.RequestId != Pending.RequestId || r.OperationId != Pending.OperationId ||
                r.Result < WorkshopResult.Ok || r.Result > WorkshopResult.AlreadyFed || r.Snapshot == null || r.Snapshot.PlayerId != player || r.Snapshot.HomeRealmId != home) return false;
            if (r.Result == WorkshopResult.CatalogMismatch || r.Snapshot.CatalogHash != Catalog.Fingerprint)
            { Disconnect("养炼目录不兼容，请更新后重连"); return false; }
            var s = r.Snapshot;
            if (!Catalog.ValidSnapshot(s) || Snapshot != null && (s.Revision < Snapshot.Revision || s.EconomyRevision < Snapshot.EconomyRevision ||
                s.CultivationRevision < Snapshot.CultivationRevision || s.ServerSeconds < Snapshot.ServerSeconds)) return false;
            Snapshot = s; LastResponse = r; Pending = null;
            if (RetryableRequest?.OperationId == r.OperationId) RetryableRequest = null;
            Status = ResultText(r.Result) + (r.Replayed ? "（原结果；未重新扣费或抽取）" : ""); return true;
        }
        public void Tick(float seconds)
        {
            if (seconds < 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (Pending == null) return; elapsed += seconds; if (elapsed < 8) return;
            if (Pending.Command != WorkshopCommand.Snapshot) RetryableRequest = Pending; Pending = null; Status = "请求超时；刷新后查询原操作，勿重复炼制";
        }
        public static string ResultText(WorkshopResult result)
        {
            switch (result)
            {
                case WorkshopResult.Ok: return "服务器已确认";
                case WorkshopResult.FailedPreserved: return "炼制失败：蛊虫保留，费用与辅材已消耗";
                case WorkshopResult.FailedDestroyed: return "炼制失败：一只投入蛊已损失，费用与辅材已消耗";
                case WorkshopResult.InsufficientFunds: return "元石不足，未扣费";
                case WorkshopResult.Dormant: return "蛊虫饥饿休眠，请先喂食";
                case WorkshopResult.NotRefined: return "野生蛊尚未炼化";
                case WorkshopResult.AlreadyRefined: return "这只蛊已经炼化";
                case WorkshopResult.AlreadyFed: return "无需补喂";
                case WorkshopResult.NotOwned: return "所选蛊虫已不在背包";
                case WorkshopResult.RankRequired: return "修为不足，请先突破";
                case WorkshopResult.MissingMaterials: return "对应食料或炼材不足";
                case WorkshopResult.EquippedIngredient: return "先从组合卸下用于炼制的蛊虫";
                case WorkshopResult.DuplicateFamily: return "同一系列只能装备一只";
                case WorkshopResult.InvalidRecipe: return "所选蛊虫与蛊方不符";
                case WorkshopResult.BattleActive: return "战斗期间不能更改养炼库存";
                case WorkshopResult.RevisionConflict: return "资料已变化，请核对最新状态后重新确认";
                default: return "服务器拒绝：" + result;
            }
        }
    }
}
