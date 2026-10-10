using System.Diagnostics;
using Framework;
using Framework.Network;
using InsectSpace.Gameplay.Economy;
using InsectSpace.Moonlight;
using InsectSpace.Network;
using InsectSpace.Server.Economy;
using InsectSpace.Simulation;

static class MoonlightTests
{
    public static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("moonlight.deterministic-cast-auto-and-terminal-phases", DeterministicState);
        yield return ("moonlight.invalid-frame-and-snapshot-hash-are-atomic", InvalidState);
        yield return ("moonlight.protocol-roundtrip-and-malformed-rejection", Protocol);
        yield return ("moonlight.real-tcp-kcp-admission-input-and-retreat", Kcp);
    }

    private static void DeterministicState()
    {
        var rules = new MoonlightBattleRules(40, 50, 5, 100, 4, 100, 30, 0, 20, 2, 10, 1001, "moon-blade", "月刃");
        var state = new MoonlightBattleState(1, rules);
        Check(state.Step(0, new[] { new MoonlightBattleCommand(1, 0, MoonlightBattleCommandKind.CastMoonBlade) }));
        Eq(25, state.MonsterHp); // 月刃 20 + frame-zero 普攻 5。
        Eq(20, state.Resource);
        Check(state.Step(1, new[] { new MoonlightBattleCommand(1, 1, MoonlightBattleCommandKind.SetAutoCast, true) }));
        Eq(true, state.AutoCast);
        Eq(25, state.MonsterHp); // 冷却中，自动释放不能绕过冷却。
        Check(state.Step(2, Array.Empty<MoonlightBattleCommand>()));
        Eq(5, state.MonsterHp); // 冷却结束后自动月刃。
        Check(state.Step(3, new[] { new MoonlightBattleCommand(1, 2, MoonlightBattleCommandKind.Retreat) }));
        Eq(MoonlightBattlePhase.Retreated, state.Phase);
        Check(!state.Step(4, Array.Empty<MoonlightBattleCommand>()));
    }

    private static void InvalidState()
    {
        var state = new MoonlightBattleState(1);
        ulong hash = state.StateHash;
        Check(!state.Step(1, Array.Empty<MoonlightBattleCommand>()));
        Eq(-1L, state.Frame); Eq(hash, state.StateHash);
        Check(!state.Step(0, new[] { new MoonlightBattleCommand(99, 0, MoonlightBattleCommandKind.CastMoonBlade) }));
        Eq(-1L, state.Frame); Eq(hash, state.StateHash);
        var snapshot = state.Capture();
        Throws<ArgumentException>(() => MoonlightBattleState.Restore(new MoonlightBattleSnapshot(
            snapshot.ActorId, snapshot.Frame, snapshot.LastSequence, snapshot.PlayerHp, snapshot.MonsterHp,
            snapshot.Resource, snapshot.MoonBladeCooldown, snapshot.AutoCast, snapshot.Phase, snapshot.Rules, snapshot.Hash ^ 1)));
        var replica = new MoonlightBattleReplica(snapshot);
        Check(!replica.Receive(new MoonlightMessage(MoonlightMessageKind.Frame, roomId: "room", sequence: -1,
            state: snapshot)));
        Eq(-1L, replica.Snapshot.Frame);
    }

    private static void Protocol()
    {
        var codec = new MoonlightPacketCodec();
        var battle = new MoonlightBattleState(1);
        Check(battle.Step(0, Array.Empty<MoonlightBattleCommand>()));
        var state = battle.Capture();
        var message = new MoonlightMessage(MoonlightMessageKind.Frame, roomId: "room", sequence: -1, state: state);
        var packet = MoonlightPacket.Create(message);
        Check(codec.Encode(packet, out var bytes));
        ReferencePool.Release(packet);
        var padded = new byte[bytes.Length + 7]; Array.Copy(bytes, 0, padded, 3, bytes.Length);
        var decoded = (MoonlightPacket)codec.Decode(padded, 3, bytes.Length, out _);
        Check(decoded != null); Eq(state.Hash, decoded.Message.State.Hash); ReferencePool.Release(decoded);
        for (int length = 0; length < bytes.Length; length++) Check(codec.Decode(bytes, 0, length, out _) == null);
        var corrupt = (byte[])bytes.Clone(); corrupt[0] ^= 1;
        Check(codec.Decode(corrupt, 0, corrupt.Length, out _) == null);
        var invalid = MoonlightPacket.Create(new MoonlightMessage(MoonlightMessageKind.Input, roomId: "room", sequence: 0, input: (MoonlightInputKind)99));
        Check(!codec.Encode(invalid, out _)); ReferencePool.Release(invalid);
    }

    private static void Kcp()
    {
        GameFrameworkEntry.Shutdown();
        using var host = new LocalEconomyHost(0, moonlightBattlePort: 0);
        var manager = GameFrameworkEntry.GetModule<INetworkManager>();
        _ = new DesktopChannelFactory(manager);
        using var client = new LocalEconomyTcpClient(manager);
        try
        {
            client.Connect(host.Port);
            Pump(() => client.Client.Ready, host, client, "TCP wallet");
            client.BeginMoonlightBattle();
            Pump(() => client.Moonlight.Ready, host, client, "Moonlight admission");
            var initial = client.Moonlight.Replica.Snapshot;
            client.Moonlight.CastMoonBlade();
            Pump(() => client.Moonlight.Pending == null && client.Moonlight.Replica.Snapshot.LastSequence >= 0, host, client, "Moon blade");
            Check(client.Moonlight.Replica.Snapshot.MonsterHp < initial.MonsterHp);
            client.Moonlight.SetAutoCast(true);
            Pump(() => client.Moonlight.Pending == null && client.Moonlight.Replica.Snapshot.AutoCast, host, client, "auto cast");
            Eq(true, client.Moonlight.Replica.Snapshot.AutoCast);
            client.Moonlight.Retreat();
            Pump(() => client.Moonlight.Finished, host, client, "retreat");
            Eq(MoonlightBattlePhase.Retreated, client.Moonlight.Replica.Snapshot.Phase);
        }
        finally { client.Dispose(); GameFrameworkEntry.Shutdown(); }
    }

    private static void Pump(Func<bool> done, LocalEconomyHost host, LocalEconomyTcpClient client, string stage)
    {
        var clock = Stopwatch.StartNew(); double last = 0;
        while (!done() && clock.ElapsedMilliseconds < 10000)
        {
            double now = clock.Elapsed.TotalSeconds; float elapsed = (float)(now - last); last = now;
            host.Tick(elapsed); GameFrameworkEntry.Update(elapsed, elapsed); client.Tick(elapsed); Thread.Sleep(2);
        }
        if (!done()) throw new Exception(stage + ": " + client.Moonlight.Status + " / " + client.Client.Status);
    }

    private static void Check(bool value) { if (!value) throw new Exception("Moonlight assertion failed."); }
    private static void Eq<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, actual {actual}."); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
}
