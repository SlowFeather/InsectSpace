using System;
using Framework;
using Framework.Network;
using InsectSpace.Contracts;
using InsectSpace.Foundation;
using InsectSpace.Network;

namespace InsectSpace.Gameplay.Demo
{
    // Real loopback TCP diagnostic. A successful hello is deliberately NOT ConfirmLogin.
    public sealed class LocalNetworkProbe : IDisposable
    {
        // Reuse GF's existing TCP transport. Constructing a second DesktopChannelFactory would
        // register KCP twice after Bootstrap. This fixture cannot create any non-loopback channel.
        private sealed class LocalTcpFactory : IPlatformChannelFactory
        {
            private readonly INetworkManager manager;
            public LocalTcpFactory(INetworkManager manager) { this.manager = manager; }
            public bool Supports(TransportKind kind) => kind == TransportKind.Tcp;
            public INetworkChannel Create(string name, ServiceEndpoint endpoint, INetworkPacketCodec codec)
            {
                if (endpoint == null || endpoint.Transport != TransportKind.Tcp || endpoint.Host != "127.0.0.1" ||
                    endpoint.Port < 1 || endpoint.Port > 65535)
                    throw new InvalidOperationException("LOCAL diagnostic only accepts loopback TCP.");
                return manager.CreateNetworkChannel(name, "tcp", codec);
            }
        }
        private SessionConnections connections;
        private bool retire;
        private readonly Action<string> log;
        public string Status { get; private set; } = "尚未连接";
        public bool Busy => connections != null;
        public bool Received { get; private set; }
        public SessionCoordinator Session { get; private set; } = new SessionCoordinator();
        public LocalNetworkProbe(Action<string> log) { this.log = log; }
        public void Connect(int port = 7777)
        {
#if !UNITY_EDITOR
            throw new PlatformNotSupportedException("LOCAL loopback diagnostic is Editor-only.");
#else
            Dispose(); Received = false; retire = false;
            Session = new SessionCoordinator();
            Status = "连接本机 TCP…";
            try
            {
                var manager = GameFrameworkEntry.GetModule<INetworkManager>();
                connections = new SessionConnections(Session, manager, new LocalTcpFactory(manager),
                    new DesktopAddressResolver(), () => DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 5);
                connections.Changed += OnChanged;
                connections.PacketReceived += OnPacket;
                connections.ConnectLobby(new ServiceEndpoint { Host = "127.0.0.1", Port = port,
                    Transport = TransportKind.Tcp }, new FoundationPacketCodec());
            }
            catch { Dispose(); Status = "连接失败（无本地回退）"; throw; }
#endif
        }
        private void OnChanged(ConnectionSignal signal)
        {
            if (signal.Kind == ConnectionSignalKind.TransportConnected)
            {
                Status = "TCP 连通，等待诊断回复；未认证";
                connections.Send(signal.Connection, FoundationPacket.Create("foundation.hello"));
            }
            if (signal.Kind == ConnectionSignalKind.Failed)
            { Status = "失败 / " + signal.Failure + "（无本地回退）"; log(Status); retire = true; }
        }
        private void OnPacket(SessionPacket packet)
        {
            if (!(packet.Packet is FoundationPacket message) || message.Message != "foundation.ready") return;
            Received = true;
            Status = "真实 TCP 往返成功；SignedOut / 未认证";
            log(Status); retire = true;
        }
        public void Tick(float elapsed)
        {
            connections?.Tick(elapsed);
            if (retire) { Dispose(); retire = false; }
        }
        public void Dispose()
        {
            if (connections == null) return;
            connections.Changed -= OnChanged;
            connections.PacketReceived -= OnPacket;
            connections.Dispose(); connections = null;
        }
    }
}
