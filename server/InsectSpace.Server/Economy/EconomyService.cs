using InsectSpace.Economy;
using InsectSpace.BattleEconomy;

namespace InsectSpace.Server.Economy;

// Constructed by authenticated server routing, never deserialized from an economy request.
public sealed record EconomyPrincipal(long PlayerId, string HomeRealmId, string WorldInstanceId, long WorldEpoch);
public enum RewardSource { Activity = 1, Monster = 2 }
public sealed record WorldActionPrice(string ActionId, StoneAmounts Cost);

// LOCAL DEVELOPMENT: process lifetime only. The lock is one single-writer transaction boundary.
public sealed class InMemoryEconomyStore
{
    internal readonly object Gate = new();
    internal readonly Dictionary<(long, string), Account> Accounts = new();
    internal readonly Dictionary<(string, string), (long Player, string Home, StoneAmounts Amount)> Credits = new();
    internal sealed class Account
    {
        public long PlayerId, Revision, Essence, Capacity;
        public string Home = "", ReserveId = "", RoomId = "";
        public StoneAmounts Wallet, Held;
        public ReservePhase Phase;
        public BattleResources Battle;
        public readonly Dictionary<string, (EconomyRequest Request, EconomyResult Result)> Operations = new();
        public readonly Dictionary<string, string> CompletedReserves = new();
    }
}

