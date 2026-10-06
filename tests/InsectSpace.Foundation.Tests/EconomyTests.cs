using System.Diagnostics;
using Framework;
using Framework.Network;
using InsectSpace.Economy;
using InsectSpace.BattleEconomy;
using InsectSpace.Gameplay.Economy;
using InsectSpace.Server.Economy;

static class EconomyTests
{
    private static readonly EconomyPrincipal P = new(11, "home-a", "world-a", 7);
    private static readonly BattleResourceRules Rules = new(30, 10, 50);
    public static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("economy.reward-and-recharge-trusted-idempotency", Credits);
        yield return ("economy.reserve-cancel-and-world-immediate-cost", ReserveAndWorld);
        yield return ("economy.battle-essence-consumption-and-authority-refund", Battle);
        yield return ("economy.battle-frame-order-replay-and-atomic-rejection", Determinism);
        yield return ("economy.concurrent-requests-cannot-double-spend", Concurrency);
        yield return ("economy.overflow-including-escrow-is-atomic", Overflow);
        yield return ("economy.codec-roundtrip-offset-and-malformed-inputs", Codec);
        yield return ("economy.client-stale-identity-timeout-and-retry", Client);
        yield return ("economy.real-tcp-client-server-reserve-battle-reconnect", Tcp);
        yield return ("economy.zero-reserve-admission-and-local-console-lifecycle", ZeroReserveAndConsole);
        yield return ("economy.kcp-resource-room-client-server-end-to-end", KcpBattle);
        yield return ("economy.kcp-capacity-preserves-recovery-and-settlement", KcpCapacity);
        yield return ("economy.resource-replica-gap-hash-and-wire-validation", ReplicaAndWire);
    }
    private static EconomyService Service()
    {
        var service = new EconomyService(new InMemoryEconomyStore(), new[] { new WorldActionPrice("cast", new StoneAmounts(3, 2)) });
        service.CreateAccount(P, 100, 100);
        Eq(EconomyResult.Ok, service.GrantReward(P, RewardSource.Monster, "monster-1", 100));
        Eq(EconomyResult.Ok, service.ApplyVerifiedRecharge(P, "paid-1", 10)); return service;
    }
    private static EconomyRequest Request(EconomyService service, EconomyCommand command, string id, StoneAmounts amount = default, string target = "", long epoch = 0) =>
        new(command, 1, id, service.Snapshot(P).Revision, amount, target, epoch);
    private static void Credits()
    {
        var s = Service(); long revision = s.Snapshot(P).Revision;
        Eq(EconomyResult.Ok, s.GrantReward(P, RewardSource.Monster, "monster-1", 100));
        Eq(EconomyResult.Ok, s.ApplyVerifiedRecharge(P, "paid-1", 10));
        Eq(revision, s.Snapshot(P).Revision);
        Eq(EconomyResult.IdempotencyConflict, s.ApplyVerifiedRecharge(P, "paid-1", 11));
        Eq(EconomyResult.InvalidRequest, s.GrantReward(P, (RewardSource)99, "bad", 1));
        Eq(EconomyResult.InvalidRequest, s.ApplyVerifiedRecharge(P, "negative", -1));
        var other = new EconomyPrincipal(22, "home-b", "world-b", 2); s.CreateAccount(other, 0, 100);
        Eq(EconomyResult.IdempotencyConflict, s.ApplyVerifiedRecharge(other, "paid-1", 10));
        Eq(0L, s.Snapshot(other).Wallet.XianYuanShi);
        Throws<UnauthorizedAccessException>(() => s.Snapshot(P with { HomeRealmId = "wrong" }));
        Eq(10L, s.Snapshot(P).Wallet.XianYuanShi);
    }
    private static void ReserveAndWorld()
    {
        var s = Service(); var request = Request(s, EconomyCommand.PrepareReserve, "prepare", new StoneAmounts(20, 4));
        var result = s.Handle(P, request); Eq(EconomyResult.Ok, result.Result);
        Eq(new StoneAmounts(80, 6), result.Snapshot.Wallet); Eq(new StoneAmounts(20, 4), result.Snapshot.Reserve);
        Check(s.Handle(P, request).Replayed);
        Eq(EconomyResult.IdempotencyConflict, s.Handle(P, new EconomyRequest(request.Command, 2, request.OperationId, request.ExpectedRevision, new StoneAmounts(21, 4))).Result);
        Eq(EconomyResult.ReserveBusy, s.Handle(P, Request(s, EconomyCommand.PrepareReserve, "again", new StoneAmounts(1, 0))).Result);
        Eq(EconomyResult.StaleRoute, s.Handle(P, Request(s, EconomyCommand.WorldAction, "stale", target: "cast", epoch: 6)).Result);
        Eq(EconomyResult.Ok, s.Handle(P, Request(s, EconomyCommand.WorldAction, "world", target: "cast", epoch: 7)).Result);
        Eq(new StoneAmounts(77, 4), s.Snapshot(P).Wallet);
        var cancel = Request(s, EconomyCommand.CancelReserve, "cancel", target: result.Snapshot.ReserveId);
        Eq(EconomyResult.Ok, s.Handle(P, cancel).Result); Check(s.Handle(P, cancel).Replayed);
        Eq(new StoneAmounts(97, 8), s.Snapshot(P).Wallet);
        Eq(EconomyResult.InsufficientFunds, s.Handle(P, Request(s, EconomyCommand.PrepareReserve, "poor", new StoneAmounts(1, 9))).Result);
        Eq(new StoneAmounts(97, 8), s.Snapshot(P).Wallet);
        var forgedPrice = Request(s, EconomyCommand.WorldAction, "price", new StoneAmounts(0, 1), "cast", 7);
        Eq(EconomyResult.InvalidRequest, s.Handle(P, forgedPrice).Result);
    }
    private static void Battle()
    {
        var s = Service(); var prepared = s.Handle(P, Request(s, EconomyCommand.PrepareReserve, "prepare", new StoneAmounts(20, 4))).Snapshot;
        Eq(EconomyResult.Ok, s.BeginBattle(P, prepared.ReserveId, "room-a", Rules));
        Eq(EconomyResult.Ok, s.BeginBattle(P, prepared.ReserveId, "room-a", Rules));
        Eq(EconomyResult.InvalidState, s.BeginBattle(P, prepared.ReserveId, "wrong", Rules));
        Eq(EconomyResult.InvalidState, s.Handle(P, Request(s, EconomyCommand.CancelReserve, "cancel-in-battle", target: prepared.ReserveId)).Result);
        Eq(EconomyResult.InvalidState, s.Handle(P, Request(s, EconomyCommand.WorldAction, "world-in-battle", target: "cast", epoch: 7)).Result);
        Eq(EconomyResult.Ok, s.ApplyBattleFrame(P, "room-a", 0, new[] { new ResourceCommand(11, 0, ResourceAction.CastSkill) }));
        Eq(70L, s.Snapshot(P).ImmortalEssence); Eq(prepared.Reserve, s.Snapshot(P).Reserve); Eq(prepared.Wallet, s.Snapshot(P).Wallet);
        Eq(EconomyResult.Ok, s.ApplyBattleFrame(P, "room-a", 1, new[] { new ResourceCommand(11, 1, ResourceAction.UseYuanShiReserve), new ResourceCommand(11, 2, ResourceAction.UseXianYuanShiReserve) }));
        Eq(100L, s.Snapshot(P).ImmortalEssence); Eq(new StoneAmounts(19, 3), s.Snapshot(P).Reserve);
        Eq(EconomyResult.InvalidState, s.SettleBattle(P, prepared.ReserveId, "wrong"));
        Eq(EconomyResult.Ok, s.SettleBattle(P, prepared.ReserveId, "room-a"));
        long revision = s.Snapshot(P).Revision;
        Eq(EconomyResult.Ok, s.SettleBattle(P, prepared.ReserveId, "room-a")); Eq(revision, s.Snapshot(P).Revision);
        Eq(new StoneAmounts(99, 9), s.Snapshot(P).Wallet); Eq(ReservePhase.None, s.Snapshot(P).Phase);
    }
    private static void Determinism()
    {
        var a = new BattleResources(11, 100, 100, new StoneAmounts(2, 1), Rules);
        var b = new BattleResources(11, 100, 100, new StoneAmounts(2, 1), Rules);
        var commands = new[] { new ResourceCommand(11, 0, ResourceAction.CastSkill), new ResourceCommand(11, 1, ResourceAction.UseYuanShiReserve) };
        Check(!a.ApplyFrame(1, commands)); Check(a.ApplyFrame(0, commands)); Check(b.ApplyFrame(0, commands.Reverse().ToArray()));
        Eq(a.StateHash, b.StateHash); ulong hash = a.StateHash;
        Check(!a.ApplyFrame(0, commands));
        Check(!a.ApplyFrame(1, new[] { new ResourceCommand(11, 2, ResourceAction.CastSkill), new ResourceCommand(11, 2, ResourceAction.CastSkill) }));
        Eq(hash, a.StateHash); Check(!a.ApplyFrame(1, new[] { new ResourceCommand(99, 2, ResourceAction.CastSkill) }));
        Eq(hash, a.StateHash);
        Check(a.ApplyFrame(1, new[] { new ResourceCommand(11, 2, ResourceAction.CastSkill), new ResourceCommand(11, 3, ResourceAction.CastSkill), new ResourceCommand(11, 4, ResourceAction.CastSkill) }));
        Eq(20L, a.Essence); Eq(new StoneAmounts(1, 1), a.Remaining);
    }
    private static void ZeroReserveAndConsole()
    {
        var s = Service();
        Eq(EconomyResult.Ok, s.BeginBattle(P, "", "zero-room", Rules));
        var before = s.Snapshot(P); Check(before.Reserve.IsEmpty); Check(before.ReserveId.Length > 0);
        Eq(EconomyResult.Ok, s.BeginBattle(P, "", "zero-room", Rules));
        Eq(before.Revision, s.Snapshot(P).Revision);
        Eq(EconomyResult.Ok, s.ApplyBattleFrame(P, "zero-room", 0, new[] { new ResourceCommand(P.PlayerId, 0, ResourceAction.CastSkill) }));
        Eq(EconomyResult.Ok, s.SettleBattle(P, before.ReserveId, "zero-room"));
        Eq(new StoneAmounts(100, 10), s.Snapshot(P).Wallet); Eq(70L, s.Snapshot(P).ImmortalEssence);
        Eq(EconomyResult.InvalidState, s.BeginBattle(P, "", "zero-room", Rules));
        Eq(EconomyResult.InvalidRequest, s.SettleBattle(P, null, "zero-room"));
        using var host = new LocalEconomyHost(0); var console = new LocalEconomyConsole(host);
        Check(console.Execute("recharge").Contains("SIMULATED"));
        console.Execute("activity"); console.Execute("monster"); console.Execute("battle-start");
        console.Execute("cast"); console.Execute("battle-end");
        var snapshot = host.Service.Snapshot(host.Principal);
        Eq(new StoneAmounts(115, 15), snapshot.Wallet); Eq(70L, snapshot.ImmortalEssence); Eq(ReservePhase.None, snapshot.Phase);
    }
    private static void Concurrency()
    {
        var s = Service(); long revision = s.Snapshot(P).Revision;
        var results = Enumerable.Range(0, 32).AsParallel().Select(i => s.Handle(P, new EconomyRequest(EconomyCommand.PrepareReserve, i + 1, "parallel-" + i, revision, new StoneAmounts(70, 0)))).ToArray();
        Eq(1, results.Count(r => r.Result == EconomyResult.Ok)); Eq(31, results.Count(r => r.Result == EconomyResult.RevisionConflict));
        Eq(30L, s.Snapshot(P).Wallet.YuanShi); Eq(70L, s.Snapshot(P).Reserve.YuanShi);
        var same = Service(); Parallel.For(0, 32, _ => Eq(EconomyResult.Ok, same.ApplyVerifiedRecharge(P, "parallel-order", 5)));
        Eq(15L, same.Snapshot(P).Wallet.XianYuanShi);
    }
    private static void Overflow()
    {
        var s = Service(); Eq(EconomyResult.Ok, s.GrantReward(P, RewardSource.Activity, "large", long.MaxValue - 100));
        var prepared = s.Handle(P, Request(s, EconomyCommand.PrepareReserve, "prepare", new StoneAmounts(20, 0))).Snapshot;
        Eq(EconomyResult.CapacityExceeded, s.GrantReward(P, RewardSource.Activity, "overflow", 1));
        Eq(prepared.Revision, s.Snapshot(P).Revision);
        Eq(EconomyResult.Ok, s.Handle(P, Request(s, EconomyCommand.CancelReserve, "cancel", target: prepared.ReserveId)).Result);
        Eq(long.MaxValue, s.Snapshot(P).Wallet.YuanShi);
    }
    private static void Codec()
    {
        var codec = new EconomyPacketCodec(); var s = Service();
        var request = Request(s, EconomyCommand.PrepareReserve, "wire", new StoneAmounts(2, 1));
        var packet = EconomyPacket.FromRequest(request); Check(codec.Encode(packet, out byte[] bytes)); ReferencePool.Release(packet);
        var padded = new byte[bytes.Length + 9]; Array.Copy(bytes, 0, padded, 4, bytes.Length);
        var decoded = (EconomyPacket)codec.Decode(padded, 4, bytes.Length, out _); Check(request.SameOperation(decoded.Request)); ReferencePool.Release(decoded);
        for (int length = 0; length < bytes.Length; length++) Check(codec.Decode(bytes, 0, length, out _) == null);
        foreach (int index in new[] { 0, 4, 8, 9 })
        { var corrupt = (byte[])bytes.Clone(); corrupt[index] = 255; Check(codec.Decode(corrupt, 0, corrupt.Length, out _) == null); }
        Check(codec.Decode(padded, 4, bytes.Length + 1, out _) == null);
        var response = EconomyPacket.FromResponse(s.Handle(P, request)); Check(codec.Encode(response, out var reply)); ReferencePool.Release(response);
        var read = (EconomyPacket)codec.Decode(reply, 0, reply.Length, out _); Eq(new StoneAmounts(98, 9), read.Response.Snapshot.Wallet); ReferencePool.Release(read);
        foreach (var invalid in new[] { new EconomyRequest((EconomyCommand)90, 1, "bad", 0), new EconomyRequest(EconomyCommand.PrepareReserve, 1, "negative", 0, new StoneAmounts(-1, 0)) })
        { var p = EconomyPacket.FromRequest(invalid); Check(!codec.Encode(p, out _)); ReferencePool.Release(p); }
    }
    private static void Client()
    {
        var s = Service(); EconomyRequest sent = null; var client = new EconomyClient(11, "home-a", r => sent = r);
        Throws<InvalidOperationException>(() => client.Prepare(new StoneAmounts(1, 0)));
        client.TransportConnected(); client.Refresh(); Check(client.Receive(s.Handle(P, sent)));
        client.Prepare(new StoneAmounts(20, 2)); var first = sent; var committed = s.Handle(P, first);
        Eq(new StoneAmounts(100, 10), client.Snapshot.Wallet);
        Check(!client.Receive(new EconomyResponse(first.RequestId + 1, first.OperationId, EconomyResult.Ok, false, committed.Snapshot)));
        var alien = new EconomySnapshot(22, "home-a", 99, default, default, "", ReservePhase.None, "", 0, 1);
        Check(!client.Receive(new EconomyResponse(first.RequestId, first.OperationId, EconomyResult.Ok, false, alien)));
        client.Tick(9); Check(client.RetryableRequest != null);
        Throws<InvalidOperationException>(() => client.Prepare(new StoneAmounts(1, 0)));
        client.Disconnect("test"); Check(client.Snapshot == null);
        client.TransportConnected(); client.Refresh(); Check(client.Receive(s.Handle(P, sent)));
        client.Retry(); Eq(first.OperationId, sent.OperationId); Check(sent.RequestId != first.RequestId);
        Check(client.Receive(s.Handle(P, sent))); Check(client.LastResponse.Replayed); Check(client.RetryableRequest == null);
        Eq(new StoneAmounts(80, 8), client.Snapshot.Wallet);
        client.Refresh();
        var stale = new EconomySnapshot(11, "home-a", 0, default, default, "", ReservePhase.None, "", 0, 1);
        Check(!client.Receive(new EconomyResponse(sent.RequestId, sent.OperationId, EconomyResult.Ok, false, stale)));
    }
    private static void Tcp()
    {
        GameFrameworkEntry.Shutdown();
        using var host = new LocalEconomyHost(0);
        var manager = GameFrameworkEntry.GetModule<INetworkManager>();
        using var tcp = new LocalEconomyTcpClient(manager);
        try
        {
            tcp.Connect(host.Port); Pump(() => tcp.Client.Ready);
            Eq(new StoneAmounts(100, 10), tcp.Client.Snapshot.Wallet);
            tcp.Client.Prepare(new StoneAmounts(20, 4)); Pump(() => tcp.Client.Pending == null);
            string reserveId = tcp.Client.Snapshot.ReserveId;
            Eq(new StoneAmounts(80, 6), tcp.Client.Snapshot.Wallet);
            tcp.Dispose(); tcp.Connect(host.Port); Pump(() => tcp.Client.Ready);
            Eq(reserveId, tcp.Client.Snapshot.ReserveId); // Connection loss must not refund held funds.
            Eq(EconomyResult.Ok, host.Service.BeginBattle(host.Principal, reserveId, "local-test-room", Rules));
            Eq(EconomyResult.Ok, host.Service.ApplyBattleFrame(host.Principal, "local-test-room", 0, new[] { new ResourceCommand(1, 0, ResourceAction.CastSkill), new ResourceCommand(1, 1, ResourceAction.UseYuanShiReserve) }));
            tcp.Client.Refresh(); Pump(() => tcp.Client.Pending == null); Eq(80L, tcp.Client.Snapshot.ImmortalEssence); Eq(19L, tcp.Client.Snapshot.Reserve.YuanShi);
            Eq(EconomyResult.Ok, host.Service.SettleBattle(host.Principal, reserveId, "local-test-room"));
            tcp.Client.Refresh(); Pump(() => tcp.Client.Pending == null); Eq(new StoneAmounts(99, 10), tcp.Client.Snapshot.Wallet);
            tcp.Client.WorldAction("local-world-skill", 1); Pump(() => tcp.Client.Pending == null); Eq(96L, tcp.Client.Snapshot.Wallet.YuanShi);
            tcp.Dispose(); Eq(0, manager.GetAllNetworkChannels().Length);
            using (var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0))
            { listener.Start(); int unavailable = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); tcp.Connect(unavailable); }
            Pump(() => !tcp.Active); Check(tcp.Client.Snapshot == null); Check(!tcp.Client.Connected);
        }
        finally { tcp.Dispose(); GameFrameworkEntry.Shutdown(); }
        void Pump(Func<bool> done)
        {
            var clock = Stopwatch.StartNew();
            double last = 0;
            while (!done() && clock.ElapsedMilliseconds < 12000)
            {
                double now = clock.Elapsed.TotalSeconds; float elapsed = (float)(now - last); last = now;
                host.Tick(elapsed); GameFrameworkEntry.Update(elapsed, elapsed); tcp.Tick(elapsed); Thread.Sleep(2);
            }
            Check(done());
        }
    }
    private static void Check(bool value) { if (!value) throw new Exception("Economy assertion failed."); }
    private static void ReplicaAndWire()
    {
        var state = new BattleResources(11, 100, 100, new StoneAmounts(2, 1), Rules);
        var replica = new BattleResourceReplica("resource-room", state.Capture());
        var codec = new BattleResourceCodec();
        state.ApplyFrame(0, new[] { new ResourceCommand(11, 0, ResourceAction.CastSkill) });
        var first = new BattleResourceMessage(BattleMessageKind.Frame, roomId: "resource-room", sequence: 0, action: ResourceAction.CastSkill, state: state.Capture());
        state.ApplyFrame(1, new[] { new ResourceCommand(11, 1, ResourceAction.UseXianYuanShiReserve) });
        var second = new BattleResourceMessage(BattleMessageKind.Frame, roomId: "resource-room", sequence: 1, action: ResourceAction.UseXianYuanShiReserve, state: state.Capture());
        Check(replica.Receive(second)); Eq(-1L, replica.Snapshot.Frame); Eq(100L, replica.Snapshot.Essence);
        Eq(1, replica.BufferedFrames); Check(replica.Receive(first)); Eq(state.StateHash, replica.Snapshot.Hash);
        Check(replica.Receive(first)); Eq(1L, replica.Snapshot.Frame);
        var initial = new BattleResources(11, 100, 100, new StoneAmounts(2, 1), Rules);
        var wrong = new BattleResourceReplica("resource-room", initial.Capture());
        ulong hash = wrong.Snapshot.Hash;
        Check(!wrong.Receive(new BattleResourceMessage(BattleMessageKind.Frame, roomId: "resource-room", sequence: 0, action: ResourceAction.UseYuanShiReserve, state: first.State)));
        Eq(hash, wrong.Snapshot.Hash);
        var packet = BattleResourcePacket.Create(first);
        Check(codec.Encode(packet, out byte[] bytes)); ReferencePool.Release(packet);
        var padded = new byte[bytes.Length + 8]; Array.Copy(bytes, 0, padded, 4, bytes.Length);
        var decoded = (BattleResourcePacket)codec.Decode(padded, 4, bytes.Length, out _); Eq(first.State.Hash, decoded.Message.State.Hash); ReferencePool.Release(decoded);
        for (int i = 0; i < bytes.Length; i++) Check(codec.Decode(bytes, 0, i, out _) == null);
        bytes[bytes.Length - 1] ^= 1; Check(codec.Decode(bytes, 0, bytes.Length, out _) == null);
        var bad = BattleResourcePacket.Create(new BattleResourceMessage(BattleMessageKind.Input, roomId: "resource-room", sequence: 0, action: (ResourceAction)255));
        Check(!codec.Encode(bad, out _)); ReferencePool.Release(bad);
    }
    private static void KcpBattle()
    {
        GameFrameworkEntry.Shutdown();
        using var host = new LocalEconomyHost(0);
        var manager = GameFrameworkEntry.GetModule<INetworkManager>();
        _ = new InsectSpace.Network.DesktopChannelFactory(manager);
        using var tcp = new LocalEconomyTcpClient(manager);
        try
        {
            tcp.Connect(host.Port); Pump(() => tcp.Client.Ready, "TCP wallet");
            tcp.Client.Prepare(new StoneAmounts(20, 4)); Pump(() => tcp.Client.Ready, "escrow");
            tcp.BeginBattle(); Pump(() => tcp.Battle.Ready && tcp.Client.Ready, "KCP admission");
            string room = tcp.Battle.RoomId; string reserve = tcp.Battle.ReserveId;
            Eq(ReservePhase.InBattle, tcp.Client.Snapshot.Phase);
            Eq(new StoneAmounts(80, 6), host.Service.Snapshot(host.Principal).Wallet);
            tcp.Battle.Send(ResourceAction.CastSkill); Eq(100L, tcp.Battle.Replica.Snapshot.Essence);
            Pump(() => tcp.Battle.Ready && tcp.Battle.Replica.Snapshot.LastSequence == 0, "cast frame");
            Eq(70L, tcp.Battle.Replica.Snapshot.Essence); Eq(new StoneAmounts(20, 4), tcp.Battle.Replica.Snapshot.Remaining);
            var battleChannel = manager.GetAllNetworkChannels().Single(c => c.Name.StartsWith("Economy.ResourceBattle."));
            battleChannel.Send(BattleResourcePacket.Create(new BattleResourceMessage(BattleMessageKind.Input, roomId: room, sequence: 0, action: ResourceAction.CastSkill)));
            tcp.Battle.Send(ResourceAction.UseYuanShiReserve); Pump(() => tcp.Battle.Ready, "use yuan frame");
            Eq(80L, tcp.Battle.Replica.Snapshot.Essence); Eq(new StoneAmounts(19, 4), tcp.Battle.Replica.Snapshot.Remaining);
            // Drop the entire client after a committed command. Re-admit to the same server room.
            tcp.Dispose(); tcp.Connect(host.Port); Pump(() => tcp.Client.Ready, "TCP reconnect");
            Eq(reserve, tcp.Client.Snapshot.ReserveId); tcp.BeginBattle(); Pump(() => tcp.Battle.Ready && tcp.Client.Ready, "KCP recovery");
            Eq(room, tcp.Battle.RoomId); Eq(80L, tcp.Battle.Replica.Snapshot.Essence);
            Eq(new StoneAmounts(19, 4), tcp.Battle.Replica.Snapshot.Remaining);
            tcp.Battle.Send(ResourceAction.UseXianYuanShiReserve); Pump(() => tcp.Battle.Ready, "use xian frame");
            Eq(100L, tcp.Battle.Replica.Snapshot.Essence); Eq(new StoneAmounts(19, 3), tcp.Battle.Replica.Snapshot.Remaining);
            var serverState = host.Service.BattleSnapshot(host.Principal, room);
            Eq(serverState.Hash, tcp.Battle.Replica.Snapshot.Hash);
            tcp.Battle.Send(ResourceAction.LeaveBattle);
            Pump(() => tcp.Battle.Finished && tcp.Client.Ready && tcp.Client.Snapshot.Phase == ReservePhase.None, "authority settlement");
            Eq(new StoneAmounts(99, 9), tcp.Client.Snapshot.Wallet);
            // Zero reserve room and ordinary casting must leave both wallet currencies unchanged.
            tcp.BeginBattle(); Pump(() => tcp.Battle.Ready && tcp.Client.Ready, "zero reserve room");
            Check(tcp.Battle.Replica.Snapshot.Remaining.IsEmpty);
            tcp.Battle.Send(ResourceAction.CastSkill); Pump(() => tcp.Battle.Ready, "zero reserve cast");
            tcp.Battle.Send(ResourceAction.LeaveBattle);
            Pump(() => tcp.Battle.Finished && tcp.Client.Ready && tcp.Client.Snapshot.Phase == ReservePhase.None, "zero reserve settlement");
            Eq(new StoneAmounts(99, 9), tcp.Client.Snapshot.Wallet); Eq(70L, tcp.Client.Snapshot.ImmortalEssence);
            tcp.Client.WorldAction("local-world-premium", 1); Pump(() => tcp.Client.Ready, "world debit");
            Eq(new StoneAmounts(99, 8), tcp.Client.Snapshot.Wallet);
            tcp.Dispose(); Eq(0, manager.NetworkChannelCount);
        }
        finally { tcp.Dispose(); GameFrameworkEntry.Shutdown(); }
        void Pump(Func<bool> done, string stage)
        {
            var clock = Stopwatch.StartNew(); double last = 0;
            while (!done() && clock.ElapsedMilliseconds < 10000)
            {
                double now = clock.Elapsed.TotalSeconds; float dt = (float)(now - last); last = now;
                host.Tick(dt); GameFrameworkEntry.Update(dt, dt); tcp.Tick(dt); Thread.Sleep(2);
            }
            if (!done()) throw new Exception(stage + ": " + tcp.Battle.Status + " / " + tcp.Client.Status);
        }
    }
    private static void KcpCapacity()
    {
        GameFrameworkEntry.Shutdown();
        using var host = new LocalEconomyHost(0, battleOperationLimit: 3);
        var manager = GameFrameworkEntry.GetModule<INetworkManager>();
        _ = new InsectSpace.Network.DesktopChannelFactory(manager);
        using var tcp = new LocalEconomyTcpClient(manager);
        try
        {
            tcp.Connect(host.Port); Pump(() => tcp.Client.Ready, "wallet");
            tcp.Client.Prepare(new StoneAmounts(20, 4)); Pump(() => tcp.Client.Ready, "reserve");
            tcp.BeginBattle(); Pump(() => tcp.Battle.Ready && tcp.Client.Ready, "admission");
            string room = tcp.Battle.RoomId;
            // Fill the bounded admission history, then recover once more without new escrow.
            for (int i = 0; i < 3; i++)
            {
                tcp.Connect(host.Port); Pump(() => tcp.Client.Ready, "reconnect");
                tcp.BeginBattle(); Pump(() => tcp.Battle.Ready && tcp.Client.Ready, "recovery at capacity");
                Eq(room, tcp.Battle.RoomId);
            }
            for (int i = 0; i < 3; i++)
            {
                tcp.Battle.Send(ResourceAction.CastSkill); Pump(() => tcp.Battle.Ready, "bounded input");
            }
            Eq(10L, tcp.Battle.Replica.Snapshot.Essence);
            tcp.Battle.Send(ResourceAction.CastSkill);
            Pump(() => tcp.Battle.Status.Contains(nameof(EconomyResult.CapacityExceeded)), "reject excess input");
            Eq(2L, host.Service.BattleSnapshot(host.Principal, room).LastSequence);
            tcp.Connect(host.Port); Pump(() => tcp.Client.Ready, "reconnect after rejection");
            tcp.BeginBattle(); Pump(() => tcp.Battle.Ready && tcp.Client.Ready, "recover to leave");
            tcp.Battle.Send(ResourceAction.LeaveBattle);
            Pump(() => tcp.Battle.Finished && tcp.Client.Ready && tcp.Client.Snapshot.Phase == ReservePhase.None, "settle at capacity");
            Eq(new StoneAmounts(100, 10), tcp.Client.Snapshot.Wallet);
            Eq(10L, tcp.Client.Snapshot.ImmortalEssence);
            tcp.BeginBattle();
            Pump(() => tcp.Battle.Status.Contains(nameof(EconomyResult.CapacityExceeded)), "new room stays bounded");
            Eq(ReservePhase.None, host.Service.Snapshot(host.Principal).Phase);
        }
        finally { tcp.Dispose(); GameFrameworkEntry.Shutdown(); }
        void Pump(Func<bool> done, string stage)
        {
            var clock = Stopwatch.StartNew(); double last = 0;
            while (!done() && clock.ElapsedMilliseconds < 10000)
            {
                double now = clock.Elapsed.TotalSeconds; float dt = (float)(now - last); last = now;
                host.Tick(dt); GameFrameworkEntry.Update(dt, dt); tcp.Tick(dt); Thread.Sleep(2);
            }
            if (!done()) throw new Exception(stage + ": " + tcp.Battle.Status + " / " + tcp.Client.Status);
        }
    }
    private static void Eq<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, actual {actual}."); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
}
