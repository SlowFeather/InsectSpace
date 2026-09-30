using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Framework.Network;
using Framework.Network.Kcp;
using InsectSpace.Contracts;
using InsectSpace.Foundation;

namespace InsectSpace.Network
{
    // All public methods, Tick and GF dispatch must run on the same host thread.
    // Only DNS completions use a worker thread, and they enqueue immutable results.
    public sealed class SessionConnections : IDisposable
    {
        private sealed class Slot
        {
            public ConnectionId Id;
            public INetworkChannel Channel;
            public ServiceEndpoint Endpoint;
            public INetworkPacketCodec Codec;
            public object ConnectData;
            public IPAddress[] Addresses;
            public int NextAddress;
            public bool RetryQueued;
            public double Deadline;
            public bool TransportConnected;
            public bool Ready;
            public string RoomId;
            public long TicketExpiry;
        }

        private readonly struct Resolution
        {
            public readonly Slot Slot;
            public readonly IPAddress[] Addresses;
            public readonly bool Failed;
            public Resolution(Slot slot, IPAddress[] addresses, bool failed)
            { Slot = slot; Addresses = addresses; Failed = failed; }
        }

        private static long nextGeneration;
        private readonly SessionCoordinator session;
        private readonly INetworkManager manager;
        private readonly IPlatformChannelFactory factory;
        private readonly IChannelAddressResolver resolver;
        private readonly Func<long> unixSeconds;
        private readonly double timeoutSeconds;
        private readonly int ownerThread;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly ConcurrentQueue<Resolution> resolutions = new ConcurrentQueue<Resolution>();
        private Slot lobby;
        private Slot battle;
        private double elapsed;
        private bool disposed;

        public event Action<ConnectionSignal> Changed;
        public event Action<SessionPacket> PacketReceived;
        public bool LobbyAuthenticated => lobby != null && lobby.Ready && lobby.Channel.Connected;
        public bool BattleTransportConnected => battle != null && battle.TransportConnected && battle.Channel.Connected;
        public ConnectionId? LobbyConnection => lobby?.Id;
        public ConnectionId? BattleConnection => battle?.Id;
        public ConnectionFailure LastFailure { get; private set; }

        public SessionConnections(SessionCoordinator session, INetworkManager manager,
            IPlatformChannelFactory factory, IChannelAddressResolver resolver, Func<long> unixSeconds, double timeoutSeconds = 15)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
            this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            this.unixSeconds = unixSeconds ?? throw new ArgumentNullException(nameof(unixSeconds));
            if (double.IsNaN(timeoutSeconds) || double.IsInfinity(timeoutSeconds) || timeoutSeconds <= 0 || timeoutSeconds > 120)
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            this.timeoutSeconds = timeoutSeconds;
            ownerThread = Thread.CurrentThread.ManagedThreadId;
            manager.NetworkConnected += OnConnected;
            manager.NetworkClosed += OnClosed;
            manager.NetworkError += OnError;
            manager.NetworkCustomError += OnProtocolError;
        }

        public ConnectionId ConnectLobby(ServiceEndpoint endpoint, INetworkPacketCodec codec)
        {
            AssertOwner();
            if (lobby != null || (session.Phase != SessionPhase.SignedOut && session.Phase != SessionPhase.Recovering))
                throw new InvalidOperationException("Sign out or enter recovery before connecting another lobby.");
            if (endpoint == null || (endpoint.Transport != TransportKind.Tcp &&
                endpoint.Transport != TransportKind.SecureWebSocket && endpoint.Transport != TransportKind.WeChatTcp))
                throw new ArgumentException("Lobby requires a TCP or secure gateway endpoint.");
            lobby = CreateSlot(SessionChannel.Lobby, endpoint, codec);
            var id = lobby.Id;
            Resolve(lobby);
            return id;
        }

        // Invoke only after the protocol/identity layer validates a response for this connection.
        public void ConfirmLogin(ConnectionId id, long playerId)
        {
            AssertOwner();
            var slot = Require(id, SessionChannel.Lobby);
            if (!slot.TransportConnected || !slot.Channel.Connected || slot.Ready)
                throw new InvalidOperationException("Lobby is not awaiting authentication.");
            if (session.Phase == SessionPhase.Recovering) session.Reauthenticated(playerId);
            else session.Authenticated(playerId);
            slot.Ready = true;
            slot.Deadline = double.PositiveInfinity;
            Notify(slot, ConnectionSignalKind.Authenticated);
        }

