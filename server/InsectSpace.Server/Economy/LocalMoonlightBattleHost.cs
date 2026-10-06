using System.Net;
using System.Security.Cryptography;
using Framework;
using Framework.Network;
using Framework.Network.Kcp;
using InsectSpace.Config;
using InsectSpace.Economy;
using InsectSpace.Moonlight;
using InsectSpace.Simulation;
using Luban;

namespace InsectSpace.Server.Economy;

// LOCAL single-player PvE room. Routing, fixed-frame advancement and the final
// snapshot are server authoritative; this host is intentionally in-memory.
public sealed class LocalMoonlightBattleHost : IDisposable
{
    private sealed class Room
    {
        public string Id = "", Ticket = "";
        public INetworkServerSession Peer;
        public MoonlightBattleState State;
        public MoonlightMessage Finished;
        public readonly Dictionary<long, MoonlightInputKind> Inputs = new();
        public MoonlightInputKind? Pending;
        public bool PendingEnabled;
        public double Timer;
    }

    private readonly INetworkServerChannel channel;
    private readonly long actorId;
    private readonly int operationLimit;
    private readonly MoonlightBattleRules rules;
    private readonly Dictionary<string, Room> rooms = new();
    private readonly Dictionary<(long, long), MoonlightMessage> admissions = new();
    private readonly Dictionary<long, Room> peers = new();
    public int Port => ((IPEndPoint)channel.LocalEndPoint).Port;

    public LocalMoonlightBattleHost(int port, long actorId = 1, int operationLimit = EconomyService.MaxOperations)
    {
        if (actorId <= 0 || operationLimit < 1 || operationLimit > EconomyService.MaxOperations)
            throw new ArgumentOutOfRangeException();
        this.actorId = actorId;
        this.operationLimit = operationLimit;
        rules = LoadRules();
        channel = new KcpServerChannelProvider(new KcpServerOptions
        {
            DriveMode = NetworkDriveMode.HostTick,
            MaxPendingSessions = 8,
            MaxSessions = 8,
            AuthTokenValidator = (token, _) => rooms.Values.Any(r => r.Finished == null && r.Ticket == token)
        }).CreateChannel("Economy.LocalMoonlight", new MoonlightPacketCodec());
        channel.PacketReceived += Receive;
        channel.SessionClosed += peer =>
        {
            if (peers.Remove(peer.Id, out var room) && room.Peer?.Id == peer.Id) room.Peer = null;
        };
        try { channel.Start(IPAddress.Loopback, port); }
        catch { channel.Dispose(); throw; }
    }

    public MoonlightMessage Admit(long tcpPeer, MoonlightMessage request)
    {
        if (request == null || request.Kind != MoonlightMessageKind.AdmissionRequest || !MoonlightPacketCodec.Valid(request))
            return new MoonlightMessage(MoonlightMessageKind.Admission, request?.RequestId ?? 0, result: (int)EconomyResult.InvalidRequest);
        if (admissions.TryGetValue((tcpPeer, request.RequestId), out var previous)) return previous;
        if (admissions.Count >= operationLimit || rooms.Count >= operationLimit)
            return new MoonlightMessage(MoonlightMessageKind.Admission, request.RequestId, result: (int)EconomyResult.CapacityExceeded);
        var room = new Room
        {
            Id = "local-moonlight-" + Guid.NewGuid().ToString("N"),
            Ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            State = new MoonlightBattleState(actorId, rules)
        };
        rooms.Add(room.Id, room);
        var response = new MoonlightMessage(MoonlightMessageKind.Admission, request.RequestId,
            roomId: room.Id, ticket: room.Ticket, port: Port, state: room.State.Capture());
        admissions[(tcpPeer, request.RequestId)] = response;
        return response;
    }

