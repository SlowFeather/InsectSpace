using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using InsectSpace.Economy;

namespace InsectSpace.GuPaths
{
    public enum GuCommand { Snapshot = 1, Equip = 2 }
    public enum GuResult { Ok = 0, InvalidRequest, CatalogMismatch, RevisionConflict, NotOwned, RankRequired, DuplicateFamily, BattleActive, IdempotencyConflict, CapacityExceeded }
    public sealed class GuRequest
    {
        public GuCommand Command { get; } public long RequestId { get; } public string OperationId { get; }
        public long ExpectedRevision { get; } public int RulesVersion { get; } public string CatalogHash { get; }
        public ReadOnlyCollection<int> Loadout { get; }
        public GuRequest(GuCommand command, long requestId, string operationId, long revision, string catalogHash, IEnumerable<int> loadout = null, int rulesVersion = GuCatalog.RulesVersion)
        { Command = command; RequestId = requestId; OperationId = operationId; ExpectedRevision = revision; CatalogHash = catalogHash; RulesVersion = rulesVersion; Loadout = Array.AsReadOnly((loadout ?? Array.Empty<int>()).ToArray()); }
        public bool SameOperation(GuRequest other) => other != null && Command == other.Command && ExpectedRevision == other.ExpectedRevision && CatalogHash == other.CatalogHash && RulesVersion == other.RulesVersion && Loadout.SequenceEqual(other.Loadout);
    }
    public sealed class GuSnapshot
    {
        public long PlayerId { get; } public string HomeRealmId { get; } public long Revision { get; }
        public int RulesVersion { get; } public string CatalogHash { get; }
        public int PlayerRank { get; } public long CultivationRevision { get; }
        public ReadOnlyCollection<int> Owned { get; } public ReadOnlyCollection<int> Loadout { get; }
        public PathProfile Profile { get; }
        public GuSnapshot(long player, string home, long revision, int version, string hash, int rank, long cultivationRevision, IEnumerable<int> owned, IEnumerable<int> loadout, PathProfile profile)
        {
            var inventory = owned.ToArray(); var slots = loadout.ToArray();
            if (player <= 0 || !EconomyPacketCodec.ValidId(home) || revision < 0 || version <= 0 || !GuPacketCodec.ValidHash(hash) || rank < 0 || rank > 9 || cultivationRevision < 0 ||
                inventory.Length > GuCatalog.MaxOwned || slots.Length > GuCatalog.MaxSlots || inventory.Any(id => id <= 0) || inventory.Distinct().Count() != inventory.Length ||
                slots.Any(id => !inventory.Contains(id)) || slots.Distinct().Count() != slots.Length || !GuPacketCodec.ValidProfile(profile)) throw new ArgumentException("Invalid Gu snapshot.");
            PlayerId = player; HomeRealmId = home; Revision = revision; RulesVersion = version; CatalogHash = hash; PlayerRank = rank; CultivationRevision = cultivationRevision;
            Owned = Array.AsReadOnly(inventory); Loadout = Array.AsReadOnly(slots); Profile = profile;
        }
    }
    public sealed class GuResponse
    {
        public long RequestId { get; } public string OperationId { get; } public GuResult Result { get; }
        public bool Replayed { get; } public GuSnapshot Snapshot { get; }
        public GuResponse(long id, string op, GuResult result, bool replayed, GuSnapshot snapshot)
        { RequestId = id; OperationId = op; Result = result; Replayed = replayed; Snapshot = snapshot; }
    }
}
