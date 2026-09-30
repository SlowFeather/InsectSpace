using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Framework.Network;
using Framework.Network.Kcp;
using InsectSpace.Network;

sealed class LoopbackDatagramSocket : IClientDatagramSocket
{
    private readonly UdpClient socket;
    public bool IsOpen { get; private set; } = true;
    public LoopbackDatagramSocket(IPAddress address, int port)
    {
        if (!IPAddress.IsLoopback(address)) throw new ArgumentException("Test fixture must stay on loopback.");
        socket = new UdpClient(address.AddressFamily);
        socket.Connect(address, port);
    }
    public void Send(byte[] datagram) => socket.Send(datagram, datagram.Length);
    public bool TryReceive(out byte[] datagram)
    {
        datagram = null;
        if (!IsOpen || socket.Available == 0) return false;
        IPEndPoint from = null;
        datagram = socket.Receive(ref from);
        return true;
    }
    public void Dispose() { IsOpen = false; socket.Dispose(); }
}

static class DatagramTransportTests
{
    public static void AdmissionAndLimits()
    {
        var socket = new QueueSocket();
        var context = new Context();
        var transport = new DatagramKcpTransportProvider("test", (_, _) => socket).CreateTransport(context);
        try
        {
            var data = new object();
            transport.Connect(IPAddress.Loopback, 7778, new KcpConnectData { AuthToken = "valid", UserData = data });
            Check(!transport.Connected && context.Connections == 0);
            Check(BinaryPrimitives.ReadUInt32LittleEndian(socket.Sent[0]) == 0);
            Check(BinaryPrimitives.ReadUInt32LittleEndian(socket.Sent[0].AsSpan(4)) == 5);
            socket.Incoming.Enqueue(Control(0, 17));
            transport.Update(0.01f, 0.01f);
            Check(!transport.Connected);
            Check(socket.Sent.Any(value => value.SequenceEqual(Control(17, 0))));
            socket.Incoming.Enqueue(Control(18, 2));
            transport.Update(0.01f, 0.01f);
            Check(!transport.Connected);
            socket.Incoming.Enqueue(Control(17, 2));
            transport.Update(0.01f, 0.01f);
            Check(transport.Connected && context.Connections == 1 && ReferenceEquals(data, context.ConnectedData));
            transport.Send(new byte[context.MaxPacketSize + 1], 0, context.MaxPacketSize + 1);
            Check(!transport.Connected && !socket.IsOpen && context.Errors == 1);
            Check(socket.Sent.Any(value => value.SequenceEqual(Control(17, 1))));
        }
        finally { transport.Close(); }
    }

    private static byte[] Control(uint id, uint command)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, id);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), command);
        return bytes;
    }
    private static void Check(bool condition)
    {
        if (!condition) throw new InvalidOperationException("SDK datagram KCP assertion failed.");
    }
    private sealed class QueueSocket : IClientDatagramSocket
    {
        public bool IsOpen { get; private set; } = true;
        public Queue<byte[]> Incoming { get; } = new();
        public List<byte[]> Sent { get; } = new();
        public void Send(byte[] bytes) => Sent.Add((byte[])bytes.Clone());
        public bool TryReceive(out byte[] bytes) => Incoming.TryDequeue(out bytes);
        public void Dispose() => IsOpen = false;
    }
    private sealed class Context : INetworkTransportContext
    {
        public string TransportId => "test";
        public int MaxPacketSize => 512;
        public int Connections, Errors;
        public object ConnectedData;
        public void OnConnected(object data) { Connections++; ConnectedData = data; }
        public void OnClosed() { }
        public void OnError(NetworkErrorCode code, SocketError socketError, string message) => Errors++;
        public void OnCustomError(object error) => throw new InvalidOperationException("Unexpected custom error.");
        public void OnDataReceived(byte[] buffer, int offset, int count) { }
    }
}