        public void BeginLocalEncounter()
        {
            AssertOwner();
            if (battle != null) throw new InvalidOperationException("A network battle already owns the encounter.");
            session.BeginLocalEncounter();
        }

        public void ConfirmWorldRoute(ConnectionId lobbyId, WorldRoute route)
        {
            AssertOwner();
            var slot = Require(lobbyId, SessionChannel.Lobby);
            if (!slot.Ready || !slot.Channel.Connected)
                throw new InvalidOperationException("Only an authenticated lobby can admit a world route.");
            session.EnterWorld(route);
        }

        public ConnectionId BeginOnlineEncounter(BattleTicket ticket, BattleCompatibility compatibility, INetworkPacketCodec codec)
        {
            AssertOwner();
            if (!LobbyAuthenticated || battle != null) throw new InvalidOperationException("An authenticated lobby and no active battle are required.");
            if (compatibility == null) throw new ArgumentNullException(nameof(compatibility));
            compatibility.Validate(ticket);
            ValidateEndpoint(ticket.Endpoint);
            if (codec == null) throw new ArgumentNullException(nameof(codec));
            session.BeginOnlineEncounter(ticket, unixSeconds());
            try
            {
                battle = CreateSlot(SessionChannel.Battle, ticket.Endpoint, codec);
                battle.RoomId = ticket.RoomId;
                battle.TicketExpiry = ticket.ExpiresAtUnixSeconds;
                battle.ConnectData = new KcpConnectData { AuthToken = ticket.Ticket };
                var id = battle.Id;
                Resolve(battle);
                return id;
            }
            catch
            {
                Retire(ref battle);
                if (session.Phase == SessionPhase.ConnectingBattle) session.ReturnToWorld();
                throw;
            }
        }

        // Transport connection is not permission to advance frames. Wait for room admission
        // and an initial authoritative frame/snapshot acknowledgement from the battle service.
        public void ConfirmBattleReady(ConnectionId id, string roomId)
        {
            AssertOwner();
            var slot = Require(id, SessionChannel.Battle);
            if (!slot.TransportConnected || !slot.Channel.Connected || slot.Ready || slot.RoomId != roomId || slot.TicketExpiry <= unixSeconds())
                throw new InvalidOperationException("Battle admission is missing, stale or expired.");
            session.BattleConnected();
            slot.Ready = true;
            slot.Deadline = double.PositiveInfinity;
            Notify(slot, ConnectionSignalKind.BattleReady);
        }

        public void Send(ConnectionId id, Packet packet)
        {
            AssertOwner();
            var slot = Require(id, id.Channel);
            if (packet == null) throw new ArgumentNullException(nameof(packet));
            if (!slot.TransportConnected || !slot.Channel.Connected)
                throw new InvalidOperationException("Transport is not connected.");
            slot.Channel.Send(packet);
        }

        // Cancelling a pending join is safe. Active online battles require server-approved return.
        public void CancelBattleJoin(ConnectionId id)
        {
            AssertOwner();
            Require(id, SessionChannel.Battle);
            if (session.Phase != SessionPhase.ConnectingBattle) throw new InvalidOperationException("Only a pending battle join may be cancelled.");
            Retire(ref battle);
            session.ReturnToWorld();
        }

        public void CompleteBattleReturn(ConnectionId id)
        {
            AssertOwner();
            var slot = Require(id, SessionChannel.Battle);
            if (!slot.Ready || session.Phase != SessionPhase.Battle) throw new InvalidOperationException("There is no admitted battle to return from.");
            Retire(ref battle);
            session.ReturnToWorld();
        }

        public void CompleteLocalBattle()
        {
            AssertOwner();
            if (session.ActiveBattle != BattleKind.Local || battle != null)
                throw new InvalidOperationException("There is no local battle to return from.");
            session.ReturnToWorld();
        }

