using System.Diagnostics;
using System.Net;
using Framework;
using Framework.Network;
using Framework.Network.Kcp;
using InsectSpace.Contracts;
using InsectSpace.Foundation;
using InsectSpace.Network;
using InsectSpace.Simulation;

internal static class SessionConnectionTests
{
    public static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("sessions.real-lobby-to-kcp-and-return", RealLobbyToBattle);
        yield return ("sessions.local-battle-opens-no-network-channel", LocalBattle);
        yield return ("sessions.transport-connect-is-not-login", LoginAdmission);
        yield return ("sessions.transport-connect-is-not-battle-admission", BattleAdmission);
        yield return ("sessions.compatibility-mismatch-is-atomic", CompatibilityMismatch);
        yield return ("sessions.cancel-join-rejects-late-admission", CancelJoin);
        yield return ("sessions.ticket-is-snapshotted-before-connect", TicketSnapshot);
        yield return ("sessions.timeout-preserves-lobby-without-local-fallback", JoinTimeout);
        yield return ("sessions.expired-ticket-is-not-admitted", ExpiredTicket);
        yield return ("sessions.lobby-loss-recovers-same-player-with-new-route", LobbyRecovery);
        yield return ("sessions.battle-loss-keeps-lobby-for-world-recovery", BattleRecovery);
        yield return ("sessions.suspend-closes-both-channels", Suspend);
        yield return ("sessions.stale-dns-cannot-dial-new-session", StaleDns);
        yield return ("sessions.dns-failure-remains-signed-out", DnsFailure);
        yield return ("sessions.failed-ipv6-falls-back-to-ipv4", AddressFallback);
        yield return ("sessions.partial-platform-failure-rolls-back-channel", PartialPlatformFailure);
        yield return ("sessions.dispose-preserves-foreign-channels", DisposeOwnership);
        yield return ("sessions.host-thread-ownership-is-enforced", ThreadOwnership);
    }

    private static void RealLobbyToBattle()
    {
        using var f = new Fixture();
        f.Login();
        Check(f.Session.Phase == SessionPhase.World && f.Connections.LobbyAuthenticated);
        var lobby = f.Connections.LobbyConnection.Value;
        var battle = f.StartBattle();
        f.PumpUntil(() => f.Session.Phase == SessionPhase.Battle);
        Check(f.Manager.NetworkChannelCount == 2);
        Check(f.Session.ActiveBattle == BattleKind.Online);
        Check(f.Session.Route.InstanceId == "instance-one");
        f.Connections.CompleteBattleReturn(battle);
        Check(f.Session.Phase == SessionPhase.World);
        Check(f.Connections.LobbyConnection.Value.Equals(lobby));
        Check(f.Manager.NetworkChannelCount == 1 && f.Connections.LobbyAuthenticated);
    }

    private static void LocalBattle()
    {
        using var f = new Fixture();
        f.Session.Authenticated(1);
        f.Session.EnterWorld(Fixture.Route(1));
        f.Connections.BeginLocalEncounter();
        using (var battle = BattleSession.CreateSmokeSession(new EmptyLocalInputSource()))
        {
            for (int i = 0; i < 20; i++) Check(battle.TryAdvance());
            Check(battle.Frame == 19);
        }
        f.Connections.CompleteLocalBattle();
        Check(f.Session.Phase == SessionPhase.World && f.Manager.NetworkChannelCount == 0);
        Check(!f.Connections.LobbyAuthenticated);
        Throws<InvalidOperationException>(() => f.StartBattle());
    }

    private static void LoginAdmission()
    {
        using var f = new Fixture { AutoLogin = false };
        var id = f.ConnectLobby();
        f.PumpUntil(() => f.LobbyConnected);
        Check(f.Session.Phase == SessionPhase.SignedOut && !f.Connections.LobbyAuthenticated);
        Throws<InvalidOperationException>(() => f.Connections.ConfirmWorldRoute(id, Fixture.Route(1)));
        f.Connections.ConfirmLogin(id, 1);
        Check(f.Session.Phase == SessionPhase.Lobby);
        Throws<InvalidOperationException>(() => f.Connections.ConfirmLogin(id, 1));
        f.Connections.ConfirmWorldRoute(id, Fixture.Route(1));
        Check(f.Session.Phase == SessionPhase.World);
    }

    private static void BattleAdmission()
    {
        using var f = new Fixture { AutoBattleAdmission = false };
        f.Login();
        var id = f.StartBattle();
        f.PumpUntil(() => f.Connections.BattleTransportConnected);
        Check(f.Session.Phase == SessionPhase.ConnectingBattle);
        Throws<InvalidOperationException>(() => f.Connections.ConfirmBattleReady(id, "wrong-room"));
        Throws<InvalidOperationException>(() => f.Connections.CompleteBattleReturn(id));
        f.Connections.ConfirmBattleReady(id, "room-one");
        Check(f.Session.Phase == SessionPhase.Battle);
    }

    private static void CompatibilityMismatch()
    {
        using var f = new Fixture();
        f.Login();
        var ticket = f.Ticket();
        ticket.ConfigHash = "wrong-config";
        Throws<InvalidOperationException>(() => f.Connections.BeginOnlineEncounter(ticket, Fixture.Compatibility, f.Codec));
        Check(f.Session.Phase == SessionPhase.World && f.Manager.NetworkChannelCount == 1);
    }

    private static void CancelJoin()
    {
        using var f = new Fixture { AutoBattleAdmission = false };
        f.Login();
        var first = f.StartBattle();
        f.Connections.CancelBattleJoin(first);
        Check(f.Session.Phase == SessionPhase.World && f.Manager.NetworkChannelCount == 1);
        var second = f.StartBattle();
        Check(!first.Equals(second));
        Throws<InvalidOperationException>(() => f.Connections.ConfirmBattleReady(first, "room-one"));
        f.PumpUntil(() => f.Connections.BattleTransportConnected);
        f.Connections.ConfirmBattleReady(second, "room-one");
        Check(f.Session.Phase == SessionPhase.Battle);
    }

    private static void TicketSnapshot()
    {
        using var f = new Fixture();
        f.Login();
        var ticket = f.Ticket();
        var id = f.Connections.BeginOnlineEncounter(ticket, Fixture.Compatibility, f.Codec);
        ticket.Endpoint.Host = "invalid.example";
        ticket.Endpoint.Port = 1;
        ticket.Ticket = "mutated";
        ticket.RoomId = "mutated";
        ticket.ExpiresAtUnixSeconds = 0;
        f.PumpUntil(() => f.Session.Phase == SessionPhase.Battle);
        f.Connections.CompleteBattleReturn(id);
        Check(f.Session.Phase == SessionPhase.World);
    }

    private static void JoinTimeout()
    {
        using var f = new Fixture { AutoBattleAdmission = false };
        f.Login();
        f.StartBattle();
        f.PumpUntil(() => f.Connections.BattleTransportConnected);
        f.Connections.Tick(4);
        Check(f.Connections.LastFailure == ConnectionFailure.Timeout);
        Check(f.Session.Phase == SessionPhase.World && f.Session.ActiveBattle == null);
        Check(f.Connections.LobbyAuthenticated && f.Manager.NetworkChannelCount == 1);
    }

    private static void ExpiredTicket()
    {
        using var f = new Fixture { AutoBattleAdmission = false };
        f.Login();
        var id = f.StartBattle();
        f.PumpUntil(() => f.Connections.BattleTransportConnected);
        f.UnixSeconds = 100;
        Throws<InvalidOperationException>(() => f.Connections.ConfirmBattleReady(id, "room-one"));
        f.Connections.Tick(0);
        Check(f.Session.Phase == SessionPhase.World && f.Manager.NetworkChannelCount == 1);
    }

    private static void LobbyRecovery()
    {
        using var f = new Fixture();
        f.Login();
        var old = f.Connections.LobbyConnection.Value;
        f.StartBattle();
        f.PumpUntil(() => f.Session.Phase == SessionPhase.Battle);
        f.CloseClient(SessionChannel.Lobby);
        f.PumpUntil(() => f.Session.Phase == SessionPhase.Recovering);
        Check(f.Manager.NetworkChannelCount == 0);
        Check(f.Session.PlayerId == 1 && f.Session.Route.Epoch == 1);
        f.AutoLogin = false;
        f.LobbyConnected = false;
        var next = f.ConnectLobby();
        f.PumpUntil(() => f.LobbyConnected);
        Throws<InvalidOperationException>(() => f.Connections.ConfirmLogin(old, 1));
        Throws<InvalidOperationException>(() => f.Connections.ConfirmLogin(next, 2));
        f.Connections.ConfirmLogin(next, 1);
        Throws<InvalidOperationException>(() => f.Connections.ConfirmWorldRoute(next, Fixture.Route(1)));
        f.Connections.ConfirmWorldRoute(next, Fixture.Route(2));
        Check(f.Session.Phase == SessionPhase.World && f.Session.Route.Epoch == 2);
    }

    private static void BattleRecovery()
    {
        using var f = new Fixture();
        f.Login();
        var lobby = f.Connections.LobbyConnection.Value;
        f.StartBattle();
        f.PumpUntil(() => f.Session.Phase == SessionPhase.Battle);
        f.CloseClient(SessionChannel.Battle);
        f.PumpUntil(() => f.Session.Phase == SessionPhase.Recovering);
        Check(f.Connections.LobbyAuthenticated && f.Manager.NetworkChannelCount == 1);
        Throws<InvalidOperationException>(() => f.StartBattle());
        f.Connections.ConfirmWorldRoute(lobby, Fixture.Route(2));
        Check(f.Session.Phase == SessionPhase.World);
    }

    private static void Suspend()
    {
        using var f = new Fixture();
        f.Login();
        f.StartBattle();
        f.PumpUntil(() => f.Session.Phase == SessionPhase.Battle);
        f.Connections.Suspend();
        Check(f.Session.Phase == SessionPhase.Recovering && f.Manager.NetworkChannelCount == 0);
        Check(f.Connections.LastFailure == ConnectionFailure.Suspended);
    }

    private static void StaleDns()
    {
        var resolver = new DeferredResolver();
        using var f = new Fixture(resolver);
        var old = f.ConnectLobby();
        f.Connections.SignOut();
        var current = f.ConnectLobby();
        resolver.Complete(0);
        f.Connections.Tick(0);
        Check(!f.LobbyConnected && f.Manager.NetworkChannelCount == 1);
        Throws<InvalidOperationException>(() => f.Connections.ConfirmLogin(old, 1));
        resolver.Complete(1);
        f.PumpUntil(() => f.Session.Phase == SessionPhase.World);
        Check(f.Connections.LobbyConnection.Value.Equals(current));
    }

    private static void DnsFailure()
    {
        var resolver = new DeferredResolver();
        using var f = new Fixture(resolver);
        f.ConnectLobby();
        resolver.Fail(0);
        f.Connections.Tick(0);
        Check(f.Session.Phase == SessionPhase.SignedOut && f.Manager.NetworkChannelCount == 0);
        Check(f.Connections.LastFailure == ConnectionFailure.Resolution);
    }

    private static void DisposeOwnership()
    {
        using var f = new Fixture();
        f.Login();
        f.Manager.CreateNetworkChannel("foreign", "tcp", f.Codec);
        f.Connections.Dispose();
        Check(f.Manager.NetworkChannelCount == 1 && f.Manager.HasNetworkChannel("foreign"));
        Check(f.Session.Phase == SessionPhase.SignedOut);
        Throws<ObjectDisposedException>(() => f.Connections.Tick(0));
    }

    private static void AddressFallback()
    {
        using var f = new Fixture(new DualStackResolver());
        f.Login();
        Check(f.Session.Phase == SessionPhase.World && f.Connections.LobbyAuthenticated);
        Check(f.Manager.NetworkChannelCount == 1);
        Check(((IPEndPoint)f.Manager.GetAllNetworkChannels()[0].RemoteEndPoint).Address.Equals(IPAddress.Loopback));
    }

    private static void PartialPlatformFailure()
    {
        using var f = new Fixture(decorateFactory: factory => new FailingBattleFactory(factory));
        f.Login();
        Throws<InvalidOperationException>(() => f.StartBattle());
        Check(f.Session.Phase == SessionPhase.World);
        Check(f.Connections.LobbyAuthenticated && f.Manager.NetworkChannelCount == 1);
    }

    private static void ThreadOwnership()
    {
        using var f = new Fixture();
        Task.Run(() => Throws<InvalidOperationException>(() => f.Connections.Tick(0))).GetAwaiter().GetResult();
        Check(f.Manager.NetworkChannelCount == 0);
    }

    private static void Check(bool value)
    { if (!value) throw new InvalidOperationException("Session connection assertion failed."); }

    private static void Throws<T>(Action run) where T : Exception
    {
        try { run(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    private sealed class DeferredResolver : IChannelAddressResolver
    {
        private readonly List<TaskCompletionSource<IPAddress[]>> pending = new();
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellation)
        {
            var completion = new TaskCompletionSource<IPAddress[]>();
            pending.Add(completion);
            return completion.Task;
        }
        public void Complete(int index) => pending[index].SetResult(new[] { IPAddress.Loopback });
        public void Fail(int index) => pending[index].SetException(new InvalidOperationException("Fixture DNS failure."));
    }

    private sealed class DualStackResolver : IChannelAddressResolver
    {
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellation) =>
            Task.FromResult(new[] { IPAddress.IPv6Loopback, IPAddress.Loopback });
    }

    private sealed class FailingBattleFactory : IPlatformChannelFactory
    {
        private readonly IPlatformChannelFactory inner;
        public FailingBattleFactory(IPlatformChannelFactory inner) { this.inner = inner; }
        public bool Supports(TransportKind kind) => inner.Supports(kind);
        public INetworkChannel Create(string name, ServiceEndpoint endpoint, INetworkPacketCodec codec)
        {
            var channel = inner.Create(name, endpoint, codec);
            if (endpoint.Transport == TransportKind.Kcp) throw new InvalidOperationException("Fixture platform failure after allocation.");
            return channel;
        }
    }

    private sealed class Fixture : IDisposable
    {
        public static readonly BattleCompatibility Compatibility = new("sim-1", "config-1", "map-1");
        public readonly SessionCoordinator Session = new();
        public readonly INetworkManager Manager;
        public readonly SessionConnections Connections;
        public readonly FoundationPacketCodec Codec = new();
        public bool AutoLogin = true;
        public bool AutoBattleAdmission = true;
        public bool LobbyConnected;
        public long UnixSeconds = 10;
        private readonly INetworkServerChannel tcp;
        private readonly INetworkServerChannel kcp;

        public Fixture(IChannelAddressResolver resolver = null, Func<IPlatformChannelFactory, IPlatformChannelFactory> decorateFactory = null)
        {
            GameFrameworkEntry.Shutdown();
            Manager = GameFrameworkEntry.GetModule<INetworkManager>();
            IPlatformChannelFactory factory = new DesktopChannelFactory(Manager);
            if (decorateFactory != null) factory = decorateFactory(factory);
            Connections = new SessionConnections(Session, Manager, factory,
                resolver ?? new DesktopAddressResolver(), () => UnixSeconds, 3);
            tcp = new TcpServerChannelProvider().CreateChannel("lobby-fixture", Codec);
            kcp = new KcpServerChannelProvider(new KcpServerOptions
            {
                DriveMode = NetworkDriveMode.HostTick,
                AuthTokenValidator = (token, _) => token == "valid-fixture-ticket"
            }).CreateChannel("battle-fixture", Codec);
            tcp.PacketReceived += (peer, packet) =>
            {
                if (packet is FoundationPacket p && p.Message == "fixture.identity")
                    peer.Send(FoundationPacket.Create("fixture.identity-confirmed"));
            };
            kcp.PacketReceived += (peer, packet) =>
            {
                if (packet is FoundationPacket p && p.Message == "fixture.room.join")
                    peer.Send(FoundationPacket.Create("fixture.room.admitted"));
            };
            tcp.Start(IPAddress.Loopback, 0);
            kcp.Start(IPAddress.Loopback, 0);
            Connections.Changed += signal =>
            {
                if (signal.Kind != ConnectionSignalKind.TransportConnected) return;
                if (signal.Connection.Channel == SessionChannel.Lobby) LobbyConnected = true;
                Connections.Send(signal.Connection, FoundationPacket.Create(
                    signal.Connection.Channel == SessionChannel.Lobby ? "fixture.identity" : "fixture.room.join"));
            };
            Connections.PacketReceived += received =>
            {
                if (!(received.Packet is FoundationPacket packet)) return;
                if (packet.Message == "fixture.identity-confirmed" && AutoLogin)
                {
                    Connections.ConfirmLogin(received.Connection, 1);
                    Connections.ConfirmWorldRoute(received.Connection, Route(1));
                }
                if (packet.Message == "fixture.room.admitted" && AutoBattleAdmission)
                    Connections.ConfirmBattleReady(received.Connection, "room-one");
            };
        }

        public ConnectionId ConnectLobby() => Connections.ConnectLobby(new ServiceEndpoint
        {
            Host = "localhost", Port = ((IPEndPoint)tcp.LocalEndPoint).Port, Transport = TransportKind.Tcp
        }, Codec);
        public void Login()
        {
            ConnectLobby();
            PumpUntil(() => Session.Phase == SessionPhase.World);
        }
        public ConnectionId StartBattle() => Connections.BeginOnlineEncounter(Ticket(), Compatibility, Codec);
        public BattleTicket Ticket() => new()
        {
            RoomId = "room-one", Ticket = "valid-fixture-ticket", SimulationVersion = "sim-1",
            ConfigHash = "config-1", MapHash = "map-1", ExpiresAtUnixSeconds = 100,
            Endpoint = new ServiceEndpoint { Host = "127.0.0.1", Port = ((IPEndPoint)kcp.LocalEndPoint).Port, Transport = TransportKind.Kcp }
        };
        public static WorldRoute Route(long epoch) => new()
        { HomeRealmId = "home", WorldClusterId = "shared-cluster", SceneId = "world", InstanceId = "instance-one", Epoch = epoch };
        public void CloseClient(SessionChannel channel) => Manager.GetAllNetworkChannels()
            .Single(c => c.Name.StartsWith("InsectSpace." + channel + ".", StringComparison.Ordinal)).Close();

        public void PumpUntil(Func<bool> condition)
        {
            var watch = Stopwatch.StartNew();
            while (!condition() && watch.ElapsedMilliseconds < 6000)
            {
                Connections.Tick(0.002);
                tcp.Update(0.002f, 0.002f);
                kcp.Update(0.002f, 0.002f);
                GameFrameworkEntry.Update(0.002f, 0.002f);
                if (!condition() && Connections.LastFailure != ConnectionFailure.None)
                    throw new InvalidOperationException("Fixture session failed: " + Connections.LastFailure + " in " + Session.Phase);
                Thread.Sleep(2);
            }
            Check(condition());
        }
        public void Dispose()
        {
            Connections.Dispose();
            tcp.Dispose();
            kcp.Dispose();
            GameFrameworkEntry.Shutdown();
        }
    }
}
