using InsectSpace.Cultivation;
using InsectSpace.Economy;
using InsectSpace.GuPaths;
using InsectSpace.Server.Economy;

namespace InsectSpace.Server.GuPaths;

// LOCAL memory storage; no persistence claim. One definition per owned Gu in this first inventory model.
public sealed class GuPathService
{
    private sealed class Entry
    {
        public long Revision;
        public readonly HashSet<int> Owned = new();
        public int[] Loadout = Array.Empty<int>();
        public readonly Dictionary<string, (GuRequest Request, GuResult Result)> Operations = new();
    }
    public const int MaxRecords = 4096;
    private readonly object gate = new();
    private readonly Dictionary<(long Player, string Home), Entry> players = new();
    private readonly Dictionary<string, (long Player, string Home, int Gu)> receipts = new();
    public GuCatalog Catalog { get; }
    public static GuCatalog LoadLocalCatalog() => new(name => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "GuPaths", name + ".bytes")));
    public GuPathService(GuCatalog catalog) { Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog)); }
    public void CreateCharacter(EconomyPrincipal p)
    {
        if (p == null || p.PlayerId <= 0 || !EconomyPacketCodec.ValidId(p.HomeRealmId)) throw new ArgumentException("Invalid bound identity.");
        lock (gate)
        {
            var key = (p.PlayerId, p.HomeRealmId); if (players.ContainsKey(key)) return;
            if (players.Count >= MaxRecords) throw new InvalidOperationException("Player capacity reached.");
            players.Add(key, new Entry());
        }
    }
    // Trusted reward/fixture entry point. No network opcode grants Gu ownership.
    public GuResult GrantOwned(EconomyPrincipal p, string eventId, int guId)
    {
        if (!EconomyPacketCodec.ValidId(eventId) || Catalog.Get(guId) == null) return GuResult.InvalidRequest;
        lock (gate)
        {
            var e = Character(p); var receipt = (p.PlayerId, p.HomeRealmId, guId);
            if (receipts.TryGetValue(eventId, out var old)) return old == receipt ? GuResult.Ok : GuResult.IdempotencyConflict;
            if (receipts.Count >= MaxRecords || e.Revision == long.MaxValue || (!e.Owned.Contains(guId) && e.Owned.Count >= GuCatalog.MaxOwned)) return GuResult.CapacityExceeded;
            receipts.Add(eventId, receipt); if (e.Owned.Add(guId)) e.Revision++; return GuResult.Ok;
        }
    }
    public GuSnapshot Snapshot(EconomyPrincipal p, CultivationSnapshot growth)
    { lock (gate) { ValidateGrowth(p, growth); return Snapshot(p, growth, Character(p)); } }
    // Growth and battle phase are supplied only by the single authority host thread.
    public GuResponse Handle(EconomyPrincipal p, GuRequest r, CultivationSnapshot growth, bool battleActive = false)
    {
        lock (gate)
        {
            ValidateGrowth(p, growth); var e = Character(p);
            if (!GuPacketCodec.ValidRequest(r)) return Reply(GuResult.InvalidRequest);
            if (r.CatalogHash != Catalog.Fingerprint || r.RulesVersion != GuCatalog.RulesVersion) return Reply(GuResult.CatalogMismatch);
            if (r.Command == GuCommand.Snapshot) return Reply(GuResult.Ok);
            if (e.Operations.TryGetValue(r.OperationId, out var old)) return Reply(old.Request.SameOperation(r) ? old.Result : GuResult.IdempotencyConflict, old.Request.SameOperation(r));
            if (e.Operations.Count >= MaxRecords || e.Revision == long.MaxValue) return Reply(GuResult.CapacityExceeded);
            var result = r.ExpectedRevision != e.Revision ? GuResult.RevisionConflict : battleActive ? GuResult.BattleActive : Validate(e, r, growth.Rank);
            if (result == GuResult.Ok)
            { var next = r.Loadout.OrderBy(id => id).ToArray(); if (!e.Loadout.SequenceEqual(next)) { e.Loadout = next; e.Revision++; } }
            e.Operations.Add(r.OperationId, (r, result)); return Reply(result);
            GuResponse Reply(GuResult result, bool replayed = false) => new(r?.RequestId ?? 0, r?.OperationId ?? "", result, replayed, Snapshot(p, growth, e));
        }
    }
    private GuResult Validate(Entry e, GuRequest r, int rank)
    {
        if (r.Loadout.Any(id => Catalog.Get(id) == null)) return GuResult.InvalidRequest;
        if (r.Loadout.Any(id => !e.Owned.Contains(id))) return GuResult.NotOwned;
        if (r.Loadout.Select(id => Catalog.Get(id).Family).Distinct().Count() != r.Loadout.Count) return GuResult.DuplicateFamily;
        if (r.Loadout.Any(id => Catalog.Get(id).Rank > rank)) return GuResult.RankRequired;
        return GuResult.Ok;
    }
    private GuSnapshot Snapshot(EconomyPrincipal p, CultivationSnapshot growth, Entry e) => new(p.PlayerId, p.HomeRealmId, e.Revision, GuCatalog.RulesVersion, Catalog.Fingerprint,
        growth.Rank, growth.Revision, e.Owned.OrderBy(id => id), e.Loadout, Catalog.Evaluate(e.Loadout));
    private Entry Character(EconomyPrincipal p)
    { if (p == null || !players.TryGetValue((p.PlayerId, p.HomeRealmId), out var e)) throw new UnauthorizedAccessException("No server-bound Gu inventory."); return e; }
    private static void ValidateGrowth(EconomyPrincipal p, CultivationSnapshot growth)
    { if (p == null || growth == null || growth.PlayerId != p.PlayerId || growth.HomeRealmId != p.HomeRealmId) throw new UnauthorizedAccessException("Mismatched growth authority."); }
}
