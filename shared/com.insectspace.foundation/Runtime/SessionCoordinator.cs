using System;
using InsectSpace.Contracts;

namespace InsectSpace.Foundation
{
    // Called on the host thread after validated server responses, not from UI.
    public sealed class SessionCoordinator
    {
        private WorldRoute route;
        public SessionPhase Phase { get; private set; } = SessionPhase.SignedOut;
        public WorldRoute Route => route?.Copy();
        public BattleKind? ActiveBattle { get; private set; }
        public long PlayerId { get; private set; }

        public void Authenticated(long playerId)
        {
            Require(SessionPhase.SignedOut);
            if (playerId <= 0) throw new ArgumentOutOfRangeException(nameof(playerId));
            PlayerId = playerId;
            Phase = SessionPhase.Lobby;
        }

        public void Reauthenticated(long playerId)
        {
            Require(SessionPhase.Recovering);
            if (playerId <= 0 || playerId != PlayerId)
                throw new InvalidOperationException("Recovery cannot change the authenticated player.");
            ActiveBattle = null;
            Phase = SessionPhase.Lobby;
        }

        public void EnterWorld(WorldRoute serverRoute)
        {
            if (Phase != SessionPhase.Lobby && Phase != SessionPhase.World && Phase != SessionPhase.Recovering)
                throw new InvalidOperationException("Cannot change world while in battle or signed out.");
            if (serverRoute == null) throw new ArgumentNullException(nameof(serverRoute));
            serverRoute.Validate();
            if (route != null && serverRoute.Epoch <= route.Epoch)
                throw new InvalidOperationException("Rejected stale route epoch.");
            route = serverRoute.Copy();
            ActiveBattle = null;
            Phase = SessionPhase.World;
        }

        public void BeginLocalEncounter()
        {
            Require(SessionPhase.World);
            ActiveBattle = BattleKind.Local;
            Phase = SessionPhase.Battle;
        }

        public void BeginOnlineEncounter(BattleTicket ticket, long unixSeconds)
        {
            Require(SessionPhase.World);
            if (ticket == null || ticket.Endpoint == null || string.IsNullOrWhiteSpace(ticket.RoomId) ||
                string.IsNullOrWhiteSpace(ticket.Ticket) || ticket.ExpiresAtUnixSeconds <= unixSeconds ||
                string.IsNullOrWhiteSpace(ticket.SimulationVersion) || string.IsNullOrWhiteSpace(ticket.ConfigHash) ||
                string.IsNullOrWhiteSpace(ticket.MapHash) || string.IsNullOrWhiteSpace(ticket.Endpoint.Host) ||
                ticket.Endpoint.Port < 1 || ticket.Endpoint.Port > 65535 ||
                (ticket.Endpoint.Transport != TransportKind.Kcp && ticket.Endpoint.Transport != TransportKind.WeChatUdpKcp))
                throw new ArgumentException("A valid server-issued KCP battle ticket is required.");
            ActiveBattle = BattleKind.Online;
            Phase = SessionPhase.ConnectingBattle;
        }

        public void BattleConnected()
        {
            Require(SessionPhase.ConnectingBattle);
            Phase = SessionPhase.Battle;
        }

        public void ReturnToWorld()
        {
            if (Phase != SessionPhase.Battle && Phase != SessionPhase.ConnectingBattle)
                throw new InvalidOperationException("There is no battle to exit.");
            ActiveBattle = null;
            Phase = SessionPhase.World;
        }

        public void ConnectionLost()
        {
            if (Phase == SessionPhase.SignedOut) return;
            ActiveBattle = null;
            Phase = SessionPhase.Recovering;
        }

        public void SignOut()
        {
            Phase = SessionPhase.SignedOut;
            ActiveBattle = null;
            route = null;
            PlayerId = 0;
        }

        private void Require(SessionPhase expected)
        {
            if (Phase != expected) throw new InvalidOperationException("Expected " + expected + ", actual " + Phase);
        }
    }
}
