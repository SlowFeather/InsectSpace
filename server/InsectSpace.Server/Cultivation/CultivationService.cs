using System.Security.Cryptography;
using InsectSpace.Cultivation;
using InsectSpace.Economy;
using InsectSpace.Server.Economy;

namespace InsectSpace.Server.Cultivation;

// Entropy is used only for server-side character creation, never authoritative battle simulation.
public interface IAptitudeRandom { int Next(int exclusiveMaximum); }
public sealed class ServerAptitudeRandom : IAptitudeRandom
{ public int Next(int exclusiveMaximum) => RandomNumberGenerator.GetInt32(exclusiveMaximum); }

// LOCAL DEVELOPMENT: lifetime is this process. No claimed database or durable character-slot history.
public sealed class InMemoryCultivationStore
{
    internal readonly object Gate = new();
    internal sealed class Entry
    {
        public CultivationSnapshot State;
        public readonly Dictionary<string, (CultivationRequest Request, CultivationResult Result)> Operations = new();
    }
    internal readonly Dictionary<(long Player, string Home), Entry> Characters = new();
    internal readonly Dictionary<string, (long Player, string Home, int Rank, MortalStage Stage, CultivationEvidence Kind, long Amount)> Receipts = new();
}

public sealed class CultivationService
{
    public const int MaxRecords = 4096;
    private readonly InMemoryCultivationStore store;
    private readonly IAptitudeRandom random;
    public CultivationService(InMemoryCultivationStore store, IAptitudeRandom random)
    { this.store = store ?? throw new ArgumentNullException(nameof(store)); this.random = random ?? throw new ArgumentNullException(nameof(random)); }
    public void CreateCharacter(EconomyPrincipal p)
    {
        if (p == null || p.PlayerId <= 0 || !EconomyPacketCodec.ValidId(p.HomeRealmId)) throw new ArgumentException("Invalid server identity.");
        lock (store.Gate)
        {
            if (store.Characters.ContainsKey((p.PlayerId, p.HomeRealmId))) return; // Creation retry never resets aptitude.
            if (store.Characters.Count >= MaxRecords) throw new InvalidOperationException("Local character capacity reached.");
            store.Characters.Add((p.PlayerId, p.HomeRealmId), new InMemoryCultivationStore.Entry
            { State = new CultivationSnapshot(p.PlayerId, p.HomeRealmId, 0, CultivationRules.Version, 0, MortalStage.None, new Aptitude(AptitudeGrade.None, 0, 0)) });
        }
    }
    public CultivationSnapshot Snapshot(EconomyPrincipal p) { lock (store.Gate) return Character(p).State; }
    // battleActive is supplied by the single host thread's authority context, never by the packet.
    public CultivationResponse Handle(EconomyPrincipal p, CultivationRequest request, bool battleActive = false)
    {
        lock (store.Gate)
        {
            var entry = Character(p); var s = entry.State;
            if (!CultivationPacketCodec.ValidRequest(request)) return Reply(entry, request, CultivationResult.InvalidRequest);
            if (request.RulesVersion != CultivationRules.Version) return Reply(entry, request, CultivationResult.RuleMismatch);
            if (request.Command == CultivationCommand.Snapshot) return Reply(entry, request, CultivationResult.Ok);
            if (entry.Operations.TryGetValue(request.OperationId, out var old))
                return Reply(entry, request, old.Request.SameOperation(request) ? old.Result : CultivationResult.IdempotencyConflict, old.Request.SameOperation(request));
            if (entry.Operations.Count >= MaxRecords || s.Revision == long.MaxValue) return Reply(entry, request, CultivationResult.CapacityExceeded);
            var result = request.ExpectedRevision != s.Revision ? CultivationResult.RevisionConflict : battleActive ? CultivationResult.BattleActive : Mutate(entry, request.Command);
            entry.Operations.Add(request.OperationId, (request, result));
            return Reply(entry, request, result);
        }
    }
    private CultivationResult Mutate(InMemoryCultivationStore.Entry entry, CultivationCommand command)
    {
        var s = entry.State;
        if (command == CultivationCommand.Awaken)
        {
            if (s.Awakened) return CultivationResult.AlreadyAwakened;
            var aptitude = CultivationRules.DrawAptitude(random.Next(10000), random.Next(10), random.Next(20));
            entry.State = Copy(s, rank: 1, stage: MortalStage.Initial, aptitude: aptitude); return CultivationResult.Ok;
        }
        if (!s.Awakened) return CultivationResult.NotAwakened;
        if (s.Rank == 9) return CultivationResult.MaximumRank;
        long required = CultivationRules.RequiredExperience(s);
        if (s.Experience < required) return CultivationResult.ExperienceRequired;
        if (!CultivationRules.EvidenceSatisfied(s)) return CultivationResult.EvidenceRequired;
        int rank = s.Rank; var stage = s.Stage; var proofs = s.Proofs;
        if (rank <= 5 && stage < MortalStage.Peak) stage++;
        else
        {
            rank++; stage = rank <= 5 ? MortalStage.Initial : MortalStage.None;
            proofs &= ~(CultivationProof.MortalTrial | CultivationProof.Ascension);
            if (rank == 6) proofs |= CultivationProof.ImmortalSourceActive;
        }
        entry.State = Copy(s, rank: rank, stage: stage, experience: s.Experience - required, proofs: proofs);
        return CultivationResult.Ok;
    }
    // Trusted reward API; no client opcode carries experience, seed, grade, target rank or proof.
    public CultivationResult GrantExperience(EconomyPrincipal p, string eventId, long amount)
    {
        if (amount <= 0 || !EconomyPacketCodec.ValidId(eventId)) return CultivationResult.InvalidRequest;
        lock (store.Gate)
        {
            var entry = Character(p);
            var receipt = (p.PlayerId, p.HomeRealmId, 0, MortalStage.None, (CultivationEvidence)0, amount);
            if (store.Receipts.TryGetValue(eventId, out var old)) return old == receipt ? CultivationResult.Ok : CultivationResult.IdempotencyConflict;
            if (store.Receipts.Count >= MaxRecords || entry.State.Revision == long.MaxValue) return CultivationResult.CapacityExceeded;
            try
            {
                var next = Copy(entry.State, experience: checked(entry.State.Experience + amount));
                store.Receipts.Add(eventId, receipt); entry.State = next; return CultivationResult.Ok;
            }
            catch (OverflowException) { return CultivationResult.CapacityExceeded; }
        }
    }
    // Called only after a server challenge/reward service verifies the event for this exact stage.
    public CultivationResult RecordEvidence(EconomyPrincipal p, string eventId, int expectedRank, MortalStage expectedStage, CultivationEvidence kind, long amount = 1)
    {
        if (!EconomyPacketCodec.ValidId(eventId) || amount <= 0 || kind < CultivationEvidence.MortalTrial || kind > CultivationEvidence.HeavenlySealBroken) return CultivationResult.InvalidRequest;
        lock (store.Gate)
        {
            var entry = Character(p); var s = entry.State;
            var receipt = (p.PlayerId, p.HomeRealmId, expectedRank, expectedStage, kind, amount);
            if (store.Receipts.TryGetValue(eventId, out var old)) return old == receipt ? CultivationResult.Ok : CultivationResult.IdempotencyConflict;
            if (s.Rank != expectedRank || s.Stage != expectedStage) return CultivationResult.RevisionConflict;
            bool valid = kind == CultivationEvidence.MortalTrial ? s.Rank >= 1 && s.Rank <= 4 && s.Stage == MortalStage.Peak && amount == 1 :
                kind == CultivationEvidence.Ascension ? s.Rank == 5 && s.Stage == MortalStage.Peak && amount == 1 :
                kind == CultivationEvidence.HeavenlyTribulation ? s.Rank == 6 && amount == 1 :
                kind == CultivationEvidence.GrandTribulation ? s.Rank == 7 && amount == 1 :
                kind == CultivationEvidence.MyriadTribulation ? s.Rank == 8 && amount == 1 :
                kind == CultivationEvidence.MainPathDaoMarks ? s.Rank >= 6 : s.Rank == 8 && amount == 1;
            if (!valid) return CultivationResult.InvalidRequest;
            if (store.Receipts.Count >= MaxRecords || s.Revision == long.MaxValue) return CultivationResult.CapacityExceeded;
            try
            {
                var proofs = s.Proofs; int heavenly = s.HeavenlyTribulations, grand = s.GrandTribulations, myriad = s.MyriadTribulations; long marks = s.MainPathDaoMarks;
                switch (kind)
                {
                    case CultivationEvidence.MortalTrial: proofs |= CultivationProof.MortalTrial; break;
                    case CultivationEvidence.Ascension: proofs |= CultivationProof.Ascension; break;
                    case CultivationEvidence.HeavenlyTribulation: heavenly = checked(heavenly + 1); break;
                    case CultivationEvidence.GrandTribulation: grand = checked(grand + 1); break;
                    case CultivationEvidence.MyriadTribulation: myriad = checked(myriad + 1); break;
                    case CultivationEvidence.MainPathDaoMarks: marks = checked(marks + amount); break;
                    case CultivationEvidence.SupremeGrandmaster: proofs |= CultivationProof.SupremeGrandmaster; break;
                    case CultivationEvidence.HeavenlySealBroken: proofs |= CultivationProof.HeavenlySealBroken; break;
                }
                var next = Copy(s, daoMarks: marks, heavenly: heavenly, grand: grand, myriad: myriad, proofs: proofs);
                store.Receipts.Add(eventId, receipt); entry.State = next; return CultivationResult.Ok;
            }
            catch (OverflowException) { return CultivationResult.CapacityExceeded; }
        }
    }
    private static CultivationSnapshot Copy(CultivationSnapshot s, int? rank = null, MortalStage? stage = null, Aptitude aptitude = null,
        long? experience = null, long? daoMarks = null, int? heavenly = null, int? grand = null, int? myriad = null, CultivationProof? proofs = null) =>
        new(s.PlayerId, s.HomeRealmId, checked(s.Revision + 1), s.RulesVersion, rank ?? s.Rank, stage ?? s.Stage, aptitude ?? s.Aptitude,
            experience ?? s.Experience, daoMarks ?? s.MainPathDaoMarks, heavenly ?? s.HeavenlyTribulations, grand ?? s.GrandTribulations, myriad ?? s.MyriadTribulations, proofs ?? s.Proofs);
    private InMemoryCultivationStore.Entry Character(EconomyPrincipal p)
    {
        if (p == null || !store.Characters.TryGetValue((p.PlayerId, p.HomeRealmId), out var entry)) throw new UnauthorizedAccessException("No server-bound cultivation identity.");
        return entry;
    }
    private static CultivationResponse Reply(InMemoryCultivationStore.Entry e, CultivationRequest r, CultivationResult result, bool replay = false) =>
        new(r?.RequestId ?? 0, r?.OperationId ?? "", result, replay, e.State);
}
