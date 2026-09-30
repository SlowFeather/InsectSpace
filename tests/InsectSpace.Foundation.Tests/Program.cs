using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Framework;
using Framework.Deterministic;
using Framework.Network;
using Framework.Network.Kcp;
using InsectSpace.Contracts;
using InsectSpace.Foundation;
using InsectSpace.Gameplay.Modules;
using InsectSpace.Network;
using InsectSpace.Simulation;

var results = new List<object>();
int failures = 0;
Run("modules.sort-and-reverse-shutdown", () =>
{
    var log = new List<string>();
    using (var host = new ModuleHost())
    {
        host.Start(new[] { new TestModule("b", log, "a"), new TestModule("a", log) }, new ServiceRegistry());
        Equal("a,b", string.Join(",", host.ActiveModuleIds));
    }
    Equal("+a,+b,-b,-a", string.Join(",", log));
});
Run("modules.reject-cycle-and-missing-dependency", () =>
{
    var log = new List<string>();
    Throws<ArgumentException>(() => new ModuleHost().Start(
        new[] { new TestModule("a", log, "b"), new TestModule("b", log, "a") }, new ServiceRegistry()));
    Throws<ArgumentException>(() => new ModuleHost().Start(new[] { new TestModule("a", log, "absent") }, new ServiceRegistry()));
    Equal(0, log.Count);
});
Run("modules.reject-duplicate", () => Throws<ArgumentException>(() => new ModuleHost().Start(
    new[] { new TestModule("a", new()), new TestModule("a", new()) }, new ServiceRegistry())));
