using Framework;
using Framework.Network;
using System.Diagnostics;
using System.IO;
using InsectSpace.Gameplay.Economy;
using InsectSpace.Cultivation;
using InsectSpace.Economy;
using InsectSpace.Gameplay.GuWorkshop;
using InsectSpace.GuWorkshop;
using InsectSpace.Server.Economy;
using InsectSpace.Server.GuPaths;
using InsectSpace.Server.GuWorkshop;
using Luban;

static class WorkshopTests
{
    private static readonly EconomyPrincipal P = new(1, "local-home", "local-world", 1);
    private sealed class Clock : IWorkshopClock { public long Value = 100; public long Seconds => Value; }
    private sealed class Random : IWorkshopRandom
    {
        public int Value, Calls;
        public int Next(int max) { Calls++; return Value % max; }
    }
    private sealed class Fixture
    {
        public readonly Clock Clock = new(); public readonly Random Random = new();
        public readonly EconomyService Economy = new(new InMemoryEconomyStore(), Array.Empty<WorldActionPrice>());
        public readonly WorkshopService Service;
        public int Rank = 2;
        public Fixture(long money = 100)
        {
            Economy.CreateAccount(P, 100, 100); if (money > 0) Economy.GrantReward(P, RewardSource.Activity, "fixture-money", money);
            Service = new WorkshopService(Economy, Catalog(), Clock, Random); Service.CreateCharacter(P);
        }
        public CultivationSnapshot Growth => new(1, "local-home", 1, 1, Rank, MortalStage.Initial, new Aptitude(AptitudeGrade.Jia, 88, 40));
        public WorkshopSnapshot State => Service.Snapshot(P, Growth);
        public WorkshopRequest Request(WorkshopCommand command, int target = 0, params long[] ids) => new(command, 1, Guid.NewGuid().ToString("N"), State.Revision, State.EconomyRevision, Service.Catalog.Fingerprint, target, ids);
        public WorkshopResponse Do(WorkshopCommand command, int target = 0, params long[] ids) => Service.Handle(P, Request(command, target, ids), Growth);
        public long Buy(int offer) { var r = Do(WorkshopCommand.Buy, offer); Eq(WorkshopResult.Ok, r.Result); return r.ProducedInstance; }
        public void Refine(long id) => Eq(WorkshopResult.Ok, Do(WorkshopCommand.Refine, 0, id).Result);
        public long[] Ingredients()
        { var ids = new[] { Buy(1), Buy(2), Buy(2) }; foreach (long id in ids) Refine(id); Buy(3); return ids; }
    }
    // TEST-ONLY bytes. These deliberately small time/cost/probability values are NOT live game configuration.
    private static WorkshopCatalog Catalog()
    {
        var data = new Dictionary<string, byte[]>();
        var b = new ByteBuf(); b.WriteSize(1); Row(b, 1, "test-material", "fixture"); data["tbitem"] = b.CopyData();
        b = new ByteBuf(); b.WriteSize(3); Row(b, 1, 1001, 0, 1, 3); Row(b, 2, 1002, 0, 1, 2); Row(b, 3, 0, 1, 10, 1); data["tboffer"] = b.CopyData();
        b = new ByteBuf(); b.WriteSize(3); foreach (int id in new[] {1001,1002,2001}) Row(b, id, 1, 1, 10, 1, 5000, 2500, "TEST ONLY"); data["tbcare"] = b.CopyData();
        b = new ByteBuf(); b.WriteSize(1); Row(b, 1, "test-recipe"); b.WriteSize(2); Row(b, 1001, 1, 1002, 2, 2001, 2);
        b.WriteSize(1); Row(b, 1, 2, 4, 5000, 2500, "TEST ONLY"); data["tbrecipe"] = b.CopyData();
        return new WorkshopCatalog(GuPathService.LoadLocalCatalog(), name => data[name]);
    }
    private static void Row(ByteBuf b, params object[] values) { foreach (var value in values) { if (value is int number) b.WriteInt(number); else b.WriteString((string)value); } }
    public static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("workshop.empty-config-fails-closed", EmptyConfig);
        yield return ("workshop.purchase-instances-wallet-and-atomic-rejections", Purchase);
        yield return ("workshop.feed-dormancy-offline-and-time-regression", Feeding);
        yield return ("workshop.single-refinement-three-outcomes-and-replay", Refinement);
        yield return ("workshop.fusion-success-and-failure-costs", Fusion);
        yield return ("workshop.rank-material-equipment-and-battle-guards", Gates);
        yield return ("workshop.concurrent-receipts-revision-and-capacity", Idempotency);
        yield return ("workshop.versioned-wire-truncation-and-offsets", Wire);
        yield return ("workshop.client-reconnect-preserves-uncertain-operation", Client);
        yield return ("workshop.real-tcp-purchase-refine-dormancy-reconnect-and-kcp-lock", Tcp);
    }
    private static void EmptyConfig()
    {
        // Reject an empty catalog regardless of whether the team has since populated live tables.
        var bytes = new ByteBuf(); bytes.WriteSize(0);
        try { _ = new WorkshopCatalog(GuPathService.LoadLocalCatalog(), _ => bytes.CopyData()); throw new Exception("Expected empty workshop tables to be rejected."); }
        catch (InvalidDataException error) { Check(error.Message.Contains("尚未启用")); }
    }
    private static void Purchase()
    {
        var f = new Fixture(); long a = f.Buy(2), b = f.Buy(2); Check(a != b); Eq(2, f.State.Inventory.Count); Eq(96L, f.State.Wallet.YuanShi);
        Eq(WorkshopResult.NotRefined, f.Do(WorkshopCommand.Equip, 0, a).Result);
        f.Service.CreateCharacter(P); Eq(2, f.State.Inventory.Count);
        var poor = new Fixture(1); var before = poor.State; var response = poor.Do(WorkshopCommand.Buy, 1);
        Eq(WorkshopResult.InsufficientFunds, response.Result); Eq(0L, response.ProducedInstance); Eq(0, poor.State.Inventory.Count); Eq(before.Revision, poor.State.Revision); Eq(1L, poor.State.Wallet.YuanShi);
        Eq(WorkshopResult.InvalidRequest, f.Do(WorkshopCommand.Buy, 999).Result);
        var other = new EconomyPrincipal(1, "other-home", "local-world", 1);
        Throws<UnauthorizedAccessException>(() => f.Service.Snapshot(other, f.Growth));
    }
    private static void Feeding()
    {
        var f = new Fixture(); long id = f.Buy(1); f.Refine(id); f.Buy(3); Eq(WorkshopResult.Ok, f.Do(WorkshopCommand.Equip, 0, id).Result);
        f.Clock.Value = 110; Check(f.State.Inventory[0].DormantAt(f.State.ServerSeconds));
        Eq(InsectSpace.GuPaths.PathFormation.Empty, f.Service.Catalog.ActiveProfile(f.State).Formation);
        Eq(WorkshopResult.Dormant, f.Do(WorkshopCommand.Equip, 0, id).Result);
        var before = f.State; Eq(WorkshopResult.MissingMaterials, f.Do(WorkshopCommand.Feed, 99, id).Result); Eq(before.Revision, f.State.Revision);
        Eq(WorkshopResult.Ok, f.Do(WorkshopCommand.Feed, 1, id).Result); Eq(120L, f.State.Inventory[0].FedUntil); Eq(9, f.State.Materials[0].Count);
        Eq(InsectSpace.GuPaths.PathFormation.Inclination, f.Service.Catalog.ActiveProfile(f.State).Formation);
        Eq(WorkshopResult.AlreadyFed, f.Do(WorkshopCommand.Feed, 1, id).Result);
        f.Clock.Value = 90; Eq(110L, f.State.ServerSeconds); Eq(120L, f.State.Inventory[0].FedUntil);
        f.Clock.Value = 100000; Eq(1, f.State.Inventory.Count); Check(f.State.Inventory[0].DormantAt(f.State.ServerSeconds));
    }
    private static void Refinement()
    {
        foreach (int roll in new[] {0, 5000, 9999})
        {
            var f = new Fixture(); long id = f.Buy(1); f.Random.Value = roll; var request = f.Request(WorkshopCommand.Refine, 0, id);
            var r = f.Service.Handle(P, request, f.Growth); var expected = roll == 0 ? WorkshopResult.Ok : roll == 5000 ? WorkshopResult.FailedPreserved : WorkshopResult.FailedDestroyed;
            Eq(expected, r.Result); Eq(96L, f.State.Wallet.YuanShi); Eq(roll == 9999 ? 0 : 1, f.State.Inventory.Count);
            if (roll != 9999) Eq(roll == 0, f.State.Inventory[0].Refined);
            var again = f.Service.Handle(P, request, f.Growth); Check(again.Replayed); Eq(expected, again.Result); Eq(1, f.Random.Calls); Eq(96L, f.State.Wallet.YuanShi);
        }
        var counts = new Dictionary<WorkshopResult,int>();
        for (int roll = 0; roll < 10000; roll++) { var r = WorkshopCatalog.Outcome(roll, 5000, 2500); counts[r] = counts.GetValueOrDefault(r) + 1; }
        Eq(5000, counts[WorkshopResult.Ok]); Eq(2500, counts[WorkshopResult.FailedPreserved]); Eq(2500, counts[WorkshopResult.FailedDestroyed]);
    }
    private static void Fusion()
    {
        foreach (int roll in new[] {0,5000,9999})
        {
            var f = new Fixture(); var ids = f.Ingredients(); f.Random.Value = roll; var request = f.Request(WorkshopCommand.Fuse, 1, ids); var before = f.State;
            var r = f.Service.Handle(P, request, f.Growth);
            Eq(before.Wallet.YuanShi - 4, f.State.Wallet.YuanShi); Eq(8, f.State.Materials[0].Count);
            Eq(roll == 0 ? 1 : roll == 5000 ? 3 : 2, f.State.Inventory.Count);
            if (roll == 0) { Eq(2001, f.State.Inventory[0].DefinitionId); Check(f.State.Inventory[0].Refined); Eq(3, r.RemovedInstances.Count); }
            if (roll == 9999) { Eq(1, r.RemovedInstances.Count); Check(ids.Contains(r.RemovedInstances[0])); }
            int calls = f.Random.Calls; Check(f.Service.Handle(P, request, f.Growth).Replayed); Eq(calls, f.Random.Calls); Eq(before.Wallet.YuanShi - 4, f.State.Wallet.YuanShi);
        }
    }
    private static void Gates()
    {
        var f = new Fixture(); var ids = f.Ingredients(); var before = f.State;
        Eq(WorkshopResult.InvalidRecipe, f.Do(WorkshopCommand.Fuse, 1, ids.Take(2).ToArray()).Result);
        Eq(WorkshopResult.InvalidRequest, f.Do(WorkshopCommand.Fuse, 1, ids[0], ids[1], ids[1]).Result);
        f.Rank = 1; Eq(WorkshopResult.RankRequired, f.Do(WorkshopCommand.Fuse, 1, ids).Result); f.Rank = 2;
        Eq(before.Revision, f.State.Revision); Eq(before.Wallet.YuanShi, f.State.Wallet.YuanShi);
        Eq(WorkshopResult.DuplicateFamily, f.Do(WorkshopCommand.Equip, 0, ids[1], ids[2]).Result);
        Eq(WorkshopResult.Ok, f.Do(WorkshopCommand.Equip, 0, ids[0], ids[1]).Result);
        Eq(WorkshopResult.EquippedIngredient, f.Do(WorkshopCommand.Fuse, 1, ids).Result);
        Eq(EconomyResult.Ok, f.Economy.BeginBattle(P, "", "test-room", new InsectSpace.BattleEconomy.BattleResourceRules(30,10,50)));
        Eq(WorkshopResult.BattleActive, f.Do(WorkshopCommand.Buy, 1).Result); Eq(WorkshopResult.BattleActive, f.Do(WorkshopCommand.Equip).Result);
    }
    private static void Idempotency()
    {
        var f = new Fixture(); var request = f.Request(WorkshopCommand.Buy, 1);
        Parallel.For(0, 24, _ => Eq(WorkshopResult.Ok, f.Service.Handle(P, request, f.Growth).Result)); Eq(1, f.State.Inventory.Count); Eq(97L, f.State.Wallet.YuanShi);
        Eq(WorkshopResult.IdempotencyConflict, f.Service.Handle(P, new WorkshopRequest(request.Command, 2, request.OperationId, request.Revision, request.EconomyRevision, request.CatalogHash, 2), f.Growth).Result);
        Eq(WorkshopResult.RevisionConflict, f.Service.Handle(P, new WorkshopRequest(request.Command, 3, "stale", request.Revision, request.EconomyRevision, request.CatalogHash, 1), f.Growth).Result);
        Eq(WorkshopResult.CatalogMismatch, f.Service.Handle(P, new WorkshopRequest(request.Command, 3, "version", f.State.Revision, f.State.EconomyRevision, new string('0',64), 1), f.Growth).Result);
        var rich = new Fixture(10000); for (int i = 0; i < 64; i++) rich.Buy(2);
        var before = rich.State; Eq(WorkshopResult.CapacityExceeded, rich.Do(WorkshopCommand.Buy, 2).Result); Eq(before.Wallet.YuanShi, rich.State.Wallet.YuanShi);
        for (int i = 2; i < WorkshopService.MaxOperations; i++) Eq(WorkshopResult.InvalidRequest, f.Do(WorkshopCommand.Buy, 999).Result);
        Eq(WorkshopResult.CapacityExceeded, f.Do(WorkshopCommand.Buy, 1).Result); Check(f.Service.Handle(P, request, f.Growth).Replayed);
    }
    private static void Wire()
    {
        var f = new Fixture(); var response = f.Do(WorkshopCommand.Buy, 1); var codec = new LocalPlayerPacketCodec();
        var packet = WorkshopPacket.FromResponse(response); Check(codec.Encode(packet, out var bytes)); ReferencePool.Release(packet);
        var buffer = new byte[bytes.Length + 8]; bytes.CopyTo(buffer, 4); var decoded = (WorkshopPacket)codec.Decode(buffer, 4, bytes.Length, out _);
        Eq(1, decoded.Response.Snapshot.Inventory.Count); ReferencePool.Release(decoded);
        for (int size = 0; size < bytes.Length; size++) Check(codec.Decode(bytes, 0, size, out _) == null);
        var trailing = bytes.Concat(new byte[] {0}).ToArray(); Check(codec.Decode(trailing,0,trailing.Length,out _) == null);
        bytes[0] = 2; Check(codec.Decode(bytes,0,bytes.Length,out _) == null);
    }
    private static void Client()
    {
        var f = new Fixture(); WorkshopRequest sent = null; var client = new WorkshopClient(1,"local-home",f.Service.Catalog,r => sent = r);
        client.TransportConnected(); client.Refresh(); Check(client.Receive(f.Service.Handle(P,sent,f.Growth)));
        client.Request(WorkshopCommand.Buy,1); var original = sent; var lost = f.Service.Handle(P,sent,f.Growth);
        client.Tick(9); client.Disconnect("test"); client.TransportConnected(); client.Refresh(); Check(client.Receive(f.Service.Handle(P,sent,f.Growth)));
        Throws<InvalidOperationException>(() => client.Request(WorkshopCommand.Buy,2)); client.Retry(); Eq(original.OperationId,sent.OperationId);
        Check(!client.Receive(lost)); Check(client.Receive(f.Service.Handle(P,sent,f.Growth))); Check(client.LastResponse.Replayed); Eq(1,client.Snapshot.Inventory.Count); Eq(97L,client.Snapshot.Wallet.YuanShi);
    }
    private static void Tcp()
    {
        var clock = new Clock(); var random = new Random(); var catalog = Catalog();
        GameFrameworkEntry.Shutdown();
        using var host = new LocalEconomyHost(0, workshopMode:true, workshopClock:clock, workshopRandom:random, workshopCatalog:catalog);
        var manager = GameFrameworkEntry.GetModule<INetworkManager>(); _ = new InsectSpace.Network.DesktopChannelFactory(manager);
        using var tcp = new LocalEconomyTcpClient(manager, enableCultivation:true, workshopCatalog:catalog); var c = tcp.Workshop;
        try
        {
            tcp.Connect(host.Port); Pump(() => c.Ready && tcp.Client.Ready && tcp.Cultivation.Ready, "connect");
            Eq(0, c.Snapshot.Inventory.Count); Eq(0, host.GuPaths.Snapshot(P,host.Cultivation.Snapshot(P)).Owned.Count);
            Do(WorkshopCommand.Buy,1); Eq(WorkshopResult.RankRequired,c.LastResponse.Result);
            tcp.Cultivation.Awaken(); Pump(() => tcp.Cultivation.Ready,"awaken"); c.Refresh(); Pump(() => c.Ready,"growth");
            Do(WorkshopCommand.Buy,1); long moon = c.LastResponse.ProducedInstance; Check(moon > 0);
            Do(WorkshopCommand.Equip,0,moon); Eq(WorkshopResult.NotRefined,c.LastResponse.Result);
            Do(WorkshopCommand.Refine,0,moon); Eq(WorkshopResult.Ok,c.LastResponse.Result); Eq(1,random.Calls);
            Do(WorkshopCommand.Equip,0,moon); Eq(WorkshopResult.Ok,c.LastResponse.Result);
            Do(WorkshopCommand.Buy,3); Eq(95L,c.Snapshot.Wallet.YuanShi);
            tcp.Dispose(); clock.Value = 110; tcp.Connect(host.Port);
            Pump(() => c.Ready && tcp.Client.Ready && tcp.Cultivation.Ready,"reconnect");
            Check(c.Snapshot.Inventory.Single().DormantAt(c.Snapshot.ServerSeconds)); Eq(moon,c.Snapshot.Loadout.Single());
            Eq(InsectSpace.GuPaths.PathFormation.Empty,catalog.ActiveProfile(c.Snapshot).Formation);
            Do(WorkshopCommand.Feed,1,moon); Eq(WorkshopResult.Ok,c.LastResponse.Result); Eq(9,c.Snapshot.Materials.Single().Count);
            Do(WorkshopCommand.Equip); Eq(WorkshopResult.Ok,c.LastResponse.Result);
            Do(WorkshopCommand.Buy,2); long light=c.LastResponse.ProducedInstance; random.Value=9999;
            Do(WorkshopCommand.Refine,0,light); Eq(WorkshopResult.FailedDestroyed,c.LastResponse.Result);
            Check(c.Snapshot.Inventory.All(g=>g.Id!=light)); Eq(92L,c.Snapshot.Wallet.YuanShi);
            tcp.Client.Refresh(); Pump(()=>tcp.Client.Ready,"wallet"); tcp.BeginBattle(); Pump(()=>tcp.Battle.Ready && tcp.Client.Ready,"battle");
            c.Refresh(); Pump(()=>c.Ready,"battle state"); Check(c.Snapshot.BattleActive);
            Do(WorkshopCommand.Buy,1); Eq(WorkshopResult.BattleActive,c.LastResponse.Result);
            tcp.Battle.Send(InsectSpace.BattleEconomy.ResourceAction.LeaveBattle); Pump(()=>tcp.Battle.Finished && tcp.Client.Ready,"leave");
            tcp.Connect(host.Port); Pump(()=>c.Ready && tcp.Client.Ready,"final reconnect");
            Eq(1,c.Snapshot.Inventory.Count); Eq(92L,c.Snapshot.Wallet.YuanShi); Eq(10L,c.Snapshot.Wallet.XianYuanShi);
        }
        finally { tcp.Dispose(); GameFrameworkEntry.Shutdown(); }
        void Do(WorkshopCommand command,int target=0,params long[] ids) { c.Request(command,target,ids); Pump(()=>c.Ready,command.ToString()); }
        void Pump(Func<bool> done,string stage)
        {
            var timer=Stopwatch.StartNew(); double last=0;
            while(!done() && timer.ElapsedMilliseconds<10000)
            { double now=timer.Elapsed.TotalSeconds; float dt=(float)(now-last); last=now; host.Tick(dt); GameFrameworkEntry.Update(dt,dt); tcp.Tick(dt); Thread.Sleep(2); }
            if(!done()) throw new Exception(stage+": "+c.Status);
        }
    }
    private static void Check(bool condition) { if (!condition) throw new Exception("Workshop assertion failed."); }
    private static void Eq<T>(T expected,T actual) { if (!EqualityComparer<T>.Default.Equals(expected,actual)) throw new Exception($"Expected {expected}, got {actual}."); }
    private static void Throws<T>(Action action) where T:Exception { try { action(); } catch(T) { return; } throw new Exception("Expected " + typeof(T).Name); }
}
