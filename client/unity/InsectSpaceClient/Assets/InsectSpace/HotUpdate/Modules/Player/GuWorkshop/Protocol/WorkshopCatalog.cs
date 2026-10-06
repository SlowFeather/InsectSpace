using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using InsectSpace.GuPaths;
using InsectSpace.WorkshopConfig;
using Luban;

namespace InsectSpace.GuWorkshop
{
    // Freeze generated lists so consumers cannot mutate authoritative rules after hashing.
    public sealed class WorkshopRecipeDefinition
    {
        public int Id { get; }
        public string Name { get; }
        public IReadOnlyList<WorkshopAmount> Ingredients { get; }
        public int Output { get; }
        public int MinimumRank { get; }
        public IReadOnlyList<WorkshopAmount> Materials { get; }
        public int YuanShi { get; }
        public int Success { get; }
        public int Destroy { get; }
        public string Source { get; }
        internal WorkshopRecipeDefinition(WorkshopRecipe row)
        {
            Id = row.Id; Name = row.Name; Output = row.Output; MinimumRank = row.MinimumRank;
            YuanShi = row.YuanShi; Success = row.Success; Destroy = row.Destroy; Source = row.Source;
            Ingredients = Array.AsReadOnly(row.Ingredients.ToArray());
            Materials = Array.AsReadOnly(row.Materials.ToArray());
        }
    }

    public sealed class WorkshopCatalog
    {
        public GuCatalog Gu { get; }
        public string Fingerprint { get; }
        public IReadOnlyDictionary<int, WorkshopItem> Items { get; }
        public IReadOnlyDictionary<int, WorkshopOffer> Offers { get; }
        public IReadOnlyDictionary<int, WorkshopCare> Care { get; }
        public IReadOnlyDictionary<int, WorkshopRecipeDefinition> Recipes { get; }
        public WorkshopCatalog(GuCatalog gu, Func<string, byte[]> loader)
        {
            Gu = gu ?? throw new ArgumentNullException(nameof(gu));
            var bytes = new Dictionary<string, byte[]>();
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream)) using (var sha = SHA256.Create())
            {
                writer.Write(Gu.Fingerprint); writer.Write(1); // Rules version is included with all table bytes.
                foreach (string name in new[] { "tbitem", "tboffer", "tbcare", "tbrecipe" })
                { var b = loader(name); if (b == null || b.Length == 0) throw new InvalidDataException("Missing workshop table."); bytes.Add(name, b); writer.Write(b.Length); writer.Write(b); }
                Fingerprint = BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-", "").ToLowerInvariant();
            }
            var t = new WorkshopTables(name => new ByteBuf(bytes[name]));
            Items = new ReadOnlyDictionary<int, WorkshopItem>(t.TbItem.DataList.ToDictionary(x => x.Id));
            Offers = new ReadOnlyDictionary<int, WorkshopOffer>(t.TbOffer.DataList.ToDictionary(x => x.Id));
            Care = new ReadOnlyDictionary<int, WorkshopCare>(t.TbCare.DataList.ToDictionary(x => x.Id));
            Recipes = new ReadOnlyDictionary<int, WorkshopRecipeDefinition>(t.TbRecipe.DataList.ToDictionary(x => x.Id, x => new WorkshopRecipeDefinition(x)));
            if (Items.Count == 0 && Care.Count == 0 && Offers.Count == 0 && Recipes.Count == 0)
                throw new InvalidDataException("养炼配置尚未启用：请先确认玩法参数，填写 GuWorkshop/Tables 并运行 Build-WorkshopTables.ps1。");
            if (Items.Count == 0 || Items.Count > WorkshopSnapshot.MaxMaterials || Care.Count == 0 || Offers.Count == 0 || Recipes.Count == 0 ||
                Items.Values.Any(x => x.Id <= 0 || string.IsNullOrWhiteSpace(x.Name)) ||
                Care.Values.Any(x => Gu.Get(x.Id) == null || !Items.ContainsKey(x.Food) || x.FoodCount <= 0 || x.FoodCount > 9999 ||
                    x.FedSeconds <= 0 || x.FedSeconds > 604800 || x.RefineFee <= 0 || !Probabilities(x.Success, x.Destroy)) ||
                Offers.Values.Any(x => x.Id <= 0 || x.YuanShi <= 0 || x.Quantity <= 0 || x.Quantity > 9999 ||
                    !(x.Gu > 0 && x.Item == 0 && x.Quantity == 1 && Care.ContainsKey(x.Gu) || x.Gu == 0 && Items.ContainsKey(x.Item))) ||
                Recipes.Values.Any(x => x.Id <= 0 || x.Ingredients.Count == 0 || x.Ingredients.Count > 6 ||
                    x.Ingredients.Select(i => i.Id).Distinct().Count() != x.Ingredients.Count ||
                    x.Ingredients.Any(i => !Care.ContainsKey(i.Id) || i.Count <= 0 || i.Count > 6) ||
                    x.Ingredients.Sum(i => (long)i.Count) < 2 || x.Ingredients.Sum(i => (long)i.Count) > 6 || !Care.ContainsKey(x.Output) ||
                    x.MinimumRank < Gu.Get(x.Output).Rank || x.MinimumRank > 9 || x.Materials.Count > WorkshopSnapshot.MaxMaterials ||
                    x.Materials.Select(m => m.Id).Distinct().Count() != x.Materials.Count ||
                    x.Materials.Any(m => !Items.ContainsKey(m.Id) || m.Count <= 0 || m.Count > 9999) ||
                    x.YuanShi <= 0 || !Probabilities(x.Success, x.Destroy) || string.IsNullOrWhiteSpace(x.Name) || string.IsNullOrWhiteSpace(x.Source)))
                throw new InvalidDataException("Invalid workshop rules.");
        }
        private static bool Probabilities(int success, int destroy) => success >= 0 && destroy >= 0 && (long)success + destroy <= 10000;
        public static WorkshopResult Outcome(int roll, int success, int destroy)
        {
            if (roll < 0 || roll >= 10000 || !Probabilities(success, destroy)) throw new ArgumentOutOfRangeException(nameof(roll));
            return roll < success ? WorkshopResult.Ok : roll >= 10000 - destroy ? WorkshopResult.FailedDestroyed : WorkshopResult.FailedPreserved;
        }
        public bool ValidSnapshot(WorkshopSnapshot s)
        {
            if (s == null || s.CatalogHash != Fingerprint || s.Inventory.Any(g => !Care.ContainsKey(g.DefinitionId)) || s.Materials.Any(m => !Items.ContainsKey(m.Id))) return false;
            var equipped = s.Loadout.Select(id => s.Inventory.Single(g => g.Id == id)).ToArray();
            if (equipped.Any(g => Gu.Get(g.DefinitionId).Rank > s.Rank)) return false;
            try { Gu.Evaluate(equipped.Select(g => g.DefinitionId)); return true; } catch (ArgumentException) { return false; }
        }
        public PathProfile ActiveProfile(WorkshopSnapshot s) => Gu.Evaluate(s.Loadout.Select(id => s.Inventory.Single(g => g.Id == id))
            .Where(g => !g.DormantAt(s.ServerSeconds)).Select(g => g.DefinitionId));
    }
}