Run("modules.rollback-including-partial-start", () =>
{
    var log = new List<string>();
    using var host = new ModuleHost();
    var failure = new TestModule("b", log, "a") { FailStart = true };
    Throws<InvalidOperationException>(() => host.Start(new[] { failure, new TestModule("a", log) }, new ServiceRegistry()));
    Equal("+a,+b,-b,-a", string.Join(",", log));
});
Run("modules.shutdown-continues-after-failure", () =>
{
    var log = new List<string>();
    var host = new ModuleHost();
    host.Start(new[] { new TestModule("a", log), new TestModule("b", log, "a") { FailStop = true } }, new ServiceRegistry());
    Throws<AggregateException>(() => host.Dispose());
    Equal("+a,+b,-b,-a", string.Join(",", log));
});
Run("services.duplicate-freeze-and-missing", () =>
{
    var services = new ServiceRegistry();
    services.Add("a");
    Throws<InvalidOperationException>(() => services.Add("b"));
    Throws<InvalidOperationException>(() => services.Get<SessionCoordinator>());
    services.Freeze();
    Throws<InvalidOperationException>(() => services.Add(new SessionCoordinator()));
});
Run("session.local-battle-return", () =>
{
    var session = WorldSession();
    session.BeginLocalEncounter();
    Equal(SessionPhase.Battle, session.Phase);
    session.ReturnToWorld();
    Equal(SessionPhase.World, session.Phase);
    Equal("one", session.Route.InstanceId);
});
Run("session.online-does-not-enter-before-connect", () =>
{
    var session = WorldSession();
    session.BeginOnlineEncounter(Ticket(), 10);
    Equal(SessionPhase.ConnectingBattle, session.Phase);
    session.BattleConnected();
    Equal(SessionPhase.Battle, session.Phase);
    session.ReturnToWorld();
});
Run("session.expired-ticket-rejected", () =>
{
    var session = WorldSession();
    Throws<ArgumentException>(() => session.BeginOnlineEncounter(Ticket(), 100));
    Equal(SessionPhase.World, session.Phase);
});
Run("session.stale-route-and-battle-transfer-rejected", () =>
{
    var session = WorldSession();
    Throws<InvalidOperationException>(() => session.EnterWorld(Route()));
    session.BeginLocalEncounter();
    Throws<InvalidOperationException>(() => session.EnterWorld(Route(2)));
});
Run("session.disconnect-and-authoritative-recovery", () =>
{
    var session = WorldSession();
    session.ConnectionLost();
    Equal(SessionPhase.Recovering, session.Phase);
    session.EnterWorld(Route(2));
    Equal(SessionPhase.World, session.Phase);
    session.SignOut();
    Equal(0L, session.PlayerId);
    Equal(null, session.Route);
});
Run("session.route-does-not-leak-mutable-state", () =>
{
    var session = WorldSession();
    var route = session.Route;
    route.InstanceId = "tampered";
    Equal("one", session.Route.InstanceId);
});
Run("aoi.reject-stale-and-cross-instance-snapshots", () =>
{
    var presence = new WorldPresenceStore();
    presence.Bind(Route());
    var actors = new[] { new WorldActorSnapshot { ActorId = 1 } };
    Check(!presence.Apply("other", 1, 1, actors));
    Check(presence.Apply("one", 1, 1, actors));
    Check(!presence.Apply("one", 1, 1, Array.Empty<WorldActorSnapshot>()));
    actors[0].ActorId = 999;
    Equal(1L, presence.Actors.Single().ActorId);
    presence.Bind(Route(2));
    Check(!presence.Apply("one", 1, 2, actors));
    Equal(0, presence.Actors.Count);
});
Run("aoi.invalid-batch-is-atomic", () =>
{
    var presence = new WorldPresenceStore();
    presence.Bind(Route());
    presence.Apply("one", 1, 1, new[] { new WorldActorSnapshot { ActorId = 1 } });
    Throws<ArgumentException>(() => presence.Apply("one", 1, 2,
        new[] { new WorldActorSnapshot { ActorId = 2 }, new WorldActorSnapshot { ActorId = 2 } }));
    Equal(1L, presence.Actors.Single().ActorId);
});
Run("frames.missing-online-frame-does-not-advance", () =>
{
    using var inbox = new OrderedFrameInbox();
    using var session = BattleSession.CreateSmokeSession(inbox);
    Check(!session.TryAdvance());
    Equal(-1L, session.Frame);
    Check(inbox.Push(1, Array.Empty<ISimulationCommand>()));
    Check(!session.TryAdvance());
    Check(inbox.Push(0, Array.Empty<ISimulationCommand>()));
    Check(session.TryAdvance());
    Check(session.TryAdvance());
    Check(!inbox.Push(0, Array.Empty<ISimulationCommand>()));
    Equal(1L, session.Frame);
});
Run("frames.bound-window-and-reject-wrong-command", () =>
{
    using var inbox = new OrderedFrameInbox(2);
    Check(!inbox.Push(2, Array.Empty<ISimulationCommand>()));
    Throws<ArgumentException>(() => inbox.Push(0, new[] { new TestCommand(1, 1, 1) }));
});
Run("simulation.local-and-network-replay-identical", () =>
{
    var stateA = new TestState();
    var stateB = new TestState();
    var worldA = new FrameSimulationWorld(FP64.One / 20, new[] { new AccumulateSystem(stateA) }, stateA);
    var worldB = new FrameSimulationWorld(FP64.One / 20, new[] { new AccumulateSystem(stateB) }, stateB);
    using var inbox = new OrderedFrameInbox();
    using var online = new BattleSession(worldB, inbox);
    for (int frame = 0; frame < 100; frame++)
    {
        ISimulationCommand[] commands = { new TestCommand(frame, 2, frame), new TestCommand(frame, 1, frame) };
        worldA.Step(frame, commands);
        Array.Reverse(commands);
        inbox.Push(frame, commands);
        Check(online.TryAdvance());
        Equal(worldA.ComputeHash(), online.StateHash);
    }
    Check(worldA.ComputeHash() != StableHash64.Offset);
});
Run("codec.roundtrip-and-boundary-validation", () =>
{
    var codec = new FoundationPacketCodec();
    var packet = FoundationPacket.Create("hello");
    Check(codec.Encode(packet, out var data));
    var decoded = (FoundationPacket)codec.Decode(data, 0, data.Length, out var error);
    Equal(null, error);
    Equal("hello", decoded.Message);
    ReferencePool.Release(decoded);
    data[0] = 99;
    Equal(null, codec.Decode(data, 0, data.Length, out error));
    Check(error != null);
    data[0] = ProtocolVersion.Current;
    data[12] = 0xff;
    Equal(null, codec.Decode(data, 0, data.Length, out error));
    Check(error != null);
    Equal(null, codec.Decode(data, -1, 12, out error));
    packet.Message = new string('x', ProtocolVersion.MaxPacketBytes);
    Check(!codec.Encode(packet, out _));
    ReferencePool.Release(packet);
});
Run("platform.minigame-never-silently-uses-desktop-sockets", () =>
{
    var factory = new UnconfiguredMiniGameChannelFactory();
    Check(!factory.Supports(TransportKind.Tcp));
    Throws<PlatformNotSupportedException>(() => factory.Create("lobby", new ServiceEndpoint(), new FoundationPacketCodec()));
});
Run("network.tcp-real-loopback-roundtrip", () => TransportRoundtrip(false));
Run("network.kcp-real-loopback-roundtrip", () => TransportRoundtrip(true));
Run("network.sdk-datagram-kcp-real-loopback-roundtrip", () => TransportRoundtrip(true, true, true));
Run("network.sdk-datagram-kcp-reject-invalid-ticket", () => TransportRoundtrip(true, false, true));
Run("network.sdk-datagram-kcp-admission-and-limits", DatagramTransportTests.AdmissionAndLimits);
Run("network.kcp-reject-invalid-ticket", () => TransportRoundtrip(true, false));
foreach (var test in SessionConnectionTests.Cases()) Run(test.Name, test.Run);