    private void Receive(INetworkServerSession peer, Packet packet)
    {
        if (packet is not MoonlightPacket p || p.Message == null) { peer.Close(); return; }
        var m = p.Message;
        if (m.Kind == MoonlightMessageKind.Join)
        {
            if (!rooms.TryGetValue(m.RoomId, out var room) || room.Finished != null || room.Ticket != m.Ticket)
            { Reject(peer, m.RoomId, EconomyResult.Unauthorized); return; }
            if (room.Peer != null && room.Peer.Id != peer.Id) { peers.Remove(room.Peer.Id); room.Peer.Close(); }
            room.Peer = peer; peers[peer.Id] = room; room.Timer = 0;
            Send(peer, new MoonlightMessage(MoonlightMessageKind.Checkpoint, roomId: room.Id, state: room.State.Capture()));
            return;
        }
        if (m.Kind != MoonlightMessageKind.Input || !peers.TryGetValue(peer.Id, out var active) || active.Id != m.RoomId || active.Peer?.Id != peer.Id)
        { Reject(peer, m.RoomId, EconomyResult.Unauthorized); return; }
        if (active.Inputs.TryGetValue(m.Sequence, out var prior))
        {
            if (prior != m.Input) { Reject(peer, m.RoomId, EconomyResult.IdempotencyConflict); return; }
            if (active.Finished != null) Send(peer, active.Finished);
            else if (m.Sequence <= active.State.LastSequence) Send(peer, new MoonlightMessage(MoonlightMessageKind.Checkpoint, roomId: active.Id, state: active.State.Capture()));
            return;
        }
        if (active.Finished != null || active.Pending.HasValue || m.Sequence != active.State.LastSequence + 1)
        { Reject(peer, m.RoomId, EconomyResult.InvalidState); return; }
        if (active.Inputs.Count >= operationLimit && m.Input != MoonlightInputKind.Retreat)
        { Reject(peer, m.RoomId, EconomyResult.CapacityExceeded); return; }
        active.Inputs.Add(m.Sequence, m.Input);
        active.Pending = m.Input;
        active.PendingEnabled = m.Enabled;
    }

    public void Tick(float elapsed)
    {
        if (elapsed < 0 || !float.IsFinite(elapsed)) throw new ArgumentOutOfRangeException(nameof(elapsed));
        channel.Update(elapsed, elapsed);
        foreach (var room in rooms.Values)
        {
            if (room.Finished != null || room.Peer == null || !room.Peer.Connected) continue;
            room.Timer += elapsed;
            for (int n = 0; n < 4 && room.Timer >= 1d / 20d; n++)
            {
                room.Timer -= 1d / 20d;
                var input = room.Pending;
                var commands = input.HasValue
                    ? new[] { new MoonlightBattleCommand(actorId, room.State.LastSequence + 1, (MoonlightBattleCommandKind)input.Value, room.PendingEnabled) }
                    : Array.Empty<MoonlightBattleCommand>();
                long frame = room.State.Frame + 1;
                if (!room.State.Step(frame, commands)) { Reject(room.Peer, room.Id, EconomyResult.InvalidState); room.Peer = null; break; }
                room.Pending = null;
                room.PendingEnabled = false;
                var snapshot = room.State.Capture();
                bool finished = room.State.Phase != MoonlightBattlePhase.Active;
                var message = new MoonlightMessage(finished ? MoonlightMessageKind.Finished : MoonlightMessageKind.Frame,
                    roomId: room.Id, sequence: input.HasValue ? snapshot.LastSequence : -1,
                    input: input ?? 0, enabled: room.PendingEnabled, state: snapshot);
                if (finished) room.Finished = message;
                Send(room.Peer, message);
                if (finished) break;
            }
        }
    }

    private static MoonlightBattleRules LoadRules()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Data", "tbbattlerule.bytes");
        if (!File.Exists(path)) return MoonlightBattleRules.Default;
        var tables = new Tables(name => new ByteBuf(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", name + ".bytes"))));
        var rule = tables.TbBattleRule.GetOrDefault(1) ?? throw new InvalidOperationException("Missing battle rule 1.");
        var skill = tables.TbBattleSkill.GetOrDefault("moon-blade") ?? throw new InvalidOperationException("Missing moon-blade skill.");
        return new MoonlightBattleRules(rule.PlayerMaxHp, rule.MonsterMaxHp, rule.AutoAttackDamage, rule.AutoAttackIntervalFrames,
            rule.MonsterAttackDamage, rule.MonsterAttackIntervalFrames, rule.MaxResource, rule.ResourceRegenPerFrame,
            skill.Damage, skill.CooldownFrames, skill.ResourceCost, skill.GuId, skill.Id, skill.Name);
    }

    private static void Send(INetworkServerSession peer, MoonlightMessage message) => peer.Send(MoonlightPacket.Create(message));
    private static void Reject(INetworkServerSession peer, string room, EconomyResult result) => Send(peer,
        new MoonlightMessage(MoonlightMessageKind.Rejected, roomId: room, result: (int)result));
    public void Dispose() => channel.Dispose();
}
