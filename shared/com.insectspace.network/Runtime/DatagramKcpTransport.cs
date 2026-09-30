using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.Sockets.Kcp;
using System.Text;
using Framework.Network;
using Framework.Network.Kcp;
using KcpAlgorithm = System.Net.Sockets.Kcp.Kcp;

namespace InsectSpace.Network
{
    // Implemented by the platform SDK. The production adapter never creates desktop sockets.
    public interface IClientDatagramSocket : IDisposable
    {
        bool IsOpen { get; }
        bool TryReceive(out byte[] datagram);
        void Send(byte[] datagram);
    }

    public sealed class DatagramKcpTransportProvider : INetworkTransportProvider
    {
        private readonly Func<IPAddress, int, IClientDatagramSocket> createSocket;
        public string Id { get; }

        public DatagramKcpTransportProvider(string id, Func<IPAddress, int, IClientDatagramSocket> createSocket)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Transport ID is required.", nameof(id));
            Id = id;
            this.createSocket = createSocket ?? throw new ArgumentNullException(nameof(createSocket));
        }

        public INetworkTransport CreateTransport(INetworkTransportContext context) =>
            new DatagramKcpTransport(Id, context, createSocket);
    }

    internal sealed class DatagramKcpTransport : INetworkTransport, IKcpCallback
    {
        private enum Phase { Closed, Requesting, Confirming, Connected }
        private readonly INetworkTransportContext context;
        private readonly Func<IPAddress, int, IClientDatagramSocket> createSocket;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private IClientDatagramSocket socket;
        private KcpAlgorithm kcp;
        private Phase phase;
        private uint session;
        private byte[] request;
        private object connectData;
        private long started, lastHandshake;

        public string TransportId { get; }
        public NetworkDriveMode DriveMode => NetworkDriveMode.HostTick;
        public TimeSpan PreferredDriveInterval => TimeSpan.FromMilliseconds(10);
        public bool Connected => phase == Phase.Connected && socket != null && socket.IsOpen;
        public EndPoint RemoteEndPoint { get; private set; }
        public INetworkFrameCodec FrameCodec => null;

        public DatagramKcpTransport(string id, INetworkTransportContext context,
            Func<IPAddress, int, IClientDatagramSocket> createSocket)
        {
            TransportId = id;
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            this.createSocket = createSocket;
        }

        public void Connect(IPAddress address, int port, object userData)
        {
            if (address == null) throw new ArgumentNullException(nameof(address));
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            Close();
            var supplied = userData as KcpConnectData;
            byte[] token = Encoding.UTF8.GetBytes(supplied?.AuthToken ?? "");
            if (token.Length > 255) throw new ArgumentException("KCP ticket exceeds 255 UTF-8 bytes.");
            // GF alpha.7 admission protocol: zero prefix, optional token length and token.
            request = new byte[token.Length == 0 ? 4 : token.Length + 8];
            if (token.Length != 0)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(4), (uint)token.Length);
                token.CopyTo(request, 8);
            }
            connectData = supplied == null ? userData : supplied.UserData;
            RemoteEndPoint = new IPEndPoint(address, port);
            started = lastHandshake = clock.ElapsedMilliseconds;
            try
            {
                socket = createSocket(address, port) ?? throw new InvalidOperationException("Missing SDK datagram socket.");
                phase = Phase.Requesting;
                socket.Send(request);
            }
            catch (Exception) { Fail(NetworkErrorCode.ConnectError, "Platform UDP connection failed."); }
        }

        public void Send(byte[] buffer, int offset, int count)
        {
            if (buffer == null || offset < 0 || count < 0 || offset > buffer.Length - count)
                throw new ArgumentException("Invalid KCP payload range.");
            if (!Connected || count > context.MaxPacketSize)
            {
                Fail(NetworkErrorCode.SendError, "KCP is disconnected or the packet exceeds its limit.");
                return;
            }
            if (kcp.WaitSnd >= 256 || kcp.Send(new Span<byte>(buffer, offset, count)) < 0)
                Fail(NetworkErrorCode.SendError, "KCP outgoing window is full or the payload was rejected.");
        }

        public TimeSpan GetNextDriveDelay() => phase == Phase.Closed ? TimeSpan.MaxValue : PreferredDriveInterval;

        public void Update(float elapsedSeconds, float realElapsedSeconds)
        {
            if (phase == Phase.Closed) return;
            try
            {
                if (!socket.IsOpen)
                {
                    Close();
                    context.OnClosed();
                    return;
                }
                for (int i = 0; i < 64 && phase != Phase.Closed && socket.TryReceive(out byte[] datagram); i++)
                    Receive(datagram);
                if (phase == Phase.Closed) return;
                long now = clock.ElapsedMilliseconds;
                if (phase == Phase.Requesting || phase == Phase.Confirming)
                {
                    if (now - started >= 5000)
                    {
                        Fail(NetworkErrorCode.ConnectError, "KCP admission timed out.");
                        return;
                    }
                    if (now - lastHandshake >= 250)
                    {
                        lastHandshake = now;
                        socket.Send(phase == Phase.Requesting ? request : Control(session, 0));
                    }
                }
                if (kcp == null) return;
                // Protocol time is independent from the deterministic battle clock.
                kcp.Update(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(now));
                for (int i = 0; i < 64 && phase == Phase.Connected; i++)
                {
                    int size = kcp.PeekSize();
                    if (size < 0) break;
                    if (size == 0 || size > context.MaxPacketSize)
                    {
                        Fail(NetworkErrorCode.ReceiveError, "KCP packet exceeds the receive limit.");
                        return;
                    }
                    var payload = new byte[size];
                    if (kcp.Recv(payload) < 0) break;
                    context.OnDataReceived(payload, 0, payload.Length);
                }
            }
            catch (Exception) { Fail(NetworkErrorCode.ReceiveError, "Platform UDP receive failed."); }
        }

        private void Receive(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 4 || bytes.Length > 4096) return;
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
            if (bytes.Length == 8)
            {
                uint value = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4));
                if (id == 0 && value != 0 && (phase == Phase.Requesting || phase == Phase.Confirming))
                {
                    if (session != 0 && session != value) return;
                    if (kcp == null)
                    {
                        session = value;
                        kcp = new KcpAlgorithm(session, this);
                        kcp.NoDelay(1, 10, 2, 1);
                        kcp.WndSize(64, 64);
                        kcp.SetMtu(1200);
                    }
                    phase = Phase.Confirming;
                    socket.Send(Control(session, 0));
                }
                else if (id == session && session != 0 && value == 2 && phase == Phase.Confirming)
                    Admit();
                else if (id == session && session != 0 && value == 1)
                {
                    Close();
                    context.OnClosed();
                }
                return;
            }
            if (kcp == null || id != session || bytes.Length < 24) return;
            if (kcp.Input(bytes) < 0) return;
            if (phase == Phase.Confirming) Admit();
        }

        private void Admit()
        {
            phase = Phase.Connected;
            object data = connectData;
            connectData = null;
            request = null;
            context.OnConnected(data);
        }

        public void Output(IMemoryOwner<byte> buffer, int validLength)
        {
            using (buffer)
                if (socket != null && socket.IsOpen)
                    socket.Send(buffer.Memory.Slice(0, validLength).ToArray());
        }

        public void Close()
        {
            var previous = socket;
            socket = null;
            phase = Phase.Closed;
            if (previous != null)
            {
                try { if (session != 0 && previous.IsOpen) previous.Send(Control(session, 1)); }
                catch (Exception) { }
                finally
                {
                    try { previous.Dispose(); }
                    catch (Exception) { }
                }
            }
            kcp?.Dispose();
            kcp = null;
            session = 0;
            request = null;
            connectData = null;
            RemoteEndPoint = null;
        }

        private void Fail(NetworkErrorCode code, string message)
        {
            Close();
            context.OnError(code, SocketError.SocketError, message);
        }

        private static byte[] Control(uint id, uint command)
        {
            var result = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(result, id);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), command);
            return result;
        }
    }
}
