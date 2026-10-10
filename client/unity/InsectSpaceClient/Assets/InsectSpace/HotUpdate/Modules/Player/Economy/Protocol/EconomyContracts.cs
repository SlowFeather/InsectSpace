using System;

namespace InsectSpace.Economy
{
    public enum EconomyCommand : byte { Snapshot = 1, PrepareReserve = 2, CancelReserve = 3, WorldAction = 4 }
    public enum EconomyResult : byte
    {
        Ok, InvalidRequest, InsufficientFunds, RevisionConflict, ReserveBusy, ReserveNotFound,
        InvalidState, UnknownAction, StaleRoute, IdempotencyConflict, CapacityExceeded, Unauthorized
    }
    public enum ReservePhase : byte { None, Prepared, InBattle }

    public readonly struct StoneAmounts : IEquatable<StoneAmounts>
    {
        public long YuanShi { get; }
        public long XianYuanShi { get; }
        public bool IsNonNegative => YuanShi >= 0 && XianYuanShi >= 0;
        public bool IsEmpty => YuanShi == 0 && XianYuanShi == 0;
        public StoneAmounts(long yuanShi, long xianYuanShi) { YuanShi = yuanShi; XianYuanShi = xianYuanShi; }
        public bool Equals(StoneAmounts other) => YuanShi == other.YuanShi && XianYuanShi == other.XianYuanShi;
        public override bool Equals(object obj) => obj is StoneAmounts other && Equals(other);
        public override int GetHashCode() => YuanShi.GetHashCode() ^ XianYuanShi.GetHashCode();
        public static StoneAmounts Add(StoneAmounts a, StoneAmounts b) => new StoneAmounts(checked(a.YuanShi + b.YuanShi), checked(a.XianYuanShi + b.XianYuanShi));
        public static StoneAmounts Subtract(StoneAmounts a, StoneAmounts b) => new StoneAmounts(checked(a.YuanShi - b.YuanShi), checked(a.XianYuanShi - b.XianYuanShi));
        public bool Covers(StoneAmounts cost) => cost.IsNonNegative && YuanShi >= cost.YuanShi && XianYuanShi >= cost.XianYuanShi;
    }

    // Immutable server snapshot; held amounts are NOT spendable wallet balances.
    public sealed class EconomySnapshot
    {
        public long PlayerId { get; }
        public string HomeRealmId { get; }
        public long Revision { get; }
        public StoneAmounts Wallet { get; }
        public StoneAmounts Reserve { get; }
        public string ReserveId { get; }
        public ReservePhase Phase { get; }
        public string RoomId { get; }
        public long ImmortalEssence { get; }
        public long EssenceCapacity { get; }
        public EconomySnapshot(long playerId, string homeRealmId, long revision, StoneAmounts wallet,
            StoneAmounts reserve, string reserveId, ReservePhase phase, string roomId, long essence, long capacity)
        {
            if (playerId <= 0 || string.IsNullOrEmpty(homeRealmId) || revision < 0 || !wallet.IsNonNegative ||
                !reserve.IsNonNegative || capacity < 0 || essence < 0 || essence > capacity ||
                phase < ReservePhase.None || phase > ReservePhase.InBattle || reserveId == null || roomId == null ||
                (phase == ReservePhase.None && (!reserve.IsEmpty || reserveId.Length != 0 || roomId.Length != 0)) ||
                (phase != ReservePhase.None && reserveId.Length == 0) ||
                (phase == ReservePhase.Prepared && roomId.Length != 0) || (phase == ReservePhase.InBattle && roomId.Length == 0))
                throw new ArgumentException("Invalid economy snapshot.");
            PlayerId = playerId; HomeRealmId = homeRealmId; Revision = revision; Wallet = wallet; Reserve = reserve;
            ReserveId = reserveId; Phase = phase; RoomId = roomId; ImmortalEssence = essence; EssenceCapacity = capacity;
        }
    }

    public sealed class EconomyRequest
    {
        public EconomyCommand Command { get; }
        public long RequestId { get; }
        public string OperationId { get; }
        public long ExpectedRevision { get; }
        public StoneAmounts Amount { get; }
        public string TargetId { get; }
        public long WorldEpoch { get; }
        public EconomyRequest(EconomyCommand command, long requestId, string operationId, long expectedRevision,
            StoneAmounts amount = default, string targetId = "", long worldEpoch = 0)
        { Command = command; RequestId = requestId; OperationId = operationId; ExpectedRevision = expectedRevision; Amount = amount; TargetId = targetId; WorldEpoch = worldEpoch; }
        public bool SameOperation(EconomyRequest other) => other != null && Command == other.Command &&
            OperationId == other.OperationId && ExpectedRevision == other.ExpectedRevision && Amount.Equals(other.Amount) &&
            TargetId == other.TargetId && WorldEpoch == other.WorldEpoch;
    }

    public sealed class EconomyResponse
    {
        public long RequestId { get; }
        public string OperationId { get; }
        public EconomyResult Result { get; }
        public bool Replayed { get; }
        public EconomySnapshot Snapshot { get; }
        public EconomyResponse(long requestId, string operationId, EconomyResult result, bool replayed, EconomySnapshot snapshot)
        { RequestId = requestId; OperationId = operationId; Result = result; Replayed = replayed; Snapshot = snapshot; }
    }
}
