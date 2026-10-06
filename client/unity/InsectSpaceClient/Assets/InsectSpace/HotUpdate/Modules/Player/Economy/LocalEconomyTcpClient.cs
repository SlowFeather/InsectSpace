using System;
using System.Net;
using Framework;
using Framework.Network;
using InsectSpace.Economy;
using InsectSpace.Cultivation;
using InsectSpace.Gameplay.Cultivation;
using InsectSpace.Gameplay.GuPaths;
using InsectSpace.GuPaths;
using InsectSpace.GuWorkshop;
using InsectSpace.Gameplay.GuWorkshop;
using InsectSpace.Moonlight;

namespace InsectSpace.Gameplay.Economy
{
    // Explicit LOCAL DEVELOPMENT adapter. No login claim; no production fallback.
    // The host owns GameFrameworkEntry.Update; this adapter never double-ticks it.
    public sealed class LocalEconomyTcpClient : IDisposable
    {
        private readonly INetworkManager manager;
        private readonly bool enableCultivation;
        private INetworkChannel channel;
        private float connecting;
        private bool opened;
        private bool refreshRequested;
        public EconomyClient Client { get; }
        public LocalEconomyBattleClient Battle { get; }
        public LocalMoonlightBattleClient Moonlight { get; }
        public CultivationClient Cultivation { get; }
        public GuPathClient GuPaths { get; }
        public WorkshopClient Workshop { get; }
        public bool Active => channel != null;
        public LocalEconomyTcpClient(INetworkManager manager, long localPlayerId = 1, string localHome = "local-home", bool enableCultivation = false, GuCatalog guCatalog = null, WorkshopCatalog workshopCatalog = null)
        {
            this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
            this.enableCultivation = enableCultivation;
            Client = new EconomyClient(localPlayerId, localHome, request => channel.Send(EconomyPacket.FromRequest(request)));
            Cultivation = new CultivationClient(localPlayerId, localHome, request => channel.Send(CultivationPacket.FromRequest(request)));
            if (guCatalog != null) GuPaths = new GuPathClient(localPlayerId, localHome, guCatalog, request => channel.Send(GuPacket.FromRequest(request)));
            if (workshopCatalog != null) Workshop = new WorkshopClient(localPlayerId, localHome, workshopCatalog, request => channel.Send(WorkshopPacket.FromRequest(request)));
            Battle = new LocalEconomyBattleClient(manager, localPlayerId,
                message => channel.Send(BattleResourcePacket.Create(message)), () => refreshRequested = true);
            Moonlight = new LocalMoonlightBattleClient(manager, localPlayerId,
                message => channel.Send(MoonlightPacket.Create(message)));
        }
        public void Connect(int port = 7779)
        {
#if UNITY_5_3_OR_NEWER && !UNITY_EDITOR
            throw new PlatformNotSupportedException("LOCAL economy adapter is Editor-only. Install an authenticated platform transport for production.");
#else
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            Dispose(); opened = false; connecting = 0;
            try
            {
                channel = manager.CreateNetworkChannel("Economy.Local." + Guid.NewGuid().ToString("N"), "tcp", new LocalPlayerPacketCodec());
                channel.SetDefaultHandler((_, packet) =>
                {
                    if (packet is WorkshopPacket workshop && workshop.Response != null) Workshop?.Receive(workshop.Response);
                    else if (packet is GuPacket gu && gu.Response != null) GuPaths?.Receive(gu.Response);
                    else if (packet is EconomyPacket p && p.Response != null) Client.Receive(p.Response);
                    else if (packet is BattleResourcePacket battle) Battle.ReceiveAdmission(battle.Message);
                    else if (packet is MoonlightPacket moonlight) Moonlight.ReceiveAdmission(moonlight.Message);
                    else if (packet is CultivationPacket growth && growth.Response != null) Cultivation.Receive(growth.Response);
                });
                channel.Connect(IPAddress.Loopback, port, null);
            }
            catch { Dispose(); throw; }
#endif
        }
        public void Tick(float seconds)
        {
            if (channel == null) return;
            if (channel.Connected && !opened)
            {
                opened = true; Client.TransportConnected(); Client.Refresh();
                if (GuPaths != null) { GuPaths.TransportConnected(); GuPaths.Refresh(); }
                if (Workshop != null) { Workshop.TransportConnected(); Workshop.Refresh(); }
                if (enableCultivation) { Cultivation.TransportConnected(); Cultivation.Refresh(); }
            }
            else if (!channel.Connected && opened) { Dispose(); return; }
            else if (!opened)
            { connecting += seconds; if (connecting >= 8) { Dispose(); return; } }
            Client.Tick(seconds);
            Cultivation.Tick(seconds);
            GuPaths?.Tick(seconds);
            Workshop?.Tick(seconds);
            Battle.Tick(seconds);
            Moonlight.Tick(seconds);
            if (refreshRequested && Client.Connected && Client.Pending == null)
            { refreshRequested = false; Client.Refresh(); }
        }
        public void BeginBattle()
        {
            if (!Client.Ready || Client.RetryableRequest != null) throw new InvalidOperationException("Refresh wallet and resolve pending operations before battle.");
            Battle.RequestAdmission(Client.Snapshot.ReserveId);
        }
        public void BeginMoonlightBattle() => Moonlight.RequestAdmission();
        public void Dispose()
        {
            Battle?.Dispose(); Moonlight?.Dispose(); refreshRequested = false;
            if (channel != null) { manager.DestroyNetworkChannel(channel.Name); channel = null; }
            opened = false; Client.Disconnect("已断开；余额不可用，储备不会自动退款");
            GuPaths?.Disconnect("已断开；重连读取服务器蛊虫组合");
            Workshop?.Disconnect("已断开；重连后查询原养炼结果");
            Cultivation.Disconnect("已断开；重连读取原有转数与资质，不使用本地随机");
        }
    }
}
