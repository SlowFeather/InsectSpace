#if INSECTSPACE_WECHAT_SDK && (UNITY_WEBGL || UNITY_EDITOR)
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Framework.Network;
using InsectSpace.Contracts;
using InsectSpace.Network;
using WeChatWASM;

namespace InsectSpace.Platform.WeChat
{
    public sealed class WeChatAddressResolver : IChannelAddressResolver
    {
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!IPAddress.TryParse(host, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
                throw new NotSupportedException("This SDK bridge requires a server-issued IPv4 endpoint. Desktop DNS is not available.");
            return Task.FromResult(new[] { address });
        }
    }

    public sealed class WeChatChannelFactory : IPlatformChannelFactory
    {
        private readonly INetworkManager manager;
        public WeChatChannelFactory(INetworkManager manager)
        {
            this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
            manager.RegisterTransport(new TcpProvider());
            manager.RegisterTransport(new DatagramKcpTransportProvider("wechat-kcp",
                (address, port) => new WeChatDatagramSocket(address, port)));
        }
        public bool Supports(TransportKind kind) => kind == TransportKind.Tcp || kind == TransportKind.Kcp;
        public INetworkChannel Create(string name, ServiceEndpoint endpoint, INetworkPacketCodec codec)
        {
            if (endpoint == null || endpoint.Port < 1 || endpoint.Port > 65535 ||
                !IPAddress.TryParse(endpoint.Host, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
                throw new ArgumentException("A server-issued IPv4 endpoint and valid port are required.");
            if (!Supports(endpoint.Transport)) throw new PlatformNotSupportedException("Unsupported WeChat transport.");
            return manager.CreateNetworkChannel(name, endpoint.Transport == TransportKind.Tcp ? "wechat-tcp" : "wechat-kcp", codec);
        }
        private sealed class TcpProvider : INetworkTransportProvider
        {
            public string Id => "wechat-tcp";
            public INetworkTransport CreateTransport(INetworkTransportContext context) => new WeChatTcpTransport(context);
        }
    }

    internal sealed class WeChatDatagramSocket : IClientDatagramSocket
    {
        private WXUDPSocket socket;
        private readonly Queue<byte[]> received = new Queue<byte[]>();
        private readonly IPAddress peer;
        private readonly int port;
        private string failure;
        private bool open;
        private Action<UDPSocketOnMessageListenerResult> onMessage;
        private Action<GeneralCallbackResult> onClose, onError;
        public bool IsOpen => open;

        public WeChatDatagramSocket(IPAddress peer, int port)
        {
            this.peer = peer;
            this.port = port;
            socket = WX.CreateUDPSocket();
            if (socket == null) throw new IOException("WeChat UDP is unavailable.");
            try
            {
                var current = socket;
                onClose = _ => { if (socket == current) open = false; };
                onError = _ => { if (socket == current) failure = "WeChat UDP reported an error."; };
                onMessage = message =>
                {
                    if (socket != current || !open || message?.message == null || message.remoteInfo == null ||
                        !IPAddress.TryParse(message.remoteInfo.address, out var source) ||
                        !source.Equals(this.peer) || message.remoteInfo.port != this.port) return;
                    if (received.Count >= 256 || message.message.Length > 4096)
                    {
                        failure = "WeChat UDP receive limit exceeded.";
                        return;
                    }
                    received.Enqueue((byte[])message.message.Clone());
                };
                socket.OnClose(onClose);
                socket.OnError(onError);
                socket.OnMessage(onMessage, true);
                if (socket.Bind(null) <= 0) throw new IOException("WeChat UDP bind failed.");
                open = true;
            }
            catch { Dispose(); throw; }
        }

        public bool TryReceive(out byte[] datagram)
        {
            if (failure != null) throw new IOException(failure);
            datagram = received.Count == 0 ? null : received.Dequeue();
            return datagram != null;
        }
        public void Send(byte[] datagram)
        {
            if (!open || failure != null) throw new IOException("WeChat UDP is not available.");
            socket.Send(new UDPSocketSendOption
            {
                address = peer.ToString(), port = port, message = datagram, offset = 0, length = datagram.Length
            });
        }
        public void Dispose()
        {
            var previous = socket;
            socket = null;
            open = false;
            received.Clear();
            if (previous == null) return;
            try
            {
                previous.OffMessage(onMessage);
                previous.OffClose(onClose);
                previous.OffError(onError);
            }
            finally { previous.Close(); }
        }
    }

    internal sealed class WeChatTcpTransport : INetworkTransport
    {
        private readonly INetworkTransportContext context;
        private WXTCPSocket socket;
        private readonly Queue<Action> events = new Queue<Action>();
        private int pendingBytes;
        private bool overflow;
        private Action<GeneralCallbackResult> onConnect, onClose, onError;
        private Action<TCPSocketOnMessageListenerResult> onMessage;
        public string TransportId => "wechat-tcp";
        public NetworkDriveMode DriveMode => NetworkDriveMode.HostTick;
        public TimeSpan PreferredDriveInterval => TimeSpan.FromMilliseconds(10);
        public bool Connected { get; private set; }
        public EndPoint RemoteEndPoint { get; private set; }
        public INetworkFrameCodec FrameCodec { get; }

        public WeChatTcpTransport(INetworkTransportContext context)
        {
            this.context = context;
            FrameCodec = new LengthPrefixedNetworkFrameCodec(context.MaxPacketSize);
        }
        public void Connect(IPAddress address, int port, object userData)
        {
            Close();
            if (address == null || address.AddressFamily != AddressFamily.InterNetwork || port < 1 || port > 65535)
                throw new ArgumentException("WeChat TCP requires an IPv4 endpoint.");
            try
            {
                socket = WX.CreateTCPSocket();
                if (socket == null) throw new IOException("WeChat TCP is unavailable.");
                var current = socket;
                RemoteEndPoint = new IPEndPoint(address, port);
                onConnect = _ => Enqueue(current, () => { Connected = true; context.OnConnected(userData); });
                onClose = _ => Enqueue(current, () => { Close(); context.OnClosed(); });
                onError = _ => Enqueue(current, () => Fail(NetworkErrorCode.SocketError, "WeChat TCP reported an error."));
                onMessage = message =>
                {
                    if (socket != current || message?.message == null) return;
                    if (pendingBytes > 2 * 1024 * 1024 - message.message.Length) { overflow = true; return; }
                    byte[] bytes = (byte[])message.message.Clone();
                    pendingBytes += bytes.Length;
                    Enqueue(current, () =>
                    {
                        pendingBytes -= bytes.Length;
                        context.OnDataReceived(bytes, 0, bytes.Length);
                    });
                };
                socket.OnConnect(onConnect);
                socket.OnClose(onClose);
                socket.OnError(onError);
                socket.OnMessage(onMessage, false);
                socket.Connect(new TCPSocketConnectOption { address = address.ToString(), port = port, timeout = 10000 });
            }
            catch (Exception) { Fail(NetworkErrorCode.ConnectError, "WeChat TCP connection failed."); }
        }
        private void Enqueue(WXTCPSocket current, Action action)
        {
            if (socket != current) return;
            if (events.Count >= 256) { overflow = true; return; }
            events.Enqueue(action);
        }
        public void Send(byte[] buffer, int offset, int count)
        {
            if (buffer == null || offset < 0 || count < 0 || offset > buffer.Length - count)
                throw new ArgumentException("Invalid TCP send range.");
            if (!Connected) { Fail(NetworkErrorCode.SendError, "WeChat TCP is disconnected."); return; }
            var bytes = new byte[count];
            Buffer.BlockCopy(buffer, offset, bytes, 0, count);
            try { socket.Write(bytes); }
            catch (Exception) { Fail(NetworkErrorCode.SendError, "WeChat TCP write failed."); }
        }
        public TimeSpan GetNextDriveDelay() => socket == null ? TimeSpan.MaxValue : PreferredDriveInterval;
        public void Update(float elapsedSeconds, float realElapsedSeconds)
        {
            if (overflow) { Fail(NetworkErrorCode.ReceiveError, "WeChat TCP receive limit exceeded."); return; }
            for (int i = 0; i < 64 && events.Count != 0; i++) events.Dequeue()();
        }
        private void Fail(NetworkErrorCode code, string message)
        {
            Close();
            context.OnError(code, SocketError.SocketError, message);
        }
        public void Close()
        {
            var previous = socket;
            socket = null;
            Connected = false;
            RemoteEndPoint = null;
            events.Clear();
            pendingBytes = 0;
            overflow = false;
            if (previous == null) return;
            try
            {
                previous.OffConnect(onConnect);
                previous.OffClose(onClose);
                previous.OffError(onError);
                previous.OffMessage(onMessage);
            }
            finally { previous.Close(); }
        }
    }
}
#endif
