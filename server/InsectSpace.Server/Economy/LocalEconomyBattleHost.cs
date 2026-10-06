using System.Net;
using System.Security.Cryptography;
using Framework.Network;
using Framework.Network.Kcp;
using InsectSpace.BattleEconomy;
using InsectSpace.Economy;

namespace InsectSpace.Server.Economy;

// LOCAL single-player resource room over actual KCP. No authentication claim or MMO match simulation.
// Network ticks schedule fixed frames; the state machine only sees integer frame numbers and commands.
public sealed class LocalEconomyBattleHost : IDisposable
{
    private sealed class Room
    {
        public string Id, ReserveId, Ticket;
        public INetworkServerSession Peer;
        public ResourceCommand? Pending;
        public BattleResourceSnapshot State;
        public BattleResourceMessage Finished;
        public readonly Dictionary<long, ResourceAction> Inputs = new();
        public double Timer;
    }
    private readonly INetworkServerChannel channel;
    private readonly EconomyService service;
    private readonly EconomyPrincipal principal;
    private readonly int operationLimit;
    private readonly Dictionary<string, Room> rooms = new();
    private readonly Dictionary<(long, long), (string Reserve, BattleResourceMessage Response)> admissions = new();
    private readonly Dictionary<long, Room> peers = new();
    public int Port => ((IPEndPoint)channel.LocalEndPoint).Port;
    public LocalEconomyBattleHost(EconomyService service, EconomyPrincipal principal, int port, int operationLimit = EconomyService.MaxOperations)
    {
        if (operationLimit < 1 || operationLimit > EconomyService.MaxOperations) throw new ArgumentOutOfRangeException(nameof(operationLimit));
        this.service = service; this.principal = principal;
        this.operationLimit = operationLimit;
        channel = new KcpServerChannelProvider(new KcpServerOptions
        {
            DriveMode = NetworkDriveMode.HostTick, MaxPendingSessions = 8, MaxSessions = 8,
            AuthTokenValidator = (token, _) => rooms.Values.Any(r => r.Finished == null && r.Ticket == token)
        }).CreateChannel("Economy.LocalBattle", new BattleResourceCodec());
        channel.PacketReceived += Receive;
        channel.SessionClosed += peer =>
        {
            if (peers.Remove(peer.Id, out var room) && room.Peer?.Id == peer.Id) room.Peer = null;
        };
        try { channel.Start(IPAddress.Loopback, port); } catch { channel.Dispose(); throw; }
    }
    public BattleResourceMessage Admit(long tcpPeer, BattleResourceMessage request)
    {
        if (request == null || request.Kind != BattleMessageKind.AdmissionRequest || !BattleResourceCodec.Valid(request))
            return new BattleResourceMessage(BattleMessageKind.Admission, request?.RequestId ?? 0, result: EconomyResult.InvalidRequest);
        if (admissions.TryGetValue((tcpPeer, request.RequestId), out var previous))
            return previous.Reserve == request.ReserveId ? previous.Response :
                new BattleResourceMessage(BattleMessageKind.Admission, request.RequestId, result: EconomyResult.IdempotencyConflict);
        var snapshot = service.Snapshot(principal);
        Room room = null;
        EconomyResult result;
        if (snapshot.Phase == ReservePhase.InBattle)
        {
            result = rooms.TryGetValue(snapshot.RoomId, out room) && room.Finished == null &&
                (request.ReserveId == "" || request.ReserveId == snapshot.ReserveId) ? EconomyResult.Ok : EconomyResult.InvalidState;
        }
        else
        {
            if (admissions.Count >= operationLimit || rooms.Count >= operationLimit)
                return new BattleResourceMessage(BattleMessageKind.Admission, request.RequestId, result: EconomyResult.CapacityExceeded);
            string id = "local-kcp-" + Guid.NewGuid().ToString("N");
            result = service.BeginBattle(principal, request.ReserveId, id, new BattleResourceRules(30, 10, 50));
            if (result == EconomyResult.Ok)
            {
                snapshot = service.Snapshot(principal);
                room = new Room { Id = id, ReserveId = snapshot.ReserveId, Ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                    State = service.BattleSnapshot(principal, id) };
                rooms.Add(id, room);
            }
        }
        var response = result == EconomyResult.Ok ? new BattleResourceMessage(BattleMessageKind.Admission, request.RequestId,
            room.Id, room.ReserveId, room.Ticket, Port, state: room.State) : new BattleResourceMessage(BattleMessageKind.Admission, request.RequestId, result: result);
        // Once full, only read-only recovery of the existing room is allowed. Retain old records;
        // a later retry cannot create a second room because new admissions remain blocked.
        if (admissions.Count < operationLimit) admissions.Add((tcpPeer, request.RequestId), (request.ReserveId, response));
        return response;
    }
    private void Receive(INetworkServerSession peer, Packet packet)
    {
        if (!(packet is BattleResourcePacket p) || p.Message == null) { peer.Close(); return; }
        var m = p.Message;
        if (m.Kind == BattleMessageKind.Join)
        {
            if (!rooms.TryGetValue(m.RoomId, out var room) || room.Finished != null || room.Ticket != m.Ticket)
            { Reject(peer, m.RoomId, EconomyResult.Unauthorized); return; }
            if (room.Peer != null && room.Peer.Id != peer.Id)
            { peers.Remove(room.Peer.Id); room.Peer.Close(); }
            room.Peer = peer; peers[peer.Id] = room; room.Timer = 0;
            Send(peer, new BattleResourceMessage(BattleMessageKind.Checkpoint, roomId: room.Id, state: room.State)); return;
        }
        if (m.Kind != BattleMessageKind.Input || !peers.TryGetValue(peer.Id, out var active) || active.Id != m.RoomId || active.Peer?.Id != peer.Id)
        { Reject(peer, m.RoomId, EconomyResult.Unauthorized); return; }
        if (active.Inputs.TryGetValue(m.Sequence, out var action))
        {
            if (action != m.Action) { Reject(peer, m.RoomId, EconomyResult.IdempotencyConflict); return; }
            if (active.Finished != null) Send(peer, active.Finished);
            else if (m.Sequence <= active.State.LastSequence) Send(peer, new BattleResourceMessage(BattleMessageKind.Checkpoint, roomId: active.Id, state: active.State));
            return; // Already queued/committed; never repeat its cost.
        }
        if (active.Finished != null || active.Pending != null || m.Sequence != active.State.LastSequence + 1)
        { Reject(peer, m.RoomId, EconomyResult.InvalidState); return; }
        // Reserve one terminal input beyond the history limit so escrow can always be settled.
        if (active.Inputs.Count >= operationLimit && m.Action != ResourceAction.LeaveBattle)
        { Reject(peer, m.RoomId, EconomyResult.CapacityExceeded); return; }
        active.Inputs.Add(m.Sequence, m.Action);
        active.Pending = new ResourceCommand(principal.PlayerId, m.Sequence, m.Action);
    }
    public void Tick(float elapsed)
    {
        if (elapsed < 0 || !float.IsFinite(elapsed)) throw new ArgumentOutOfRangeException(nameof(elapsed));
        channel.Update(elapsed, elapsed);
        foreach (var room in rooms.Values)
        {
            if (room.Finished != null || room.Peer == null || !room.Peer.Connected) continue;
            room.Timer += elapsed;
            for (int n = 0; n < 4 && room.Timer >= .05; n++)
            {
                room.Timer -= .05;
                ResourceCommand? command = room.Pending;
                var commands = command.HasValue ? new[] { command.Value } : Array.Empty<ResourceCommand>();
                var result = service.ApplyBattleFrame(principal, room.Id, room.State.Frame + 1, commands);
                if (result != EconomyResult.Ok) { Reject(room.Peer, room.Id, result); room.Peer = null; break; }
                room.Pending = null; room.State = service.BattleSnapshot(principal, room.Id);
                bool finished = command?.Action == ResourceAction.LeaveBattle;
                if (finished)
                {
                    result = service.SettleBattle(principal, room.ReserveId, room.Id);
                    if (result != EconomyResult.Ok) { Reject(room.Peer, room.Id, result); room.Peer = null; break; }
                }
                var message = new BattleResourceMessage(finished ? BattleMessageKind.Finished : BattleMessageKind.Frame,
                    roomId: room.Id, sequence: command?.Sequence ?? -1, action: command?.Action ?? 0, state: room.State);
                if (finished) room.Finished = message;
                Send(room.Peer, message);
                if (finished) break;
            }
        }
    }
    private static void Send(INetworkServerSession peer, BattleResourceMessage message) => peer.Send(BattleResourcePacket.Create(message));
    private static void Reject(INetworkServerSession peer, string room, EconomyResult result)
    { Send(peer, new BattleResourceMessage(BattleMessageKind.Rejected, roomId: room, result: result)); }
    public void Dispose() => channel.Dispose();
}