Directory.CreateDirectory(".artifacts/validation");
File.WriteAllText(".artifacts/validation/foundation-tests.json",
    JsonSerializer.Serialize(new { passed = results.Count - failures, failed = failures, results }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"FOUNDATION_TESTS total={results.Count} passed={results.Count - failures} failed={failures}");
return failures == 0 ? 0 : 1;

void Run(string name, Action action)
{
    var clock = Stopwatch.StartNew();
    try { action(); results.Add(new { name, passed = true, ms = clock.ElapsedMilliseconds }); Console.WriteLine("PASS " + name); }
    catch (Exception error) { failures++; results.Add(new { name, passed = false, error = error.ToString() }); Console.WriteLine("FAIL " + name + ": " + error); }
}
static void Check(bool value) { if (!value) throw new Exception("Assertion failed."); }
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected '{expected}', got '{actual}'."); }
static void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception("Expected exception: " + typeof(T).Name);
}
static WorldRoute Route(long epoch = 1) => new()
{ HomeRealmId = "home", WorldClusterId = "global", InstanceId = "one", SceneId = "1001", Epoch = epoch };
static SessionCoordinator WorldSession()
{
    var session = new SessionCoordinator();
    session.Authenticated(1);
    session.EnterWorld(Route());
    return session;
}
static BattleTicket Ticket() => new()
{
    RoomId = "room", Ticket = "ephemeral", ExpiresAtUnixSeconds = 100, SimulationVersion = "1",
    ConfigHash = "config", MapHash = "map", Endpoint = new ServiceEndpoint { Transport = TransportKind.Kcp, Host = "127.0.0.1", Port = 7778 }
};
static void TransportRoundtrip(bool useKcp, bool accepted = true, bool platformDatagram = false)
{
    GameFrameworkEntry.Shutdown();
    INetworkServerChannel server = useKcp
        ? new KcpServerChannelProvider(new KcpServerOptions
        { DriveMode = NetworkDriveMode.HostTick, AuthTokenValidator = (token, _) => token == "valid" }).CreateChannel("test", new FoundationPacketCodec())
        : new TcpServerChannelProvider().CreateChannel("test", new FoundationPacketCodec());
    using (server)
    {
        server.PacketReceived += (session, packet) =>
        {
            if (packet is FoundationPacket message && message.Message == "hello")
                session.Send(FoundationPacket.Create("ready"));
        };
        server.Start(IPAddress.Loopback, 0);
        var endpoint = (IPEndPoint)server.LocalEndPoint;
        var manager = GameFrameworkEntry.GetModule<INetworkManager>();
        var factory = new DesktopChannelFactory(manager);
        if (platformDatagram)
            manager.RegisterTransport(new DatagramKcpTransportProvider("test-sdk-kcp",
                (address, port) => new LoopbackDatagramSocket(address, port)));
        var channel = platformDatagram ? manager.CreateNetworkChannel("test", "test-sdk-kcp", new FoundationPacketCodec()) :
            factory.Create("test", new ServiceEndpoint
        { Host = "127.0.0.1", Port = endpoint.Port, Transport = useKcp ? TransportKind.Kcp : TransportKind.Tcp }, new FoundationPacketCodec());
        bool received = false;
        bool sent = false;
        channel.SetDefaultHandler((_, packet) => received = packet is FoundationPacket message && message.Message == "ready");
        channel.Connect(IPAddress.Loopback, endpoint.Port, new KcpConnectData { AuthToken = accepted ? "valid" : "invalid" });
        var clock = Stopwatch.StartNew();
        double last = 0;
        while (clock.ElapsedMilliseconds < (accepted ? 5000 : 700) && !received)
        {
            double now = clock.Elapsed.TotalSeconds;
            float elapsed = (float)(now - last);
            last = now;
            server.Update(elapsed, elapsed);
            GameFrameworkEntry.Update(elapsed, elapsed);
            if (channel.Connected && !sent) { channel.Send(FoundationPacket.Create("hello")); sent = true; }
            Thread.Sleep(2);
        }
        try
        {
            if (accepted) Check(received);
            else { Check(!received); Equal(0, server.SessionCount); }
        }
        finally { manager.DestroyNetworkChannel("test"); GameFrameworkEntry.Shutdown(); }
    }
}

