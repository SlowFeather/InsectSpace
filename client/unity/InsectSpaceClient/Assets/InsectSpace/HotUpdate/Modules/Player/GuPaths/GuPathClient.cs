using System;
using System.Collections.Generic;
using System.Linq;
using InsectSpace.Economy;
using InsectSpace.GuPaths;

namespace InsectSpace.Gameplay.GuPaths
{
    public sealed class GuPathClient
    {
        private readonly long player; private readonly string home; private readonly Action<GuRequest> send;
        private long sequence; private float waiting;
        public GuCatalog Catalog { get; }
        public GuSnapshot Snapshot { get; private set; }
        public GuRequest Pending { get; private set; } public GuRequest RetryableRequest { get; private set; }
        public GuResponse LastResponse { get; private set; } public bool Connected { get; private set; }
        public bool Ready => Connected && Snapshot != null && Pending == null;
        public string Status { get; private set; } = "等待服务器蛊虫背包";
        public GuPathClient(long player, string home, GuCatalog catalog, Action<GuRequest> send)
        { if (player <= 0 || !EconomyPacketCodec.ValidId(home)) throw new ArgumentException("Invalid identity."); this.player = player; this.home = home; Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog)); this.send = send ?? throw new ArgumentNullException(nameof(send)); }
        public void TransportConnected() { Connected = true; Status = "读取蛊虫与组合…"; }
        public void Disconnect(string reason)
        { if (Pending?.Command == GuCommand.Equip) RetryableRequest = Pending; Pending = null; Snapshot = null; LastResponse = null; Connected = false; Status = reason; }
        public GuRequest Refresh() => Request(GuCommand.Snapshot, Array.Empty<int>());
        public GuRequest Equip(IEnumerable<int> ids) => Request(GuCommand.Equip, ids);
        private GuRequest Request(GuCommand command, IEnumerable<int> ids)
        {
            if (!Connected || Pending != null || (command == GuCommand.Equip && (Snapshot == null || RetryableRequest != null))) throw new InvalidOperationException("请先读取权威资料并处理未确认操作。");
            var r = new GuRequest(command, checked(++sequence), Guid.NewGuid().ToString("N"), Snapshot?.Revision ?? 0, Catalog.Fingerprint, ids);
            if (!GuPacketCodec.ValidRequest(r)) throw new ArgumentException("组合最多六只蛊虫。"); return Send(r);
        }
        public GuRequest Retry()
        {
            if (!Connected || Pending != null || RetryableRequest == null) throw new InvalidOperationException("没有可重试的操作。");
            var r = RetryableRequest; return Send(new GuRequest(r.Command, checked(++sequence), r.OperationId, r.ExpectedRevision, r.CatalogHash, r.Loadout, r.RulesVersion));
        }
        private GuRequest Send(GuRequest r)
        { Pending = r; waiting = 0; Status = "等待服务器确认组合…"; try { send(r); } catch { Disconnect("发送失败，重连后重试原操作。"); throw; } return r; }
        public bool Receive(GuResponse r)
        {
            if (!Connected || Pending == null || r == null || r.RequestId != Pending.RequestId || r.OperationId != Pending.OperationId || r.Result < GuResult.Ok || r.Result > GuResult.CapacityExceeded) return false;
            var s = r.Snapshot;
            if (s == null || s.PlayerId != player || s.HomeRealmId != home) return false;
            if (s.CatalogHash != Catalog.Fingerprint || s.RulesVersion != GuCatalog.RulesVersion || r.Result == GuResult.CatalogMismatch)
            { Disconnect("目录或规则不兼容，请更新客户端目录后重连。"); return false; }
            if (Snapshot != null && (s.Revision < Snapshot.Revision || s.CultivationRevision < Snapshot.CultivationRevision)) return false;
            if (s.Owned.Any(id => Catalog.Get(id) == null) || s.Loadout.Any(id => Catalog.Get(id).Rank > s.PlayerRank)) return false;
            PathProfile expected;
            try { expected = Catalog.Evaluate(s.Loadout); } catch (ArgumentException) { return false; }
            if (expected.Formation != s.Profile.Formation || expected.DominantPath != s.Profile.DominantPath || expected.Roles != s.Profile.Roles || expected.Scores.Count != s.Profile.Scores.Count) return false;
            for (int i = 0; i < expected.Scores.Count; i++)
            { var a = expected.Scores[i]; var b = s.Profile.Scores[i]; if (a.Path != b.Path || a.Score != b.Score || a.GuCount != b.GuCount || a.Roles != b.Roles) return false; }
            Snapshot = s; LastResponse = r; Pending = null; if (RetryableRequest?.OperationId == r.OperationId) RetryableRequest = null;
            Status = ResultText(r.Result) + (r.Replayed ? "（原操作已去重）" : ""); return true;
        }
        public void Tick(float seconds)
        {
            if (seconds < 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (Pending == null) return; waiting += seconds; if (waiting < 8) return;
            if (Pending.Command == GuCommand.Equip) RetryableRequest = Pending; Pending = null; Status = "请求超时，请刷新并重试原组合。";
        }
        public static string ResultText(GuResult r)
        {
            switch (r)
            {
                case GuResult.Ok: return "服务器已确认";
                case GuResult.NotOwned: return "尚未拥有该蛊虫";
                case GuResult.RankRequired: return "角色转数不足，请先修炼突破";
                case GuResult.DuplicateFamily: return "同一进阶系列只能装备一只";
                case GuResult.BattleActive: return "战斗中不能更换组合";
                case GuResult.RevisionConflict: return "背包或组合已更新，请重新核对";
                case GuResult.CatalogMismatch: return "目录或规则版本不一致";
                default: return "服务器拒绝：" + r;
            }
        }
    }
}
