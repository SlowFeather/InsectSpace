using System.Diagnostics;
using System.Security.Cryptography;
using InsectSpace.Cultivation;
using InsectSpace.Economy;
using InsectSpace.GuWorkshop;
using InsectSpace.Server.Economy;

namespace InsectSpace.Server.GuWorkshop;

public interface IWorkshopClock { long Seconds { get; } }
public interface IWorkshopRandom { int Next(int exclusiveMaximum); }
// Business time only, never used by the authoritative battle simulation.
public sealed class WorkshopServerClock : IWorkshopClock
{
    private readonly long start = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    public long Seconds => checked(start + elapsed.ElapsedMilliseconds / 1000);
}
public sealed class WorkshopServerRandom : IWorkshopRandom
{ public int Next(int exclusiveMaximum) => RandomNumberGenerator.GetInt32(exclusiveMaximum); }

public sealed class WorkshopService
{
    public const int MaxOperations = 4096;
    private sealed class Entry
    {
        public long Revision, NextInstance = 1;
        public Dictionary<long, GuInstance> Inventory = new();
        public Dictionary<int, int> Materials = new();
        public long[] Loadout = Array.Empty<long>();
        public readonly Dictionary<string, Receipt> Operations = new();
    }
    private sealed record Receipt(WorkshopRequest Request, WorkshopResult Result, long Produced, long[] Removed);
    private readonly Dictionary<(long, string), Entry> players = new();
    private readonly EconomyService economy;
    private readonly IWorkshopClock clock;
    private readonly IWorkshopRandom random;
    private long lastTime;
    public WorkshopCatalog Catalog { get; }
    public static WorkshopCatalog LoadLocalCatalog() => new(GuPaths.GuPathService.LoadLocalCatalog(),
        name => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "GuWorkshop", name + ".bytes")));
    public WorkshopService(EconomyService economy, WorkshopCatalog catalog, IWorkshopClock clock = null, IWorkshopRandom random = null)
    { this.economy = economy ?? throw new ArgumentNullException(nameof(economy)); Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog)); this.clock = clock ?? new WorkshopServerClock(); this.random = random ?? new WorkshopServerRandom(); }
    public void CreateCharacter(EconomyPrincipal principal)
    {
        lock (economy.WorkshopGate)
        {
            economy.WorkshopAccount(principal); var key = (principal.PlayerId, principal.HomeRealmId);
            if (players.ContainsKey(key)) return;
            if (players.Count >= MaxOperations) throw new InvalidOperationException("Workshop character capacity reached.");
            players.Add(key, new Entry());
        }
    }
    public WorkshopSnapshot Snapshot(EconomyPrincipal principal, CultivationSnapshot growth)
    { lock (economy.WorkshopGate) { ValidateGrowth(principal, growth); return State(principal, growth, Character(principal), Now()); } }
    public WorkshopResponse Handle(EconomyPrincipal principal, WorkshopRequest request, CultivationSnapshot growth)
    {
        lock (economy.WorkshopGate)
        {
            ValidateGrowth(principal, growth); var e = Character(principal); var account = economy.WorkshopAccount(principal); long now = Now();
            if (!WorkshopPacketCodec.ValidRequest(request)) return Reply(WorkshopResult.InvalidRequest);
            if (request.CatalogHash != Catalog.Fingerprint) return Reply(WorkshopResult.CatalogMismatch);
            if (request.Command == WorkshopCommand.Snapshot) return Reply(WorkshopResult.Ok);
            if (e.Operations.TryGetValue(request.OperationId, out var prior))
                return request.SameOperation(prior.Request) ? Reply(prior.Result, true, prior.Produced, prior.Removed) : Reply(WorkshopResult.IdempotencyConflict);
            if (e.Operations.Count >= MaxOperations || e.Revision == long.MaxValue || account.Revision == long.MaxValue || e.NextInstance == long.MaxValue)
                return Reply(WorkshopResult.CapacityExceeded);
            WorkshopResult result; long produced = 0; long[] removed = Array.Empty<long>();
            // Battle lock is an authority boundary. Return it before revision checks so a stale
            // client cannot mistake an in-battle mutation attempt for an ordinary resync.
            if (account.Phase == ReservePhase.InBattle) result = WorkshopResult.BattleActive;
            else if (request.Revision != e.Revision || request.EconomyRevision != account.Revision) result = WorkshopResult.RevisionConflict;
            else result = Mutate(e, account, request, growth.Rank, now, out produced, out removed);
            e.Operations.Add(request.OperationId, new Receipt(request, result, produced, removed));
            return Reply(result, false, produced, removed);

            WorkshopResponse Reply(WorkshopResult code, bool replayed = false, long output = 0, long[] lost = null) =>
                new(request?.RequestId ?? 0, request?.OperationId ?? "", code, replayed, State(principal, growth, e, now), output, lost);
        }
    }
    private WorkshopResult Mutate(Entry e, InMemoryEconomyStore.Account account, WorkshopRequest r, int rank, long now, out long produced, out long[] removed)
    {
        produced = 0; removed = Array.Empty<long>();
        // Work on copies until every precondition and random-source result has been validated.
        var items = new Dictionary<long, GuInstance>(e.Inventory); var materials = new Dictionary<int, int>(e.Materials);
        var loadout = e.Loadout; long fee = 0, next = e.NextInstance; var result = WorkshopResult.Ok;
        if (r.Instances.Any(id => !items.ContainsKey(id))) return WorkshopResult.NotOwned;
        var selected = r.Instances.Select(id => items[id]).OrderBy(g => g.Id).ToArray();
        if (r.Command == WorkshopCommand.Buy)
        {
            if (!Catalog.Offers.TryGetValue(r.Target, out var offer)) return WorkshopResult.InvalidRequest;
            fee = offer.YuanShi;
            if (offer.Gu != 0)
            {
                if (rank < Catalog.Gu.Get(offer.Gu).Rank) return WorkshopResult.RankRequired;
                if (items.Count >= WorkshopSnapshot.MaxInstances) return WorkshopResult.CapacityExceeded;
                if (now > long.MaxValue - Catalog.Care[offer.Gu].FedSeconds) return WorkshopResult.CapacityExceeded;
                produced = next++; items.Add(produced, new GuInstance(produced, offer.Gu, false, now + Catalog.Care[offer.Gu].FedSeconds));
            }
            else
            {
                int count = materials.GetValueOrDefault(offer.Item);
                if (count > 9999 - offer.Quantity || count == 0 && materials.Count >= WorkshopSnapshot.MaxMaterials) return WorkshopResult.CapacityExceeded;
                materials[offer.Item] = count + offer.Quantity;
            }
        }
        else if (r.Command == WorkshopCommand.Feed)
        {
            var g = selected[0]; var rule = Catalog.Care[g.DefinitionId];
            if (rule.Food != r.Target) return WorkshopResult.MissingMaterials;
            if (now > long.MaxValue - rule.FedSeconds) return WorkshopResult.CapacityExceeded;
            if (g.FedUntil >= now + rule.FedSeconds) return WorkshopResult.AlreadyFed;
            if (materials.GetValueOrDefault(rule.Food) < rule.FoodCount) return WorkshopResult.MissingMaterials;
            Consume(rule.Food, rule.FoodCount); items[g.Id] = new GuInstance(g.Id, g.DefinitionId, g.Refined, now + rule.FedSeconds);
        }
        else if (r.Command == WorkshopCommand.Equip)
        {
            if (selected.Any(g => !g.Refined)) return WorkshopResult.NotRefined;
            if (selected.Any(g => g.DormantAt(now))) return WorkshopResult.Dormant;
            if (selected.Any(g => Catalog.Gu.Get(g.DefinitionId).Rank > rank)) return WorkshopResult.RankRequired;
            if (selected.Select(g => Catalog.Gu.Get(g.DefinitionId).Family).Distinct().Count() != selected.Length) return WorkshopResult.DuplicateFamily;
            loadout = selected.Select(g => g.Id).ToArray();
        }
        else
        {
            if (selected.Any(g => g.DormantAt(now))) return WorkshopResult.Dormant;
            if (selected.Any(g => e.Loadout.Contains(g.Id))) return WorkshopResult.EquippedIngredient;
            int success, destroy; int output = 0;
            if (r.Command == WorkshopCommand.Refine)
            {
                var g = selected[0]; var rule = Catalog.Care[g.DefinitionId];
                if (g.Refined) return WorkshopResult.AlreadyRefined;
                if (rank < Catalog.Gu.Get(g.DefinitionId).Rank) return WorkshopResult.RankRequired;
                fee = rule.RefineFee; success = rule.Success; destroy = rule.Destroy;
            }
            else if (r.Command == WorkshopCommand.Fuse)
            {
                if (!Catalog.Recipes.TryGetValue(r.Target, out var recipe)) return WorkshopResult.InvalidRecipe;
                if (selected.Length != recipe.Ingredients.Sum(i => i.Count) ||
                    recipe.Ingredients.Any(i => selected.Count(g => g.DefinitionId == i.Id) != i.Count)) return WorkshopResult.InvalidRecipe;
                if (selected.Any(g => !g.Refined)) return WorkshopResult.NotRefined;
                if (rank < recipe.MinimumRank) return WorkshopResult.RankRequired;
                if (recipe.Materials.Any(m => materials.GetValueOrDefault(m.Id) < m.Count)) return WorkshopResult.MissingMaterials;
                if (now > long.MaxValue - Catalog.Care[recipe.Output].FedSeconds) return WorkshopResult.CapacityExceeded;
                fee = recipe.YuanShi; success = recipe.Success; destroy = recipe.Destroy; output = recipe.Output;
                foreach (var m in recipe.Materials) Consume(m.Id, m.Count);
            }
            else return WorkshopResult.InvalidRequest;
            if (account.Wallet.YuanShi < fee) return WorkshopResult.InsufficientFunds;
            result = WorkshopCatalog.Outcome(Roll(10000), success, destroy);
            if (result == WorkshopResult.Ok)
            {
                if (output == 0) { var g = selected[0]; items[g.Id] = new GuInstance(g.Id, g.DefinitionId, true, g.FedUntil); }
                else
                {
                    removed = selected.Select(g => g.Id).ToArray(); foreach (long id in removed) items.Remove(id);
                    produced = next++; items.Add(produced, new GuInstance(produced, output, true, now + Catalog.Care[output].FedSeconds));
                }
            }
            else if (result == WorkshopResult.FailedDestroyed)
            { long lost = selected[selected.Length == 1 ? 0 : Roll(selected.Length)].Id; items.Remove(lost); removed = new[] { lost }; }
        }
        if (account.Wallet.YuanShi < fee) { produced = 0; removed = Array.Empty<long>(); return WorkshopResult.InsufficientFunds; }
        var wallet = StoneAmounts.Subtract(account.Wallet, new StoneAmounts(fee, 0));
        // No fallible validation follows this point. Wallet and inventory commit under the SAME lock.
        account.Wallet = wallet; if (fee != 0) account.Revision++;
        e.Inventory = items; e.Materials = materials; e.Loadout = loadout; e.NextInstance = next; e.Revision++;
        return result;
        void Consume(int id, int quantity) { int count = materials[id] - quantity; if (count == 0) materials.Remove(id); else materials[id] = count; }
    }
    private int Roll(int maximum)
    { int value = random.Next(maximum); if (value < 0 || value >= maximum) throw new InvalidOperationException("Invalid server random result."); return value; }
    private long Now()
    { long seconds = clock.Seconds; if (seconds < 0) throw new InvalidOperationException("Invalid business time."); lastTime = Math.Max(lastTime, seconds); return lastTime; }
    private Entry Character(EconomyPrincipal p)
    { if (p == null || !players.TryGetValue((p.PlayerId, p.HomeRealmId), out var e)) throw new UnauthorizedAccessException("No bound workshop identity."); return e; }
    private WorkshopSnapshot State(EconomyPrincipal p, CultivationSnapshot growth, Entry e, long now)
    {
        var a = economy.WorkshopAccount(p);
        return new WorkshopSnapshot(p.PlayerId, p.HomeRealmId, e.Revision, a.Revision, growth.Revision, Catalog.Fingerprint, growth.Rank, now,
            a.Wallet, a.Phase == ReservePhase.InBattle, e.Inventory.Values.OrderBy(g => g.Id),
            e.Materials.OrderBy(m => m.Key).Select(m => new WorkshopMaterial(m.Key, m.Value)), e.Loadout);
    }
    private static void ValidateGrowth(EconomyPrincipal p, CultivationSnapshot g)
    { if (p == null || g == null || g.PlayerId != p.PlayerId || g.HomeRealmId != p.HomeRealmId) throw new UnauthorizedAccessException("Mismatched cultivation authority."); }
}
