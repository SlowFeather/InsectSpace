using System.Diagnostics;
using System.Net;
using Framework;
using Framework.Network;
using Framework.Network.Kcp;
using InsectSpace.Config;
using InsectSpace.Network;
using InsectSpace.Simulation;
using Luban;

var tables = new Tables(name => new ByteBuf(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", name + ".bytes"))));
using var battle = BattleSession.CreateSmokeSession(new EmptyLocalInputSource());
for (int frame = 0; frame < 20; frame++) battle.TryAdvance();
Console.WriteLine($"SERVER_FOUNDATION_READY scenes={tables.TbWorldScene.DataList.Count} deterministicFrames={battle.Frame + 1}");
if (args.Contains("--validate")) return;

int tcpPort = ReadPort("--tcp-port", 7777);
int kcpPort = ReadPort("--kcp-port", 7778);
string token = Environment.GetEnvironmentVariable("INSECTSPACE_DEV_BATTLE_TOKEN") ?? "local-smoke-only";
using var tcp = new TcpServerChannelProvider().CreateChannel("Lobby.Dev", new FoundationPacketCodec());
using var kcp = new KcpServerChannelProvider(new KcpServerOptions
{
    DriveMode = NetworkDriveMode.HostTick,
    AuthTokenValidator = (provided, _) => provided == token,
    MaxPendingSessions = 32, MaxSessions = 32
}).CreateChannel("Battle.Dev", new FoundationPacketCodec());
Configure(tcp);
Configure(kcp);
tcp.Start(IPAddress.Loopback, tcpPort);
kcp.Start(IPAddress.Loopback, kcpPort);
Console.WriteLine($"LOCAL SMOKE ONLY. TCP=127.0.0.1:{tcpPort}, KCP=127.0.0.1:{kcpPort}. No account or game services.");
bool stopping = false;
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stopping = true; };
var clock = Stopwatch.StartNew();
double last = clock.Elapsed.TotalSeconds;
while (!stopping)
{
    double now = clock.Elapsed.TotalSeconds;
    float elapsed = (float)(now - last);
    last = now;
    tcp.Update(elapsed, elapsed);
    kcp.Update(elapsed, elapsed);
    Thread.Sleep(5);
}
GameFrameworkEntry.Shutdown();

void Configure(INetworkServerChannel channel)
{
    channel.PacketReceived += (session, packet) =>
    {
        if (packet is FoundationPacket hello && hello.Message == "foundation.hello")
            session.Send(FoundationPacket.Create("foundation.ready"));
        else
            session.Close();
    };
    channel.Error += (_, code, _, _) => Console.Error.WriteLine($"{channel.Name}: {code}");
}
int ReadPort(string option, int fallback)
{
    int index = Array.IndexOf(args, option);
    if (index < 0) return fallback;
    if (index + 1 >= args.Length || !int.TryParse(args[index + 1], out int port) || port < 1 || port > 65535)
        throw new ArgumentException("Invalid port: " + option);
    return port;
}
