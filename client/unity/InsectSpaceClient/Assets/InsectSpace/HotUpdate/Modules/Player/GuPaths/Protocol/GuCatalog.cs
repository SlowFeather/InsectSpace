using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using InsectSpace.GuConfig;
using Luban;

namespace InsectSpace.GuPaths
{
    [Flags] public enum GuRole { None = 0, Attack = 1, Defense = 2, Heal = 4, Movement = 8, Support = 16, Control = 32 }
    public enum PathFormation { Empty = 0, Inclination = 1, Formed = 2, Mixed = 3 }
    public sealed class GuEntry
    {
        public int Id { get; } public string Family { get; } public string Name { get; }
        public int Rank { get; } public int Path { get; } public int SupportPath { get; }
        public GuRole Roles { get; } public string Effect { get; } public string Source { get; } public string PathBasis { get; }
        internal GuEntry(GuDefinition g)
        { Id = g.Id; Family = g.Family; Name = g.Name; Rank = g.Rank; Path = g.Path; SupportPath = g.SupportPath; Roles = (GuRole)g.Roles; Effect = g.Effect; Source = g.Source; PathBasis = g.PathBasis; }
    }
    public sealed class PathScore
    {
        public int Path { get; } public int Score { get; } public int GuCount { get; }
        public GuRole Roles { get; }
        public PathScore(int path, int score, int count, GuRole roles) { Path = path; Score = score; GuCount = count; Roles = roles; }
    }
    public sealed class PathProfile
    {
        public PathFormation Formation { get; } public int DominantPath { get; }
        public GuRole Roles { get; } public ReadOnlyCollection<PathScore> Scores { get; }
        public PathProfile(PathFormation formation, int dominant, GuRole roles, IEnumerable<PathScore> scores)
        { Formation = formation; DominantPath = dominant; Roles = roles; Scores = Array.AsReadOnly(scores.ToArray()); }
    }
    // Loaded from the same versioned Luban bytes on both ends. Never a Unity dependency.
    public sealed class GuCatalog
    {
        public const int RulesVersion = 1, MaxSlots = 6, MaxOwned = 256;
        public string Fingerprint { get; }
        public ReadOnlyCollection<GuEntry> Entries { get; }
        public IReadOnlyDictionary<int, string> Paths { get; }
        private readonly Dictionary<int, GuEntry> byId;
        public GuEntry Get(int id) => byId.TryGetValue(id, out var g) ? g : null;
        public string PathName(int id) => id == 0 ? "未定流派" : Paths.TryGetValue(id, out var name) ? name : "未知流派";
        public GuCatalog(Func<string, byte[]> loader)
        {
            if (loader == null) throw new ArgumentNullException(nameof(loader));
            // Fixed ordering and length prefixes bind the entire pair of tables.
            var bytes = new Dictionary<string, byte[]>();
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            using (var sha = SHA256.Create())
            {
                foreach (var name in new[] { "tbpath", "tbgu" })
                { var data = loader(name); if (data == null || data.Length == 0) throw new InvalidDataException("Missing Gu catalog."); bytes.Add(name, data); writer.Write(data.Length); writer.Write(data); }
                Fingerprint = BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-", "").ToLowerInvariant();
            }
            var tables = new GuTables(name => new ByteBuf(bytes[name]));
            var paths = tables.TbPath.DataList.ToDictionary(p => p.Id, p => p.Name);
            if (paths.Count == 0 || paths.Any(p => p.Key <= 0 || string.IsNullOrWhiteSpace(p.Value))) throw new InvalidDataException("Invalid paths.");
            Paths = new ReadOnlyDictionary<int, string>(paths);
            var entries = tables.TbGu.DataList.Select(g => new GuEntry(g)).OrderBy(g => g.Id).ToArray();
            if (entries.Length == 0 || entries.Length > MaxOwned || entries.Any(g => g.Id <= 0 || g.Rank < 1 || g.Rank > 5 ||
                !paths.ContainsKey(g.Path) || (g.SupportPath != 0 && (!paths.ContainsKey(g.SupportPath) || g.SupportPath == g.Path)) ||
                g.Roles == GuRole.None || ((int)g.Roles & ~63) != 0 || string.IsNullOrWhiteSpace(g.Family) || string.IsNullOrWhiteSpace(g.Name) || string.IsNullOrWhiteSpace(g.Source)))
                throw new InvalidDataException("Invalid mortal Gu catalog.");
            byId = entries.ToDictionary(g => g.Id); Entries = Array.AsReadOnly(entries);
        }
        public PathProfile Evaluate(IEnumerable<int> ids)
        {
            var chosen = ids.ToArray();
            if (chosen.Length > MaxSlots || chosen.Distinct().Count() != chosen.Length || chosen.Any(id => Get(id) == null)) throw new ArgumentException("Invalid loadout.");
            var gu = chosen.Select(Get).ToArray();
            if (gu.Select(g => g.Family).Distinct().Count() != gu.Length) throw new ArgumentException("Duplicate evolution family.");
            var scores = new List<PathScore>(); GuRole roles = GuRole.None;
            foreach (var g in gu) roles |= g.Roles;
            foreach (var path in Paths.Keys.OrderBy(p => p))
            {
                int score = 0, count = 0; GuRole coverage = GuRole.None;
                foreach (var g in gu)
                    if (g.Path == path || g.SupportPath == path)
                    { score += g.Path == path ? 3 : 2; count++; coverage |= g.Roles; }
                if (score != 0) scores.Add(new PathScore(path, score, count, coverage));
            }
            scores = scores.OrderByDescending(s => s.Score).ThenBy(s => s.Path).ToList();
            if (scores.Count == 0) return new PathProfile(PathFormation.Empty, 0, roles, scores);
            var top = scores[0]; int total = scores.Sum(s => s.Score);
            bool tie = scores.Count > 1 && scores[1].Score == top.Score;
            bool dominant = !tie && top.Score * 100 >= total * 60;
            bool twoRoles = ((int)top.Roles & ((int)top.Roles - 1)) != 0;
            var state = dominant ? (top.GuCount >= 2 && twoRoles ? PathFormation.Formed : PathFormation.Inclination) : PathFormation.Mixed;
            return new PathProfile(state, dominant ? top.Path : 0, roles, scores);
        }
        public string Describe(PathProfile p) => p.Formation == PathFormation.Empty ? "尚未配置" : p.Formation == PathFormation.Mixed ? "混修 · 无单一主流派" : PathName(p.DominantPath) + (p.Formation == PathFormation.Formed ? " · 已成型" : " · 倾向（需互补蛊虫）");
        public static string RoleNames(GuRole roles)
        {
            var names = new List<string>(); var labels = new[] { "攻击", "防御", "治疗", "移动", "辅助", "控制" };
            for (int i = 0; i < labels.Length; i++) if (((int)roles & (1 << i)) != 0) names.Add(labels[i]);
            return names.Count == 0 ? "无" : string.Join(" / ", names);
        }
    }
}
