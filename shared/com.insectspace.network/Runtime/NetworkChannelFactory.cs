using System;
using Framework.Network;
using Framework.Network.Kcp;
using InsectSpace.Contracts;

namespace InsectSpace.Network
{
    public interface IPlatformChannelFactory
    {
        bool Supports(TransportKind kind);
        INetworkChannel Create(string name, ServiceEndpoint endpoint, INetworkPacketCodec codec);
    }

    public sealed class DesktopChannelFactory : IPlatformChannelFactory
    {
        private readonly INetworkManager manager;
        public DesktopChannelFactory(INetworkManager manager)
        {
            this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
            manager.RegisterTransport(new KcpTransportProvider(new KcpClientOptions
            {
                DriveMode = NetworkDriveMode.HostTick, PreferredDriveIntervalMilliseconds = 10
            }));
        }
        public bool Supports(TransportKind kind) => kind == TransportKind.Tcp || kind == TransportKind.Kcp;
        public INetworkChannel Create(string name, ServiceEndpoint endpoint, INetworkPacketCodec codec)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (!Supports(endpoint.Transport)) throw new PlatformNotSupportedException("Desktop factory does not support " + endpoint.Transport);
            if (endpoint.Port < 1 || endpoint.Port > 65535 || string.IsNullOrWhiteSpace(endpoint.Host))
                throw new ArgumentException("Invalid service endpoint.");
            return manager.CreateNetworkChannel(name, endpoint.Transport == TransportKind.Tcp ? "tcp" : "kcp", codec);
        }
    }

    // Install a real SDK bridge here. Never route WebGL to System.Net.Sockets.
    public sealed class UnconfiguredMiniGameChannelFactory : IPlatformChannelFactory
    {
        public bool Supports(TransportKind kind) => false;
        public INetworkChannel Create(string name, ServiceEndpoint endpoint, INetworkPacketCodec codec) =>
            throw new PlatformNotSupportedException(
                "WeChat transport is not configured. Provide a verified TCP/WSS gateway and UDP/KCP SDK adapter.");
    }
}
