using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using InsectSpace.Economy;
using InsectSpace.GuPaths;

namespace InsectSpace.GuWorkshop
{
    public enum WorkshopCommand : byte { Snapshot = 1, Buy, Feed, Refine, Fuse, Equip }
    public enum WorkshopResult : byte
    {
        Ok, InvalidRequest, CatalogMismatch, RevisionConflict, InsufficientFunds, NotOwned,
        RankRequired, Dormant, NotRefined, AlreadyRefined, MissingMaterials, InvalidRecipe,
        EquippedIngredient, DuplicateFamily, BattleActive, FailedPreserved, FailedDestroyed,
        IdempotencyConflict, CapacityExceeded, AlreadyFed
    }
    public sealed class WorkshopRequest
    {
        public WorkshopCommand Command { get; }
        public long RequestId { get; }
        public string OperationId { get; }
        public long Revision { get; }
        public long EconomyRevision { get; }
        public string CatalogHash { get; }
        public int Target { get; }
        public ReadOnlyCollection<long> Instances { get; }
        public WorkshopRequest(WorkshopCommand command, long id, string operation, long revision, long economyRevision,
            string hash, int target = 0, IEnumerable<long> instances = null)
        {
            Command = command; RequestId = id; OperationId = operation; Revision = revision;
            EconomyRevision = economyRevision; CatalogHash = hash; Target = target;
            Instances = Array.AsReadOnly((instances ?? Array.Empty<long>()).ToArray());
        }
        public bool SameOperation(WorkshopRequest other) => other != null && Command == other.Command &&
            Revision == other.Revision && EconomyRevision == other.EconomyRevision && CatalogHash == other.CatalogHash &&
            Target == other.Target && Instances.SequenceEqual(other.Instances);
    }
    public sealed class GuInstance
    {
        public long Id { get; }
        public int DefinitionId { get; }
        public bool Refined { get; }
        public long FedUntil { get; }
        public GuInstance(long id, int definitionId, bool refined, long fedUntil)
        {
            if (id <= 0 || definitionId <= 0 || fedUntil < 0) throw new ArgumentException("Invalid Gu instance.");
            Id = id; DefinitionId = definitionId; Refined = refined; FedUntil = fedUntil;
        }
        public bool DormantAt(long serverSeconds) => FedUntil <= serverSeconds;
    }
    public sealed class WorkshopMaterial
    {
        public int Id { get; }
        public int Count { get; }
        public WorkshopMaterial(int id, int count)
        { if (id <= 0 || count <= 0 || count > 9999) throw new ArgumentException("Invalid material stack."); Id = id; Count = count; }
    }
    public sealed class WorkshopSnapshot
    {
        public const int MaxInstances = 64, MaxMaterials = 32;
        public long PlayerId { get; }
        public string HomeRealmId { get; }
        public long Revision { get; }
        public long EconomyRevision { get; }
        public long CultivationRevision { get; }
        public string CatalogHash { get; }
        public int Rank { get; }
        public long ServerSeconds { get; }
        public StoneAmounts Wallet { get; }
        public bool BattleActive { get; }
        public ReadOnlyCollection<GuInstance> Inventory { get; }
        public ReadOnlyCollection<WorkshopMaterial> Materials { get; }
        public ReadOnlyCollection<long> Loadout { get; }
        public WorkshopSnapshot(long player, string home, long revision, long economyRevision, long cultivationRevision,
            string hash, int rank, long seconds, StoneAmounts wallet, bool battleActive, IEnumerable<GuInstance> inventory,
            IEnumerable<WorkshopMaterial> materials, IEnumerable<long> loadout)
        {
            var items = inventory.ToArray(); var stacks = materials.ToArray(); var slots = loadout.ToArray();
            if (player <= 0 || !EconomyPacketCodec.ValidId(home) || revision < 0 || economyRevision < 0 || cultivationRevision < 0 ||
                !GuPacketCodec.ValidHash(hash) || rank < 0 || rank > 9 || seconds < 0 || !wallet.IsNonNegative ||
                items.Length > MaxInstances || items.Any(g => g == null) || items.Select(g => g.Id).Distinct().Count() != items.Length ||
                stacks.Length > MaxMaterials || stacks.Any(m => m == null) || stacks.Select(m => m.Id).Distinct().Count() != stacks.Length ||
                slots.Length > GuCatalog.MaxSlots || slots.Distinct().Count() != slots.Length || slots.Any(id => !items.Any(g => g.Id == id && g.Refined)))
                throw new ArgumentException("Invalid workshop snapshot.");
            PlayerId = player; HomeRealmId = home; Revision = revision; EconomyRevision = economyRevision;
            CultivationRevision = cultivationRevision; CatalogHash = hash; Rank = rank; ServerSeconds = seconds;
            Wallet = wallet; BattleActive = battleActive; Inventory = Array.AsReadOnly(items);
            Materials = Array.AsReadOnly(stacks); Loadout = Array.AsReadOnly(slots);
        }
    }
    public sealed class WorkshopResponse
    {
        public long RequestId { get; }
        public string OperationId { get; }
        public WorkshopResult Result { get; }
        public bool Replayed { get; }
        public long ProducedInstance { get; }
        public ReadOnlyCollection<long> RemovedInstances { get; }
        public WorkshopSnapshot Snapshot { get; }
        public WorkshopResponse(long id, string operation, WorkshopResult result, bool replayed, WorkshopSnapshot snapshot,
            long produced = 0, IEnumerable<long> removed = null)
        {
            RequestId = id; OperationId = operation; Result = result; Replayed = replayed; Snapshot = snapshot;
            ProducedInstance = produced; RemovedInstances = Array.AsReadOnly((removed ?? Array.Empty<long>()).ToArray());
        }
    }
}
