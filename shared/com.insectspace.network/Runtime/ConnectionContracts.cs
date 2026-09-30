using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Framework.Network;
using InsectSpace.Contracts;

namespace InsectSpace.Network
{
    public enum SessionChannel { Lobby, Battle }
    public enum ConnectionSignalKind { TransportConnected, Authenticated, BattleReady, Failed }
    public enum ConnectionFailure { None, Closed, Transport, Protocol, Timeout, Resolution, Suspended }

    public readonly struct ConnectionId : IEquatable<ConnectionId>
    {
        public SessionChannel Channel { get; }
        public long Generation { get; }
        public ConnectionId(SessionChannel channel, long generation) { Channel = channel; Generation = generation; }
        public bool Equals(ConnectionId other) => Channel == other.Channel && Generation == other.Generation;
        public override bool Equals(object other) => other is ConnectionId id && Equals(id);
        public override int GetHashCode() => ((int)Channel * 397) ^ Generation.GetHashCode();
    }

    public readonly struct ConnectionSignal
    {
        public ConnectionId Connection { get; }
        public ConnectionSignalKind Kind { get; }
        public ConnectionFailure Failure { get; }
        public ConnectionSignal(ConnectionId connection, ConnectionSignalKind kind, ConnectionFailure failure = ConnectionFailure.None)
        { Connection = connection; Kind = kind; Failure = failure; }
    }

    public readonly struct SessionPacket
    {
        public ConnectionId Connection { get; }
        // GF owns received packets. Decode/copy within the callback; never retain this object.
        public Packet Packet { get; }
        public SessionPacket(ConnectionId connection, Packet packet) { Connection = connection; Packet = packet; }
    }

    public sealed class BattleCompatibility
    {
        public string SimulationVersion { get; }
        public string ConfigHash { get; }
        public string MapHash { get; }
        public BattleCompatibility(string simulationVersion, string configHash, string mapHash)
        {
            if (string.IsNullOrWhiteSpace(simulationVersion) || string.IsNullOrWhiteSpace(configHash) || string.IsNullOrWhiteSpace(mapHash))
                throw new ArgumentException("A loaded simulation/config/map compatibility tuple is required.");
            SimulationVersion = simulationVersion; ConfigHash = configHash; MapHash = mapHash;
        }
        public void Validate(BattleTicket ticket)
        {
            if (ticket == null || ticket.SimulationVersion != SimulationVersion ||
                ticket.ConfigHash != ConfigHash || ticket.MapHash != MapHash)
                throw new InvalidOperationException("Battle ticket does not match the loaded simulation, configuration and map.");
        }
    }

    public interface IChannelAddressResolver
    {
        Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellation);
    }

    public sealed class DesktopAddressResolver : IChannelAddressResolver
    {
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (IPAddress.TryParse(host, out var address)) return Task.FromResult(new[] { address });
            // netstandard2.1 DNS cannot be cancelled in flight. The owner discards stale results.
            return Dns.GetHostAddressesAsync(host);
        }
    }

    public sealed class UnconfiguredMiniGameAddressResolver : IChannelAddressResolver
    {
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellation) =>
            throw new PlatformNotSupportedException("Install the verified mini-game SDK address/transport bridge.");
    }
}
