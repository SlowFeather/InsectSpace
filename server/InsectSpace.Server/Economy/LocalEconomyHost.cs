using System.Net;
using Framework.Network;
using InsectSpace.Economy;
using InsectSpace.Cultivation;
using InsectSpace.Server.Cultivation;
using InsectSpace.GuPaths;
using InsectSpace.Server.GuPaths;
using InsectSpace.GuWorkshop;
using InsectSpace.Server.GuWorkshop;
using InsectSpace.Moonlight;

namespace InsectSpace.Server.Economy;

// Dedicated development port. Every loopback connection views ONE fixed local fixture account.
// Production must bind principal through verified authentication and server world routing.
public sealed class LocalEconomyHost : IDisposable
{
    private readonly INetworkServerChannel channel;
    public EconomyService Service { get; }
    public LocalEconomyBattleHost Battle { get; }
    public LocalMoonlightBattleHost Moonlight { get; }
    public CultivationService Cultivation { get; }
    public GuPathService GuPaths { get; }
    public WorkshopService Workshop { get; }
    public EconomyPrincipal Principal { get; } = new(1, "local-home", "local-world", 1);
    public int Port => ((IPEndPoint)channel.LocalEndPoint).Port;
    public LocalEconomyHost(int port, int battlePort = 0, int battleOperationLimit = EconomyService.MaxOperations, bool workshopMode = false,
        int moonlightBattlePort = 0,
        IWorkshopClock workshopClock = null, IWorkshopRandom workshopRandom = null, WorkshopCatalog workshopCatalog = null)
    {
        Service = new EconomyService(new InMemoryEconomyStore(), new[]
        { new WorldActionPrice("local-world-skill", new StoneAmounts(3, 0)), new WorldActionPrice("local-world-premium", new StoneAmounts(0, 1)) });
        Service.CreateAccount(Principal, 100, 100);
        Service.GrantReward(Principal, RewardSource.Activity, "local-activity-fixture", 100);
        Service.ApplyVerifiedRecharge(Principal, "local-simulated-paid-order", 10);
        Cultivation = new CultivationService(new InMemoryCultivationStore(), new ServerAptitudeRandom());
        Cultivation.CreateCharacter(Principal);
        GuPaths = new GuPathService(GuPathService.LoadLocalCatalog());
        GuPaths.CreateCharacter(Principal);
        // Explicit LOCAL inventory fixture, never an authenticated reward. Rank gates still apply.
        if (!workshopMode) foreach (var gu in GuPaths.Catalog.Entries) GuPaths.GrantOwned(Principal, "local-gu-fixture-" + gu.Id, gu.Id);
        if (workshopMode)
        { Workshop = new WorkshopService(Service, workshopCatalog ?? WorkshopService.LoadLocalCatalog(), workshopClock, workshopRandom); Workshop.CreateCharacter(Principal); }
        Battle = new LocalEconomyBattleHost(Service, Principal, battlePort, battleOperationLimit);
        Moonlight = new LocalMoonlightBattleHost(moonlightBattlePort);
        channel = new TcpServerChannelProvider().CreateChannel("Economy.LocalDevelopment", new LocalPlayerPacketCodec());
        channel.PacketReceived += (peer, packet) =>
        {
            if (packet is WorkshopPacket workshop && workshop.Request != null && Workshop != null)
                peer.Send(WorkshopPacket.FromResponse(Workshop.Handle(Principal, workshop.Request, Cultivation.Snapshot(Principal))));
            else if (packet is GuPacket gu && gu.Request != null && !workshopMode)
                peer.Send(GuPacket.FromResponse(GuPaths.Handle(Principal, gu.Request, Cultivation.Snapshot(Principal), Service.Snapshot(Principal).Phase == ReservePhase.InBattle)));
            else if (packet is EconomyPacket message && message.Request != null)
                peer.Send(EconomyPacket.FromResponse(Service.Handle(Principal, message.Request)));
            else if (packet is BattleResourcePacket battle && battle.Message.Kind == BattleMessageKind.AdmissionRequest)
                peer.Send(BattleResourcePacket.Create(Battle.Admit(peer.Id, battle.Message)));
            else if (packet is MoonlightPacket moonlight && moonlight.Message.Kind == MoonlightMessageKind.AdmissionRequest)
                peer.Send(MoonlightPacket.Create(Moonlight.Admit(peer.Id, moonlight.Message)));
            else if (packet is CultivationPacket growth && growth.Request != null)
                peer.Send(CultivationPacket.FromResponse(Cultivation.Handle(Principal, growth.Request, Service.Snapshot(Principal).Phase == ReservePhase.InBattle)));
            else peer.Close();
        };
        try { channel.Start(IPAddress.Loopback, port); } catch { channel.Dispose(); Battle.Dispose(); Moonlight.Dispose(); throw; }
    }
    public void Tick(float elapsed) { channel.Update(elapsed, elapsed); Battle.Tick(elapsed); Moonlight.Tick(elapsed); }
    public void Dispose() { channel.Dispose(); Battle.Dispose(); Moonlight.Dispose(); } // Pending reserve survives connection loss while this process lives.
}
