using System;
using InsectSpace.Economy;

namespace InsectSpace.Cultivation
{
    public enum MortalStage : byte { None, Initial, Middle, High, Peak }
    public enum AptitudeGrade : byte { None, Ding, Bing, Yi, Jia, Extreme }
    public enum CultivationCommand : byte { Snapshot = 1, Awaken, Breakthrough }
    public enum CultivationResult : byte
    { Ok, InvalidRequest, RuleMismatch, RevisionConflict, IdempotencyConflict, AlreadyAwakened, NotAwakened, ExperienceRequired, EvidenceRequired, MaximumRank, BattleActive, CapacityExceeded }
    [Flags]
    public enum CultivationProof : byte { None = 0, MortalTrial = 1, Ascension = 2, SupremeGrandmaster = 4, HeavenlySealBroken = 8, ImmortalSourceActive = 16 }
    public enum CultivationEvidence : byte { MortalTrial = 1, Ascension, HeavenlyTribulation, GrandTribulation, MyriadTribulation, MainPathDaoMarks, SupremeGrandmaster, HeavenlySealBroken }

    // Pure C# immutable facts. Currency, energy balances and cultivation are different models.
    public sealed class Aptitude
    {
        public AptitudeGrade Grade { get; }
        public int SeaPercent { get; }
        public int StopStep { get; }
        public Aptitude(AptitudeGrade grade, int seaPercent, int stopStep)
        {
            int index = (int)grade;
            bool valid = grade == AptitudeGrade.None ? seaPercent == 0 && stopStep == 0 :
                grade == AptitudeGrade.Extreme ? seaPercent == 100 && stopStep == 50 :
                index >= 1 && index <= 4 && seaPercent >= index * 20 && seaPercent < index * 20 + 20 && stopStep >= index * 10 && stopStep < index * 10 + 10;
            if (!valid) throw new ArgumentException("Invalid aptitude band, sea percentage or ceremony step.");
            Grade = grade; SeaPercent = seaPercent; StopStep = stopStep;
        }
    }
    public sealed class CultivationSnapshot
    {
        public long PlayerId { get; }
        public string HomeRealmId { get; }
        public long Revision { get; }
        public int RulesVersion { get; }
        public int Rank { get; }
        public MortalStage Stage { get; }
        public Aptitude Aptitude { get; }
        public long Experience { get; }
        public long MainPathDaoMarks { get; }
        public int HeavenlyTribulations { get; }
        public int GrandTribulations { get; }
        public int MyriadTribulations { get; }
        public CultivationProof Proofs { get; }
        public bool Awakened => Rank > 0;
        public CultivationSnapshot(long playerId, string homeRealmId, long revision, int rulesVersion, int rank,
            MortalStage stage, Aptitude aptitude, long experience = 0, long daoMarks = 0,
            int heavenly = 0, int grand = 0, int myriad = 0, CultivationProof proofs = CultivationProof.None)
        {
            if (playerId <= 0 || !EconomyPacketCodec.ValidId(homeRealmId) || revision < 0 || rulesVersion != CultivationRules.Version ||
                rank < 0 || rank > 9 || aptitude == null || experience < 0 || daoMarks < 0 || heavenly < 0 || grand < 0 || myriad < 0 || ((int)proofs & ~31) != 0 ||
                (rank == 0 ? aptitude.Grade != AptitudeGrade.None : aptitude.Grade == AptitudeGrade.None) ||
                (rank >= 1 && rank <= 5 ? stage < MortalStage.Initial || stage > MortalStage.Peak : stage != MortalStage.None))
                throw new ArgumentException("Invalid cultivation snapshot.");
            PlayerId = playerId; HomeRealmId = homeRealmId; Revision = revision; RulesVersion = rulesVersion;
            Rank = rank; Stage = stage; Aptitude = aptitude; Experience = experience; MainPathDaoMarks = daoMarks;
            HeavenlyTribulations = heavenly; GrandTribulations = grand; MyriadTribulations = myriad; Proofs = proofs;
        }
    }
    public sealed class CultivationRequest
    {
        public CultivationCommand Command { get; }
        public long RequestId { get; }
        public string OperationId { get; }
        public long ExpectedRevision { get; }
        public int RulesVersion { get; }
        public CultivationRequest(CultivationCommand command, long requestId, string operationId, long expectedRevision, int rulesVersion = CultivationRules.Version)
        { Command = command; RequestId = requestId; OperationId = operationId; ExpectedRevision = expectedRevision; RulesVersion = rulesVersion; }
        public bool SameOperation(CultivationRequest other) => other != null && Command == other.Command && OperationId == other.OperationId && ExpectedRevision == other.ExpectedRevision && RulesVersion == other.RulesVersion;
    }
    public sealed class CultivationResponse
    {
        public long RequestId { get; }
        public string OperationId { get; }
        public CultivationResult Result { get; }
        public bool Replayed { get; }
        public CultivationSnapshot Snapshot { get; }
        public CultivationResponse(long requestId, string operationId, CultivationResult result, bool replayed, CultivationSnapshot snapshot)
        { RequestId = requestId; OperationId = operationId; Result = result; Replayed = replayed; Snapshot = snapshot; }
    }
    public static class CultivationRules
    {
        public const int Version = 1;
        // Existing onboarding design, LOCAL balance v1. Not claimed as probabilities from the novel.
        public static Aptitude DrawAptitude(int bandRoll, int stepRoll, int seaRoll)
        {
            if (bandRoll < 0 || bandRoll >= 10000 || stepRoll < 0 || stepRoll >= 10 || seaRoll < 0 || seaRoll >= 20) throw new ArgumentOutOfRangeException();
            int band = bandRoll < 2000 ? 1 : bandRoll < 7000 ? 2 : bandRoll < 9500 ? 3 : 4;
            return new Aptitude((AptitudeGrade)band, band * 20 + seaRoll, band * 10 + stepRoll);
        }
        public static long RequiredExperience(CultivationSnapshot s) => s.Rank == 0 || s.Rank == 9 ? 0 :
            s.Rank <= 5 ? 100L * s.Rank * (int)s.Stage : 1000L * s.Rank;
        public static bool EvidenceSatisfied(CultivationSnapshot s)
        {
            if (s.Rank < 1 || s.Rank >= 9) return false;
            if (s.Rank <= 5 && s.Stage != MortalStage.Peak) return true;
            if (s.Rank < 5) return Has(s, CultivationProof.MortalTrial);
            if (s.Rank == 5) return Has(s, CultivationProof.Ascension);
            if (s.Rank == 6) return s.HeavenlyTribulations >= 3 && Has(s, CultivationProof.ImmortalSourceActive);
            if (s.Rank == 7) return s.GrandTribulations >= 3 && Has(s, CultivationProof.ImmortalSourceActive);
            return s.MyriadTribulations >= 3 && s.MainPathDaoMarks >= 300000 &&
                Has(s, CultivationProof.ImmortalSourceActive | CultivationProof.SupremeGrandmaster | CultivationProof.HeavenlySealBroken);
        }
        public static bool Has(CultivationSnapshot s, CultivationProof p) => (s.Proofs & p) == p;
        public static string RankName(CultivationSnapshot s) => s.Rank == 0 ? "尚未开窍" : RankName(s.Rank) + (s.Rank <= 5 ? " · " + StageName(s.Stage) : s.Rank == 9 ? " · 蛊尊" : " · 蛊仙");
        public static string RankName(int rank)
        { if (rank < 1 || rank > 9) throw new ArgumentOutOfRangeException(nameof(rank)); return new[] { "一转", "二转", "三转", "四转", "五转", "六转", "七转", "八转", "九转" }[rank - 1]; }
        public static string StageName(MortalStage stage) => new[] { "无小境界", "初阶", "中阶", "高阶", "巅峰" }[(int)stage];
        public static string GradeName(AptitudeGrade grade) => new[] { "未判定", "丁等", "丙等", "乙等", "甲等", "十绝（特殊）" }[(int)grade];
        public static string ApertureName(CultivationSnapshot s) => s.Rank == 0 ? "未开窍" : s.Rank <= 5 ?
            new[] { "", "光膜空窍", "水膜空窍", "石膜空窍", "晶膜空窍" }[(int)s.Stage] : s.Rank <= 7 ? "仙窍 · 福地" : "仙窍 · 洞天";
        public static string EnergyName(CultivationSnapshot s)
        {
            if (s.Rank == 0) return "尚无真元";
            if (s.Rank >= 6) return new[] { "青提仙元", "红枣仙元", "白荔仙元", "黄杏仙元" }[s.Rank - 6];
            string[] colors = { "翠绿", "苍绿", "深绿", "墨绿", "浅红", "绯红", "深红", "暗红", "淡银", "花银", "亮银", "雪银", "淡金", "亮金", "精金", "真金", "淡紫", "嫣紫", "深紫", "晶紫" };
            return colors[(s.Rank - 1) * 4 + (int)s.Stage - 1] + "真元";
        }
        public static string RequirementText(CultivationSnapshot s)
        {
            if (s.Rank == 0) return "由服务器完成一次随机开窍，进入一转初阶。";
            if (s.Rank == 9) return "已达九转上限；没有十转经验条。";
            if (s.Rank <= 5 && s.Stage != MortalStage.Peak) return "修炼经验达标后进一小阶。";
            if (s.Rank < 5) return "经验达标 + 当前转数的突破试炼凭据。";
            if (s.Rank == 5) return "经验达标 + 服务端验证完成的升仙凭据（碎窍、三气、灾劫）。";
            if (s.Rank == 6) return "经验达标 + 3 次天劫 + 仙窍本源正常。";
            if (s.Rank == 7) return "经验达标 + 3 次浩劫 + 仙窍本源正常。";
            return "经验达标 + 3 次万劫 + 30 万主修道痕 + 无上大宗师 + 突破天道封锁 + 本源正常。";
        }
    }
}
