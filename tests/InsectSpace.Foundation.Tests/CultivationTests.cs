using System.Diagnostics;
using Framework;
using Framework.Network;
using InsectSpace.Cultivation;
using InsectSpace.Economy;
using InsectSpace.BattleEconomy;
using InsectSpace.Gameplay.Cultivation;
using InsectSpace.Gameplay.Economy;
using InsectSpace.Server.Cultivation;
using InsectSpace.Server.Economy;

static class CultivationTests
{
    private static readonly EconomyPrincipal P = new(1, "local-home", "local-world", 1);
    private sealed class FixedRandom : IAptitudeRandom
    {
        public int Calls;
        public int Next(int exclusiveMaximum) { Calls++; return 0; }
    }
    public static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("cultivation.aptitude-exact-weights-boundaries-and-catalog", Rules);
        yield return ("cultivation.once-only-awakening-concurrent-and-idempotent", Awakening);
        yield return ("cultivation.all-nine-ranks-and-authority-gates", Progression);
        yield return ("cultivation.trusted-receipts-overflow-and-identity-isolation", Receipts);
        yield return ("cultivation.protocol-roundtrip-and-malformed-rejection", Wire);
        yield return ("cultivation.client-correlation-and-uncertain-retry", Client);
        yield return ("cultivation.real-tcp-reconnect-and-battle-mutation-gate", Tcp);
    }
    private static CultivationService Create(out FixedRandom rng)
    { rng = new FixedRandom(); var s = new CultivationService(new InMemoryCultivationStore(), rng); s.CreateCharacter(P); return s; }
    private static CultivationRequest Request(CultivationService s, CultivationCommand command) => new(command, 1, Guid.NewGuid().ToString("N"), s.Snapshot(P).Revision);
    private static void Rules()
    {
        int[] counts = new int[5];
        for (int roll = 0; roll < 10000; roll++)
        {
            var a = CultivationRules.DrawAptitude(roll, roll % 10, roll % 20); counts[(int)a.Grade]++;
            Eq((int)a.Grade, a.SeaPercent / 20); Eq((int)a.Grade, a.StopStep / 10);
        }
        Eq("0,2000,5000,2500,500", string.Join(",", counts));
        Throws<ArgumentOutOfRangeException>(() => CultivationRules.DrawAptitude(10000, 0, 0));
        Throws<ArgumentException>(() => new Aptitude(AptitudeGrade.Jia, 100, 49));
        Throws<ArgumentException>(() => new Aptitude(AptitudeGrade.Bing, 40, 30));
        Eq(100, new Aptitude(AptitudeGrade.Extreme, 100, 50).SeaPercent);
        for (int rank = 1; rank <= 9; rank++)
        {
            for (int stage = rank <= 5 ? 1 : 0; stage <= (rank <= 5 ? 4 : 0); stage++)
            {
                var s = new CultivationSnapshot(1, "local-home", 1, 1, rank, (MortalStage)stage, new Aptitude(AptitudeGrade.Ding, 20, 10));
                Check(CultivationRules.RankName(s).Contains(CultivationRules.RankName(rank)));
                Check(CultivationRules.EnergyName(s).EndsWith(rank <= 5 ? "真元" : "仙元"));
            }
        }
        Throws<ArgumentException>(() => new CultivationSnapshot(1, "local-home", 0, 1, 6, MortalStage.Initial, new Aptitude(AptitudeGrade.Ding, 20, 10)));
        Throws<ArgumentException>(() => new CultivationSnapshot(1, "local-home", 0, 1, 10, MortalStage.None, new Aptitude(AptitudeGrade.Ding, 20, 10)));
    }
    private static void Awakening()
    {
        var service = Create(out var random);
        var request = Request(service, CultivationCommand.Awaken);
        Parallel.For(0, 32, _ => Eq(CultivationResult.Ok, service.Handle(P, request).Result));
        Eq(3, random.Calls); var original = service.Snapshot(P); Eq(1L, original.Revision);
        Eq(AptitudeGrade.Ding, original.Aptitude.Grade); Eq(20, original.Aptitude.SeaPercent); Eq(10, original.Aptitude.StopStep);
        var repeat = service.Handle(P, new CultivationRequest(CultivationCommand.Awaken, 99, request.OperationId, 0));
        Check(repeat.Replayed); Eq(CultivationResult.Ok, repeat.Result);
        Eq(CultivationResult.IdempotencyConflict, service.Handle(P, new CultivationRequest(CultivationCommand.Breakthrough, 2, request.OperationId, 0)).Result);
        Eq(CultivationResult.AlreadyAwakened, service.Handle(P, Request(service, CultivationCommand.Awaken)).Result);
        service.CreateCharacter(P); Eq(original, service.Snapshot(P)); Eq(3, random.Calls);
        Eq(CultivationResult.RuleMismatch, service.Handle(P, new CultivationRequest(CultivationCommand.Snapshot, 3, "version", 0, 2)).Result);
        Eq(CultivationResult.InvalidRequest, service.Handle(P, new CultivationRequest((CultivationCommand)99, 3, "forged", 0)).Result);
    }
    private static void Progression()
    {
        var service = Create(out _);
        Eq(CultivationResult.NotAwakened, service.Handle(P, Request(service, CultivationCommand.Breakthrough)).Result);
        Eq(CultivationResult.Ok, service.Handle(P, Request(service, CultivationCommand.Awaken)).Result);
        Eq(CultivationResult.ExperienceRequired, service.Handle(P, Request(service, CultivationCommand.Breakthrough)).Result);
        Eq(CultivationResult.Ok, service.GrantExperience(P, "quest-all", 100000));
        var aptitude = service.Snapshot(P).Aptitude;
        for (int rank = 1; rank <= 5; rank++)
        {
            for (int stage = 1; stage <= 3; stage++)
            {
                Eq(rank, service.Snapshot(P).Rank); Eq((MortalStage)stage, service.Snapshot(P).Stage); Advance();
            }
            Eq(CultivationResult.EvidenceRequired, service.Handle(P, Request(service, CultivationCommand.Breakthrough)).Result);
            Evidence(rank < 5 ? CultivationEvidence.MortalTrial : CultivationEvidence.Ascension);
            Advance(); Eq(rank + 1, service.Snapshot(P).Rank);
            Check(!CultivationRules.Has(service.Snapshot(P), CultivationProof.MortalTrial));
        }
        Eq(MortalStage.None, service.Snapshot(P).Stage); Eq("青提仙元", CultivationRules.EnergyName(service.Snapshot(P)));
        for (int rank = 6; rank <= 8; rank++)
        {
            for (int i = 0; i < 3; i++)
            {
                Eq(CultivationResult.EvidenceRequired, service.Handle(P, Request(service, CultivationCommand.Breakthrough)).Result);
                Evidence(rank == 6 ? CultivationEvidence.HeavenlyTribulation : rank == 7 ? CultivationEvidence.GrandTribulation : CultivationEvidence.MyriadTribulation);
            }
            if (rank == 8)
            {
                Eq(CultivationResult.EvidenceRequired, service.Handle(P, Request(service, CultivationCommand.Breakthrough)).Result);
                Evidence(CultivationEvidence.MainPathDaoMarks, 299999); Evidence(CultivationEvidence.SupremeGrandmaster); Evidence(CultivationEvidence.HeavenlySealBroken);
                Eq(CultivationResult.EvidenceRequired, service.Handle(P, Request(service, CultivationCommand.Breakthrough)).Result);
                Evidence(CultivationEvidence.MainPathDaoMarks, 1);
            }
            Advance(); Eq(rank + 1, service.Snapshot(P).Rank);
        }
        Eq(aptitude, service.Snapshot(P).Aptitude); Eq("黄杏仙元", CultivationRules.EnergyName(service.Snapshot(P)));
        Eq(CultivationResult.MaximumRank, service.Handle(P, Request(service, CultivationCommand.Breakthrough)).Result);
        void Advance()
        {
            var old = service.Snapshot(P); var request = Request(service, CultivationCommand.Breakthrough);
            Eq(CultivationResult.Ok, service.Handle(P, request).Result);
            Eq(old.Experience - CultivationRules.RequiredExperience(old), service.Snapshot(P).Experience);
            long revision = service.Snapshot(P).Revision;
            Check(service.Handle(P, request).Replayed); Eq(revision, service.Snapshot(P).Revision);
        }
        void Evidence(CultivationEvidence kind, long amount = 1)
        { var s = service.Snapshot(P); Eq(CultivationResult.Ok, service.RecordEvidence(P, Guid.NewGuid().ToString("N"), s.Rank, s.Stage, kind, amount)); }
    }
    private static void Receipts()
    {
        var service = Create(out _); var other = new EconomyPrincipal(2, "local-home", "local-world", 1); service.CreateCharacter(other);
        Eq(CultivationResult.Ok, service.GrantExperience(P, "quest", long.MaxValue));
        Eq(CultivationResult.Ok, service.GrantExperience(P, "quest", long.MaxValue));
        Eq(CultivationResult.IdempotencyConflict, service.GrantExperience(other, "quest", long.MaxValue));
        Eq(CultivationResult.CapacityExceeded, service.GrantExperience(P, "overflow", 1));
        Eq(long.MaxValue, service.Snapshot(P).Experience); Eq(0L, service.Snapshot(other).Experience);
        Eq(CultivationResult.Ok, service.Handle(P, Request(service, CultivationCommand.Awaken)).Result);
        Eq(CultivationResult.InvalidRequest, service.RecordEvidence(P, "fake-tribulation", 1, MortalStage.Initial, CultivationEvidence.MyriadTribulation));
        Eq(CultivationResult.RevisionConflict, service.RecordEvidence(P, "stale-stage", 2, MortalStage.Initial, CultivationEvidence.MortalTrial));
        Throws<UnauthorizedAccessException>(() => service.Snapshot(new EconomyPrincipal(1, "other-home", "local-world", 1)));
        var request = Request(service, CultivationCommand.Breakthrough);
        Eq(CultivationResult.BattleActive, service.Handle(P, request, true).Result);
        Eq(MortalStage.Initial, service.Snapshot(P).Stage);
        Eq(CultivationResult.BattleActive, service.Handle(P, request).Result); // Stable failed-operation replay.
    }
    private static void Wire()
    {
        var service = Create(out _); service.Handle(P, Request(service, CultivationCommand.Awaken));
        var codec = new LocalPlayerPacketCodec();
        var packets = new Packet[] { CultivationPacket.FromRequest(Request(service, CultivationCommand.Breakthrough)),
            CultivationPacket.FromResponse(new CultivationResponse(9, "result", CultivationResult.Ok, false, service.Snapshot(P))) };
        foreach (var packet in packets)
        {
            Check(codec.Encode(packet, out var bytes));
            var padded = new byte[bytes.Length + 6]; Array.Copy(bytes, 0, padded, 3, bytes.Length);
            var decoded = (CultivationPacket)codec.Decode(padded, 3, bytes.Length, out _); Check(decoded != null);
            if (decoded.Response != null) Eq(20, decoded.Response.Snapshot.Aptitude.SeaPercent);
            ReferencePool.Release(decoded);
            for (int size = 0; size < bytes.Length; size++) Check(codec.Decode(bytes, 0, size, out _) == null);
            Check(codec.Decode(bytes, -1, bytes.Length, out _) == null);
            var extra = new byte[bytes.Length + 1]; bytes.CopyTo(extra, 0); Check(codec.Decode(extra, 0, extra.Length, out _) == null);
            bytes[0] = 2; Check(codec.Decode(bytes, 0, bytes.Length, out _) == null); ReferencePool.Release(packet);
        }
        Check(codec.Decode(new byte[1025], 0, 1025, out _) == null);
        var invalid = CultivationPacket.FromRequest(new CultivationRequest((CultivationCommand)10, 1, "grant-100", 0));
        Check(!codec.Encode(invalid, out _)); ReferencePool.Release(invalid);
    }
    private static void Client()
    {
        var service = Create(out _); CultivationRequest request = null;
        var client = new CultivationClient(1, "local-home", r => request = r);
        client.TransportConnected(); client.Refresh(); var initial = service.Handle(P, request);
        Check(!client.Receive(new CultivationResponse(request.RequestId + 1, request.OperationId, CultivationResult.Ok, false, initial.Snapshot)));
        Check(client.Receive(initial)); client.Awaken(); var unknown = service.Handle(P, request);
        Eq(0, client.Snapshot.Rank); string op = request.OperationId;
        client.Tick(9); client.Disconnect("test"); client.TransportConnected(); client.Refresh(); Check(client.Receive(service.Handle(P, request)));
        Throws<InvalidOperationException>(() => client.Breakthrough());
        client.Retry(); Eq(op, request.OperationId); var replay = service.Handle(P, request); Check(replay.Replayed); Check(client.Receive(replay));
        Eq(unknown.Snapshot.Aptitude, client.Snapshot.Aptitude); Check(client.RetryableRequest == null);
        client.Refresh(); var foreign = new CultivationSnapshot(2, "local-home", 3, 1, 1, MortalStage.Initial, unknown.Snapshot.Aptitude);
        Check(!client.Receive(new CultivationResponse(request.RequestId, request.OperationId, CultivationResult.Ok, false, foreign)));
        Check(!client.Receive(new CultivationResponse(request.RequestId, request.OperationId, CultivationResult.Ok, false, initial.Snapshot)));
    }
    private static void Tcp()
    {
        GameFrameworkEntry.Shutdown(); using var host = new LocalEconomyHost(0);
        var manager = GameFrameworkEntry.GetModule<INetworkManager>(); _ = new InsectSpace.Network.DesktopChannelFactory(manager);
        using var tcp = new LocalEconomyTcpClient(manager, enableCultivation: true);
        try
        {
            tcp.Connect(host.Port); Pump(() => tcp.Client.Ready && tcp.Cultivation.Ready, "snapshots");
            Eq(0, tcp.Cultivation.Snapshot.Rank); tcp.Cultivation.Awaken(); Pump(() => tcp.Cultivation.Ready, "awakening");
            var apt = tcp.Cultivation.Snapshot.Aptitude;
            Eq(host.Cultivation.Snapshot(host.Principal).Aptitude.Grade, apt.Grade);
            tcp.Cultivation.Awaken(); Pump(() => tcp.Cultivation.Ready, "reject reroll"); Eq(CultivationResult.AlreadyAwakened, tcp.Cultivation.LastResponse.Result);
            host.Cultivation.GrantExperience(host.Principal, "tcp-quest", 1000);
            tcp.Cultivation.Refresh(); Pump(() => tcp.Cultivation.Ready, "XP snapshot");
            tcp.BeginBattle(); Pump(() => tcp.Battle.Ready && tcp.Client.Ready, "battle");
            tcp.Cultivation.Breakthrough(); Pump(() => tcp.Cultivation.Ready, "battle gate"); Eq(CultivationResult.BattleActive, tcp.Cultivation.LastResponse.Result);
            tcp.Battle.Send(ResourceAction.LeaveBattle); Pump(() => tcp.Battle.Finished && tcp.Client.Ready, "settlement");
            tcp.Cultivation.Breakthrough(); Pump(() => tcp.Cultivation.Ready, "promotion"); Eq(MortalStage.Middle, tcp.Cultivation.Snapshot.Stage);
            tcp.Connect(host.Port); Pump(() => tcp.Client.Ready && tcp.Cultivation.Ready, "reconnect");
            Eq(apt.Grade, tcp.Cultivation.Snapshot.Aptitude.Grade); Eq(apt.SeaPercent, tcp.Cultivation.Snapshot.Aptitude.SeaPercent); Eq(apt.StopStep, tcp.Cultivation.Snapshot.Aptitude.StopStep);
            Eq(MortalStage.Middle, tcp.Cultivation.Snapshot.Stage); Eq(new StoneAmounts(100, 10), tcp.Client.Snapshot.Wallet);
        }
        finally { tcp.Dispose(); GameFrameworkEntry.Shutdown(); }
        void Pump(Func<bool> done, string stage)
        {
            var timer = Stopwatch.StartNew(); double last = 0;
            while (!done() && timer.ElapsedMilliseconds < 10000)
            { double now = timer.Elapsed.TotalSeconds; float dt = (float)(now - last); last = now; host.Tick(dt); GameFrameworkEntry.Update(dt, dt); tcp.Tick(dt); Thread.Sleep(2); }
            if (!done()) throw new Exception(stage + ": " + tcp.Cultivation.Status);
        }
    }
    private static void Check(bool value) { if (!value) throw new Exception("Assertion failed."); }
    private static void Eq<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}."); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
}
