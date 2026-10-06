using System.Diagnostics;
using Framework;
using Framework.Network;
using InsectSpace.BattleEconomy;
using InsectSpace.Cultivation;
using InsectSpace.Economy;
using InsectSpace.GuPaths;
using InsectSpace.Gameplay.GuPaths;
using InsectSpace.Gameplay.Economy;
using InsectSpace.Server.Economy;
using InsectSpace.Server.GuPaths;

static class GuPathTests
{
    private static readonly EconomyPrincipal P = new(1, "local-home", "local-world", 1);
    private static GuCatalog Catalog() => GuPathService.LoadLocalCatalog();
    private static CultivationSnapshot Growth(int rank = 5, long player = 1) => new(player, "local-home", rank, 1, rank, rank == 0 ? MortalStage.None : MortalStage.Initial,
        rank == 0 ? new Aptitude(AptitudeGrade.None, 0, 0) : new Aptitude(AptitudeGrade.Jia, 88, 40));
    private static GuPathService Service(bool grant = true)
    { var s = new GuPathService(Catalog()); s.CreateCharacter(P); if (grant) foreach (var g in s.Catalog.Entries) Eq(GuResult.Ok, s.GrantOwned(P, "fixture-" + g.Id, g.Id)); return s; }
    private static GuRequest Request(GuPathService s, params int[] ids) => new(GuCommand.Equip, 1, Guid.NewGuid().ToString("N"), s.Snapshot(P, Growth()).Revision, s.Catalog.Fingerprint, ids);
    public static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("gu.catalog-sourced-mortal-ranks-and-deterministic-combinations", Rules);
        yield return ("gu.ownership-rank-family-and-atomic-battle-gates", Authority);
        yield return ("gu.concurrent-idempotency-version-and-revision", Idempotency);
        yield return ("gu.reward-receipts-and-home-identity-isolation", Inventory);
        yield return ("gu.bounded-operation-ledger-does-not-forget", Capacity);
        yield return ("gu.protocol-roundtrip-offset-truncation-and-malformed", Wire);
        yield return ("gu.client-authority-correlation-reconnect-and-retry", Client);
        yield return ("gu.real-tcp-combination-rank-reconnect-and-kcp-battle-gate", Tcp);
    }
    private static void Rules()
    {
        var c = Catalog(); Eq(43, c.Entries.Count); Eq(42, c.Paths.Count); Check(GuPacketCodec.ValidHash(c.Fingerprint));
        Check(c.Entries.All(g => g.Rank >= 1 && g.Rank <= 5 && g.Source.Contains("原文 L") && g.PathBasis.Length > 0));
        Eq("1,2,3,4,5", string.Join(",", c.Entries.Select(g => g.Rank).Distinct().Order()));
        Eq(PathFormation.Empty, c.Evaluate(Array.Empty<int>()).Formation);
        Eq(PathFormation.Inclination, c.Evaluate(new[] { 1001 }).Formation);
        var moon = c.Evaluate(new[] { 1001, 1002 }); Eq(PathFormation.Formed, moon.Formation); Eq("月道", c.PathName(moon.DominantPath));
        var strength = c.Evaluate(new[] { 3004, 3005, 4003 }); Eq(PathFormation.Formed, strength.Formation); Eq("力道", c.PathName(strength.DominantPath));
        Eq(PathFormation.Inclination, c.Evaluate(new[] { 1003, 1004 }).Formation); // Two supports alone do not make a complete profile.
        Eq(PathFormation.Mixed, c.Evaluate(new[] { 1001, 1008 }).Formation); // Exact tie has no hidden ID winner.
        var wolf = c.Evaluate(new[] { 3009, 3010, 4006 }); Eq(PathFormation.Formed, wolf.Formation); Eq("奴道", c.PathName(wolf.DominantPath));
        var reverse = c.Evaluate(new[] { 4006, 3010, 3009 }); Eq(Signature(wolf), Signature(reverse));
        Throws<ArgumentException>(() => c.Evaluate(new[] { 1001, 2001 }));
        Throws<ArgumentException>(() => c.Evaluate(new[] { 2002, 5006 }));
        Throws<ArgumentException>(() => c.Evaluate(new[] { 3013, 4004 }));
        Throws<ArgumentException>(() => c.Evaluate(new[] { 1001, 1001 }));
        Throws<ArgumentException>(() => c.Evaluate(new[] { 999999 }));
        Throws<ArgumentException>(() => c.Evaluate(c.Entries.Take(7).Select(g => g.Id)));
    }
    private static void Authority()
    {
        var s = Service(false); var growth = Growth(1);
        Eq(GuResult.NotOwned, s.Handle(P, Request(s, 1001), growth).Result);
        foreach (int id in new[] { 1001, 1002, 2001 }) s.GrantOwned(P, "grant-" + id, id);
        Eq(GuResult.Ok, s.Handle(P, Request(s, 1001, 1002), growth).Result); var old = s.Snapshot(P, growth);
        Eq(GuResult.RankRequired, s.Handle(P, Request(s, 2001), growth).Result);
        Eq(GuResult.DuplicateFamily, s.Handle(P, Request(s, 1001, 2001), Growth()).Result);
        Eq(GuResult.DuplicateFamily, s.Handle(P, Request(s, 1001, 1001), growth).Result);
        Eq(GuResult.InvalidRequest, s.Handle(P, Request(s, 6001), growth).Result);
        var battle = Request(s); Eq(GuResult.BattleActive, s.Handle(P, battle, growth, true).Result);
        Eq(old.Revision, s.Snapshot(P, growth).Revision); Eq("1001,1002", string.Join(",", s.Snapshot(P, growth).Loadout));
        Eq(GuResult.BattleActive, s.Handle(P, battle, growth).Result); // Rejected operation stays rejected.
        Eq(GuResult.Ok, s.Handle(P, Request(s), growth).Result); Eq(0, s.Snapshot(P, growth).Loadout.Count);
        Eq(GuResult.RankRequired, s.Handle(P, Request(s, 1001), Growth(0)).Result);
    }
    private static void Idempotency()
    {
        var s = Service(); var r = Request(s, 1001, 1002); long before = s.Snapshot(P, Growth()).Revision;
        Parallel.For(0, 24, _ => Eq(GuResult.Ok, s.Handle(P, r, Growth()).Result)); Eq(before + 1, s.Snapshot(P, Growth()).Revision);
        Check(s.Handle(P, new GuRequest(r.Command, 9, r.OperationId, r.ExpectedRevision, r.CatalogHash, r.Loadout), Growth()).Replayed);
        Eq(GuResult.IdempotencyConflict, s.Handle(P, new GuRequest(r.Command, 9, r.OperationId, r.ExpectedRevision, r.CatalogHash, new[] { 1008 }), Growth()).Result);
        Eq(GuResult.RevisionConflict, s.Handle(P, new GuRequest(GuCommand.Equip, 1, "stale", before, r.CatalogHash, new[] { 1008 }), Growth()).Result);
        Eq(GuResult.CatalogMismatch, s.Handle(P, new GuRequest(GuCommand.Equip, 1, "wrong-hash", before + 1, new string('0', 64), new[] { 1008 }), Growth()).Result);
        Eq(GuResult.CatalogMismatch, s.Handle(P, new GuRequest(GuCommand.Equip, 1, "wrong-rules", before + 1, r.CatalogHash, new[] { 1008 }, 2), Growth()).Result);
        Eq(GuResult.Ok, s.Handle(P, Request(s, 1002, 1001), Growth()).Result); Eq(before + 1, s.Snapshot(P, Growth()).Revision);
    }
    private static void Inventory()
    {
        var s = Service(false); var other = new EconomyPrincipal(2, "local-home", "local-world", 1); s.CreateCharacter(other);
        Eq(GuResult.Ok, s.GrantOwned(P, "reward", 1001)); Eq(GuResult.Ok, s.GrantOwned(P, "reward", 1001));
        Eq(1L, s.Snapshot(P, Growth()).Revision); s.CreateCharacter(P); Eq(1, s.Snapshot(P, Growth()).Owned.Count);
        Eq(GuResult.IdempotencyConflict, s.GrantOwned(other, "reward", 1001)); Eq(0, s.Snapshot(other, Growth(5, 2)).Owned.Count);
        Eq(GuResult.IdempotencyConflict, s.GrantOwned(P, "reward", 1002)); Eq(GuResult.InvalidRequest, s.GrantOwned(P, "unknown", 6001));
        Throws<UnauthorizedAccessException>(() => s.Snapshot(P, Growth(5, 2)));
        Throws<UnauthorizedAccessException>(() => s.GrantOwned(new EconomyPrincipal(1, "other-home", "local-world", 1), "other", 1001));
        var copy = s.Snapshot(P, Growth()).Owned; Throws<NotSupportedException>(() => ((IList<int>)copy)[0] = 5001);
    }
    private static void Capacity()
    {
        var s = Service(false); var first = Request(s);
        Eq(GuResult.Ok, s.Handle(P, first, Growth()).Result);
        for (int i = 1; i < GuPathService.MaxRecords; i++) Eq(GuResult.Ok, s.Handle(P, Request(s), Growth()).Result);
        Eq(GuResult.CapacityExceeded, s.Handle(P, Request(s), Growth()).Result); Check(s.Handle(P, first, Growth()).Replayed);
        for (int i = 0; i < GuPathService.MaxRecords; i++) Eq(GuResult.Ok, s.GrantOwned(P, "grant" + i, 1001));
        Eq(GuResult.CapacityExceeded, s.GrantOwned(P, "overflow", 1002)); Eq(GuResult.Ok, s.GrantOwned(P, "grant0", 1001));
    }
    private static void Wire()
    {
        var s = Service(); var codec = new LocalPlayerPacketCodec(); var r = Request(s, 3009, 3010, 4006); var reply = s.Handle(P, r, Growth());
        foreach (var p in new[] { GuPacket.FromRequest(r), GuPacket.FromResponse(reply) })
        {
            Check(codec.Encode(p, out var bytes)); var padded = new byte[bytes.Length + 4]; bytes.CopyTo(padded, 2);
            var decoded = (GuPacket)codec.Decode(padded, 2, bytes.Length, out _); Check(decoded != null);
            if (decoded.Response != null) Eq(Signature(reply.Snapshot.Profile), Signature(decoded.Response.Snapshot.Profile));
            else Check(r.SameOperation(decoded.Request)); ReferencePool.Release(decoded);
            for (int n = 0; n < bytes.Length; n++) Check(codec.Decode(bytes, 0, n, out _) == null);
            Check(codec.Decode(bytes, -1, bytes.Length, out _) == null);
            var extra = new byte[bytes.Length + 1]; bytes.CopyTo(extra, 0); Check(codec.Decode(extra, 0, extra.Length, out _) == null);
            bytes[0] = 2; Check(codec.Decode(bytes, 0, bytes.Length, out _) == null); ReferencePool.Release(p);
        }
        Check(!GuPacketCodec.ValidRequest(new GuRequest((GuCommand)99, 1, "grant", 0, s.Catalog.Fingerprint)));
        Check(!GuPacketCodec.ValidRequest(new GuRequest(GuCommand.Equip, 1, "oversize", 0, s.Catalog.Fingerprint, Enumerable.Range(1, 7))));
        Check(codec.Decode(new byte[4097], 0, 4097, out _) == null);
    }
    private static void Client()
    {
        var s = Service(); GuRequest sent = null; var c = new GuPathClient(1, "local-home", s.Catalog, r => sent = r);
        c.TransportConnected(); c.Refresh(); var initial = s.Handle(P, sent, Growth());
        Check(!c.Receive(new GuResponse(sent.RequestId + 1, sent.OperationId, GuResult.Ok, false, initial.Snapshot))); Check(c.Receive(initial));
        int[] draft = { 1001, 1002 }; c.Equip(draft); draft[0] = 5001; Eq(1001, sent.Loadout[0]);
        var applied = s.Handle(P, sent, Growth()); string op = sent.OperationId; Eq(0, c.Snapshot.Loadout.Count);
        c.Tick(9); c.Disconnect("test"); c.TransportConnected(); c.Refresh(); Check(c.Receive(s.Handle(P, sent, Growth())));
        Throws<InvalidOperationException>(() => c.Equip(new[] { 1008 })); c.Retry(); Eq(op, sent.OperationId);
        var replay = s.Handle(P, sent, Growth()); Check(replay.Replayed); Check(c.Receive(replay)); Eq(applied.Snapshot.Revision, c.Snapshot.Revision);
        c.Refresh(); Check(!c.Receive(new GuResponse(sent.RequestId, sent.OperationId, GuResult.Ok, false, initial.Snapshot)));
        var state = replay.Snapshot;
        var fake = new GuSnapshot(1, "local-home", state.Revision, 1, state.CatalogHash, 5, 5, state.Owned, state.Loadout, s.Catalog.Evaluate(new[] { 1008 }));
        Check(!c.Receive(new GuResponse(sent.RequestId, sent.OperationId, GuResult.Ok, false, fake)));
        var mismatch = new GuSnapshot(1, "local-home", state.Revision, 1, new string('0', 64), 5, 5, state.Owned, state.Loadout, state.Profile);
        Check(!c.Receive(new GuResponse(sent.RequestId, sent.OperationId, GuResult.CatalogMismatch, false, mismatch))); Check(!c.Connected && c.Snapshot == null);
    }
    private static void Tcp()
    {
        GameFrameworkEntry.Shutdown(); using var host = new LocalEconomyHost(0);
        var manager = GameFrameworkEntry.GetModule<INetworkManager>(); _ = new InsectSpace.Network.DesktopChannelFactory(manager);
        using var tcp = new LocalEconomyTcpClient(manager, enableCultivation: true, guCatalog: Catalog()); var c = tcp.GuPaths;
        try
        {
            tcp.Connect(host.Port); Pump(() => tcp.Client.Ready && c.Ready && tcp.Cultivation.Ready, "connect");
            c.Equip(new[] { 1001, 1002 }); Pump(() => c.Ready, "rank gate"); Eq(GuResult.RankRequired, c.LastResponse.Result);
            tcp.Cultivation.Awaken(); Pump(() => tcp.Cultivation.Ready, "awaken");
            c.Refresh(); Pump(() => c.Ready, "growth refresh"); Eq(1, c.Snapshot.PlayerRank);
            c.Equip(new[] { 1001, 1002 }); Pump(() => c.Ready, "moon combo"); Eq(PathFormation.Formed, c.Snapshot.Profile.Formation);
            Eq("月道", c.Catalog.PathName(c.Snapshot.Profile.DominantPath)); long revision = c.Snapshot.Revision;
            tcp.BeginBattle(); Pump(() => tcp.Battle.Ready && tcp.Client.Ready, "battle admission");
            c.Equip(Array.Empty<int>()); Pump(() => c.Ready, "battle equipment lock"); Eq(GuResult.BattleActive, c.LastResponse.Result); Eq(revision, c.Snapshot.Revision);
            tcp.Battle.Send(ResourceAction.LeaveBattle); Pump(() => tcp.Battle.Finished && tcp.Client.Ready, "leave");
            c.Equip(new[] { 1001, 1008 }); Pump(() => c.Ready, "mixed combo"); Eq(PathFormation.Mixed, c.Snapshot.Profile.Formation);
            tcp.Connect(host.Port); Pump(() => tcp.Client.Ready && c.Ready && tcp.Cultivation.Ready, "reconnect");
            Eq("1001,1008", string.Join(",", c.Snapshot.Loadout)); Eq(new StoneAmounts(100, 10), tcp.Client.Snapshot.Wallet);
        }
        finally { tcp.Dispose(); GameFrameworkEntry.Shutdown(); }
        void Pump(Func<bool> done, string stage)
        {
            var timer = Stopwatch.StartNew(); double last = 0;
            while (!done() && timer.ElapsedMilliseconds < 10000)
            { double now = timer.Elapsed.TotalSeconds; float dt = (float)(now - last); last = now; host.Tick(dt); GameFrameworkEntry.Update(dt, dt); tcp.Tick(dt); Thread.Sleep(2); }
            if (!done()) throw new Exception(stage + ": " + c.Status);
        }
    }
    private static string Signature(PathProfile p) => p.Formation + ":" + p.DominantPath + ":" + p.Roles + ":" + string.Join(";", p.Scores.Select(s => $"{s.Path},{s.Score},{s.GuCount},{s.Roles}"));
    private static void Check(bool value) { if (!value) throw new Exception("Assertion failed."); }
    private static void Eq<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}."); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
}