        public void Tick(double elapsedSeconds)
        {
            AssertOwner();
            if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds) || elapsedSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            elapsed += elapsedSeconds;
            if (lobby != null && !lobby.Ready && elapsed >= lobby.Deadline) Fail(lobby, ConnectionFailure.Timeout);
            if (battle != null && !battle.Ready && (elapsed >= battle.Deadline || battle.TicketExpiry <= unixSeconds()))
                Fail(battle, ConnectionFailure.Timeout);
            while (resolutions.TryDequeue(out var resolution))
            {
                var slot = resolution.Slot;
                if (!IsCurrent(slot)) continue;
                if (resolution.Failed || resolution.Addresses == null || resolution.Addresses.Length == 0)
                { Fail(slot, ConnectionFailure.Resolution); continue; }
                slot.Addresses = (IPAddress[])resolution.Addresses.Clone();
                slot.RetryQueued = true;
            }
            StartNextAddress(lobby);
            StartNextAddress(battle);
        }

        public void Suspend()
        {
            AssertOwner();
            var current = lobby ?? battle;
            Retire(ref battle);
            Retire(ref lobby);
            session.ConnectionLost();
            LastFailure = ConnectionFailure.Suspended;
            if (current != null) Notify(current, ConnectionSignalKind.Failed, LastFailure);
        }

        public void SignOut()
        {
            AssertOwner();
            Retire(ref battle);
            Retire(ref lobby);
            session.SignOut();
        }

        public void Dispose()
        {
            if (disposed) return;
            AssertOwner();
            disposed = true;
            lifetime.Cancel();
            manager.NetworkConnected -= OnConnected;
            manager.NetworkClosed -= OnClosed;
            manager.NetworkError -= OnError;
            manager.NetworkCustomError -= OnProtocolError;
            Retire(ref battle);
            Retire(ref lobby);
            while (resolutions.TryDequeue(out _)) { }
            session.SignOut();
            Changed = null;
            PacketReceived = null;
            lifetime.Dispose();
        }

        private Slot CreateSlot(SessionChannel kind, ServiceEndpoint endpoint, INetworkPacketCodec codec)
        {
            ValidateEndpoint(endpoint);
            if (codec == null) throw new ArgumentNullException(nameof(codec));
            var copy = new ServiceEndpoint { Transport = endpoint.Transport, Host = endpoint.Host, Port = endpoint.Port, Path = endpoint.Path };
            var id = new ConnectionId(kind, Interlocked.Increment(ref nextGeneration));
            var slot = new Slot { Id = id, Endpoint = copy, Codec = codec, Deadline = elapsed + timeoutSeconds };
            CreateChannel(slot);
            LastFailure = ConnectionFailure.None;
            return slot;
        }

        private void CreateChannel(Slot slot)
        {
            string name = "InsectSpace." + slot.Id.Channel + "." + slot.Id.Generation;
            if (manager.HasNetworkChannel(name)) throw new InvalidOperationException("Connection channel name is already owned.");
            try
            {
                var channel = factory.Create(name, slot.Endpoint, slot.Codec);
                if (channel == null || channel.Name != name || !ReferenceEquals(manager.GetNetworkChannel(name), channel))
                    throw new InvalidOperationException("Platform factory returned an unowned channel.");
                slot.Channel = channel;
                channel.SetDefaultHandler((_, packet) =>
                {
                    if (IsCurrent(slot) && ReferenceEquals(slot.Channel, channel))
                        PacketReceived?.Invoke(new SessionPacket(slot.Id, packet));
                });
            }
            catch
            {
                slot.Channel = null;
                // This generation's name was absent before the factory call.
                if (manager.HasNetworkChannel(name)) manager.DestroyNetworkChannel(name);
                throw;
            }
        }

        private void StartNextAddress(Slot slot)
        {
            if (!IsCurrent(slot) || !slot.RetryQueued) return;
            slot.RetryQueued = false;
            try
            {
                if (slot.NextAddress > 0)
                {
                    DestroyChannel(slot);
                    CreateChannel(slot);
                }
                while (slot.NextAddress < slot.Addresses.Length && slot.Addresses[slot.NextAddress] == null)
                    slot.NextAddress++;
                if (slot.NextAddress == slot.Addresses.Length) { Fail(slot, ConnectionFailure.Resolution); return; }
                slot.Channel.Connect(slot.Addresses[slot.NextAddress++], slot.Endpoint.Port, slot.ConnectData);
            }
            catch
            {
                if (!QueueNextAddress(slot)) Fail(slot, ConnectionFailure.Transport);
            }
        }

        private bool QueueNextAddress(Slot slot)
        {
            if (slot.TransportConnected || slot.Addresses == null || slot.NextAddress >= slot.Addresses.Length)
                return false;
            slot.RetryQueued = true;
            return true;
        }

        private void Resolve(Slot slot)
        {
            var cancellation = lifetime.Token;
            Task<IPAddress[]> pending;
            try { pending = resolver.ResolveAsync(slot.Endpoint.Host, cancellation); }
            catch { resolutions.Enqueue(new Resolution(slot, null, true)); return; }
            if (pending == null) { resolutions.Enqueue(new Resolution(slot, null, true)); return; }
            pending.ContinueWith(task =>
            {
                // Observe faulted DNS tasks even when their connection was cancelled.
                var ignoredError = task.Exception;
                if (cancellation.IsCancellationRequested) return;
                resolutions.Enqueue(new Resolution(slot, task.Status == TaskStatus.RanToCompletion ? task.Result : null,
                    task.Status != TaskStatus.RanToCompletion));
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private void ValidateEndpoint(ServiceEndpoint endpoint)
        {
            if (endpoint == null || string.IsNullOrWhiteSpace(endpoint.Host) || endpoint.Port < 1 || endpoint.Port > 65535)
                throw new ArgumentException("A valid service endpoint is required.");
            if (!factory.Supports(endpoint.Transport))
                throw new PlatformNotSupportedException("The selected platform adapter cannot provide " + endpoint.Transport);
        }

        private Slot Require(ConnectionId id, SessionChannel kind)
        {
            var slot = kind == SessionChannel.Lobby ? lobby : battle;
            if (slot == null || id.Channel != kind || !slot.Id.Equals(id))
                throw new InvalidOperationException("Rejected stale or foreign connection ID.");
            return slot;
        }

        private bool IsCurrent(Slot slot) => !disposed && slot != null &&
            (ReferenceEquals(slot, lobby) || ReferenceEquals(slot, battle));

        private Slot Find(INetworkChannel channel)
        {
            if (disposed) return null;
            if (lobby != null && ReferenceEquals(lobby.Channel, channel)) return lobby;
            return battle != null && ReferenceEquals(battle.Channel, channel) ? battle : null;
        }

        private void OnConnected(object sender, NetworkConnectedEventArgs args)
        {
            var slot = Find(args.NetworkChannel);
            if (slot == null || slot.TransportConnected || slot.RetryQueued) return;
            slot.TransportConnected = true;
            slot.ConnectData = null;
            slot.Addresses = null;
            Notify(slot, ConnectionSignalKind.TransportConnected);
        }
        private void OnClosed(object sender, NetworkClosedEventArgs args)
        {
            var slot = Find(args.NetworkChannel);
            if (slot != null && !slot.RetryQueued) Fail(slot, ConnectionFailure.Closed);
        }
        private void OnError(object sender, NetworkErrorEventArgs args)
        {
            var slot = Find(args.NetworkChannel);
            if (slot == null || slot.RetryQueued) return;
            if ((args.ErrorCode == NetworkErrorCode.ConnectError || args.ErrorCode == NetworkErrorCode.AddressFamilyError) &&
                QueueNextAddress(slot)) return;
            Fail(slot, ConnectionFailure.Transport);
        }
        private void OnProtocolError(object sender, NetworkCustomErrorEventArgs args)
        { var slot = Find(args.NetworkChannel); if (slot != null) Fail(slot, ConnectionFailure.Protocol); }

        private void Fail(Slot slot, ConnectionFailure reason)
        {
            if (!IsCurrent(slot)) return;
            if (ReferenceEquals(slot, lobby))
            {
                Retire(ref battle);
                Retire(ref lobby);
                session.ConnectionLost();
            }
            else
            {
                bool admitted = slot.Ready;
                Retire(ref battle);
                if (admitted) session.ConnectionLost();
                else if (session.Phase == SessionPhase.ConnectingBattle) session.ReturnToWorld();
            }
            LastFailure = reason;
            Notify(slot, ConnectionSignalKind.Failed, reason);
        }

        private void Retire(ref Slot slot)
        {
            var previous = slot;
            slot = null;
            if (previous == null) return;
            previous.ConnectData = null;
            previous.Addresses = null;
            DestroyChannel(previous);
        }
        private void DestroyChannel(Slot slot)
        {
            var channel = slot.Channel;
            slot.Channel = null;
            if (channel != null && ReferenceEquals(manager.GetNetworkChannel(channel.Name), channel))
                manager.DestroyNetworkChannel(channel.Name);
        }
        private void Notify(Slot slot, ConnectionSignalKind kind, ConnectionFailure reason = ConnectionFailure.None) =>
            Changed?.Invoke(new ConnectionSignal(slot.Id, kind, reason));
        private void AssertOwner()
        {
            if (disposed) throw new ObjectDisposedException(nameof(SessionConnections));
            if (Thread.CurrentThread.ManagedThreadId != ownerThread)
                throw new InvalidOperationException("Session connections must be driven on their owner thread.");
        }
    }
}