public sealed class EconomyService
{
    private readonly InMemoryEconomyStore store;
    // Server-side business transaction boundary. Callers must hold this lock through their commit.
    internal object WorkshopGate => store.Gate;
    internal InMemoryEconomyStore.Account WorkshopAccount(EconomyPrincipal principal)
    {
        if (!System.Threading.Monitor.IsEntered(store.Gate)) throw new InvalidOperationException("Workshop transaction lock required.");
        return Account(principal);
    }
    private readonly Dictionary<string, StoneAmounts> prices;
    public const int MaxOperations = 4096;
    public EconomyService(InMemoryEconomyStore store, IEnumerable<WorldActionPrice> prices)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.prices = new();
        foreach (var price in prices)
        {
            if (!EconomyPacketCodec.ValidId(price.ActionId) || !price.Cost.IsNonNegative || price.Cost.IsEmpty) throw new ArgumentException("Invalid server price.");
            this.prices.Add(price.ActionId, price.Cost);
        }
    }
    public void CreateAccount(EconomyPrincipal principal, long essence, long capacity)
    {
        if (principal == null || principal.PlayerId <= 0 || !EconomyPacketCodec.ValidId(principal.HomeRealmId) || essence < 0 || capacity < essence) throw new ArgumentException("Invalid account.");
        lock (store.Gate)
            store.Accounts.Add((principal.PlayerId, principal.HomeRealmId), new InMemoryEconomyStore.Account
                { PlayerId = principal.PlayerId, Home = principal.HomeRealmId, Essence = essence, Capacity = capacity });
    }
    public EconomySnapshot Snapshot(EconomyPrincipal principal) { lock (store.Gate) return SnapshotOf(Account(principal)); }
    public BattleResourceSnapshot BattleSnapshot(EconomyPrincipal principal, string roomId)
    {
        lock (store.Gate)
        {
            var a = Account(principal);
            if (a.Phase != ReservePhase.InBattle || a.RoomId != roomId || a.Battle == null)
                throw new InvalidOperationException("No current authority battle.");
            return a.Battle.Capture();
        }
    }

    // Only invoked after server-side reward eligibility / probability evaluation. No client grant opcode.
    public EconomyResult GrantReward(EconomyPrincipal principal, RewardSource source, string eventId, long yuanShi)
    {
        if (source != RewardSource.Activity && source != RewardSource.Monster) return EconomyResult.InvalidRequest;
        return Credit(principal, "reward-" + source, eventId, new StoneAmounts(yuanShi, 0));
    }
    // Trust boundary: the caller is the payment verification service, NOT a client receipt claim.
    // Real payment provider validation is intentionally outside this local milestone.
    public EconomyResult ApplyVerifiedRecharge(EconomyPrincipal principal, string providerOrderId, long xianYuanShi) =>
        Credit(principal, "verified-payment", providerOrderId, new StoneAmounts(0, xianYuanShi));
    private EconomyResult Credit(EconomyPrincipal principal, string source, string receipt, StoneAmounts amount)
    {
        if (!EconomyPacketCodec.ValidId(receipt) || !amount.IsNonNegative || amount.IsEmpty) return EconomyResult.InvalidRequest;
        lock (store.Gate)
        {
            var a = Account(principal);
            if (store.Credits.TryGetValue((source, receipt), out var prior))
                return prior.Player == a.PlayerId && prior.Home == a.Home && prior.Amount.Equals(amount) ? EconomyResult.Ok : EconomyResult.IdempotencyConflict;
            if (store.Credits.Count >= MaxOperations || a.Revision == long.MaxValue) return EconomyResult.CapacityExceeded;
            try
            {
                // Include escrow, so later refunds can never overflow even after more grants.
                StoneAmounts.Add(StoneAmounts.Add(a.Wallet, a.Held), amount);
                var wallet = StoneAmounts.Add(a.Wallet, amount);
                store.Credits.Add((source, receipt), (a.PlayerId, a.Home, amount)); a.Wallet = wallet; a.Revision++;
                return EconomyResult.Ok;
            }
            catch (OverflowException) { return EconomyResult.CapacityExceeded; }
        }
    }
    public EconomyResponse Handle(EconomyPrincipal principal, EconomyRequest request)
    {
        lock (store.Gate)
        {
            var a = Account(principal);
            if (!EconomyPacketCodec.ValidRequest(request)) return Reply(a, request, EconomyResult.InvalidRequest);
            if (request.Command == EconomyCommand.Snapshot) return Reply(a, request, EconomyResult.Ok);
            if (a.Operations.TryGetValue(request.OperationId, out var prior))
                return Reply(a, request, prior.Request.SameOperation(request) ? prior.Result : EconomyResult.IdempotencyConflict, prior.Request.SameOperation(request));
            if (a.Operations.Count >= MaxOperations || a.Revision == long.MaxValue) return Reply(a, request, EconomyResult.CapacityExceeded);
            EconomyResult result = request.ExpectedRevision != a.Revision ? EconomyResult.RevisionConflict : Mutate(a, principal, request);
            a.Operations.Add(request.OperationId, (request, result));
            return Reply(a, request, result);
        }
    }
    private EconomyResult Mutate(InMemoryEconomyStore.Account a, EconomyPrincipal principal, EconomyRequest r)
    {
        switch (r.Command)
        {
            case EconomyCommand.PrepareReserve:
                if (a.Phase != ReservePhase.None) return EconomyResult.ReserveBusy;
                if (a.CompletedReserves.Count >= MaxOperations) return EconomyResult.CapacityExceeded;
                if (!a.Wallet.Covers(r.Amount)) return EconomyResult.InsufficientFunds;
                a.Wallet = StoneAmounts.Subtract(a.Wallet, r.Amount); a.Held = r.Amount;
                a.ReserveId = Guid.NewGuid().ToString("N"); a.Phase = ReservePhase.Prepared; break;
            case EconomyCommand.CancelReserve:
                if (a.ReserveId != r.TargetId) return EconomyResult.ReserveNotFound;
                if (a.Phase != ReservePhase.Prepared) return EconomyResult.InvalidState;
                Refund(a); break;
            case EconomyCommand.WorldAction:
                if (a.Phase == ReservePhase.InBattle) return EconomyResult.InvalidState;
                if (string.IsNullOrEmpty(principal.WorldInstanceId) || principal.WorldEpoch != r.WorldEpoch) return EconomyResult.StaleRoute;
                if (!prices.TryGetValue(r.TargetId, out var cost)) return EconomyResult.UnknownAction;
                if (!a.Wallet.Covers(cost)) return EconomyResult.InsufficientFunds;
                a.Wallet = StoneAmounts.Subtract(a.Wallet, cost); break;
            default: return EconomyResult.InvalidRequest;
        }
        a.Revision++; return EconomyResult.Ok;
    }
    // Trusted battle service APIs. Neither room binding, frames nor settlement amounts are client opcodes.
    public EconomyResult BeginBattle(EconomyPrincipal principal, string reserveId, string roomId, BattleResourceRules rules)
    {
        if (!EconomyPacketCodec.ValidId(roomId) || !EconomyPacketCodec.ValidId(reserveId, true) || rules == null) return EconomyResult.InvalidRequest;
        lock (store.Gate)
        {
            var a = Account(principal);
            if (a.CompletedReserves.Values.Contains(roomId)) return EconomyResult.InvalidState;
            if (a.Phase == ReservePhase.InBattle && a.RoomId == roomId && (reserveId.Length == 0 || a.ReserveId == reserveId)) return EconomyResult.Ok;
            if (a.Phase == ReservePhase.None && reserveId.Length == 0)
            {
                if (a.Revision == long.MaxValue || a.CompletedReserves.Count >= MaxOperations) return EconomyResult.CapacityExceeded;
                a.ReserveId = Guid.NewGuid().ToString("N");
                a.Phase = ReservePhase.Prepared; // Zero-stone admission still has a server settlement identity.
                reserveId = a.ReserveId;
            }
            if (a.ReserveId != reserveId) return EconomyResult.ReserveNotFound;
            if (a.Phase == ReservePhase.InBattle) return a.RoomId == roomId ? EconomyResult.Ok : EconomyResult.InvalidState;
            if (a.Phase != ReservePhase.Prepared) return EconomyResult.InvalidState;
            if (a.Revision == long.MaxValue) return EconomyResult.CapacityExceeded;
            a.Battle = new BattleResources(a.PlayerId, a.Essence, a.Capacity, a.Held, rules);
            a.RoomId = roomId; a.Phase = ReservePhase.InBattle; a.Revision++; return EconomyResult.Ok;
        }
    }
    public EconomyResult ApplyBattleFrame(EconomyPrincipal principal, string roomId, long frame, IReadOnlyList<ResourceCommand> commands)
    {
        lock (store.Gate)
        {
            var a = Account(principal);
            if (a.Phase != ReservePhase.InBattle || a.RoomId != roomId) return EconomyResult.InvalidState;
            if (a.Revision == long.MaxValue) return EconomyResult.CapacityExceeded;
            if (!a.Battle.ApplyFrame(frame, commands)) return EconomyResult.InvalidRequest;
            a.Held = a.Battle.Remaining; a.Essence = a.Battle.Essence; a.Revision++; return EconomyResult.Ok;
        }
    }
    public EconomyResult SettleBattle(EconomyPrincipal principal, string reserveId, string roomId)
    {
        if (!EconomyPacketCodec.ValidId(reserveId) || !EconomyPacketCodec.ValidId(roomId)) return EconomyResult.InvalidRequest;
        lock (store.Gate)
        {
            var a = Account(principal);
            if (a.CompletedReserves.TryGetValue(reserveId, out var completed))
                return completed == roomId && roomId.Length > 0 ? EconomyResult.Ok : EconomyResult.IdempotencyConflict;
            if (a.ReserveId != reserveId || a.Phase != ReservePhase.InBattle || a.RoomId != roomId) return EconomyResult.InvalidState;
            if (a.Revision == long.MaxValue) return EconomyResult.CapacityExceeded;
            Refund(a); a.Revision++; return EconomyResult.Ok;
        }
    }
    private void Refund(InMemoryEconomyStore.Account a)
    {
        a.Wallet = StoneAmounts.Add(a.Wallet, a.Held); a.CompletedReserves.Add(a.ReserveId, a.RoomId);
        a.Held = default; a.Phase = ReservePhase.None; a.ReserveId = ""; a.RoomId = ""; a.Battle = null;
    }
    private InMemoryEconomyStore.Account Account(EconomyPrincipal p)
    {
        if (p == null || !store.Accounts.TryGetValue((p.PlayerId, p.HomeRealmId), out var account)) throw new UnauthorizedAccessException("No server-bound economy identity.");
        return account;
    }
    private static EconomySnapshot SnapshotOf(InMemoryEconomyStore.Account a) => new(a.PlayerId, a.Home, a.Revision, a.Wallet, a.Held, a.ReserveId, a.Phase, a.RoomId, a.Essence, a.Capacity);
    private static EconomyResponse Reply(InMemoryEconomyStore.Account a, EconomyRequest r, EconomyResult result, bool replay = false) =>
        new(r?.RequestId ?? 0, r?.OperationId ?? "", result, replay, SnapshotOf(a));
}