sealed class TestModule : IGameModule
{
    private readonly List<string> log;
    public string Id { get; }
    public IReadOnlyList<string> Dependencies { get; }
    public bool FailStart, FailStop;
    public TestModule(string id, List<string> log, params string[] dependencies) { Id = id; this.log = log; Dependencies = dependencies; }
    public void Start(ServiceRegistry services) { log.Add("+" + Id); if (FailStart) throw new InvalidOperationException("start"); }
    public void Tick(float elapsedSeconds) { }
    public void Stop() { log.Add("-" + Id); if (FailStop) throw new InvalidOperationException("stop"); }
}
sealed class TestCommand : ISimulationCommand
{
    public long FrameId { get; }
    public long ActorId { get; }
    public long Sequence { get; }
    public TestCommand(long frame, long actor, long sequence) { FrameId = frame; ActorId = actor; Sequence = sequence; }
}
sealed class TestState : ISimulationStateStore
{
    public FP64 Value;
    public ulong ComputeHash() => StableHash64.Add(StableHash64.Offset, Value);
    public ISimulationSnapshot Capture(long frameId) => throw new NotSupportedException("Not part of this test.");
    public void Restore(ISimulationSnapshot snapshot) => throw new NotSupportedException("Not part of this test.");
}
sealed class AccumulateSystem : IFrameSimulationSystem
{
    private readonly TestState state;
    public SimulationPhase Phase => SimulationPhase.ApplyCommands;
    public int Order => 0;
    public AccumulateSystem(TestState state) { this.state = state; }
    public void Execute(FrameSimulationContext context)
    {
        foreach (var command in context.Commands)
            state.Value = state.Value / 2 + FP64.FromInt((int)command.ActorId);
    }
}
