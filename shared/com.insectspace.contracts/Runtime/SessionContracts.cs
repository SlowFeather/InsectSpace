using System;
using System.Collections.Generic;

namespace InsectSpace.Contracts
{
    public static class ProtocolVersion
    {
        public const int Current = 1;
        public const int MaxPacketBytes = 64 * 1024;
        public const int BattleFramesPerSecond = 20;
    }

    public enum TransportKind { Tcp, SecureWebSocket, Kcp, WeChatTcp, WeChatUdpKcp }
    public enum BattleKind { Local, Online }
    public enum SessionPhase { SignedOut, Lobby, World, ConnectingBattle, Battle, Recovering }
    public enum QualityTier { Low, Medium, High }

    [Serializable]
    public sealed class ServiceEndpoint
    {
        public TransportKind Transport;
        public string Host = string.Empty;
        public int Port;
        public string Path = string.Empty;
    }

    [Serializable]
    public sealed class WorldRoute
    {
        public string HomeRealmId = string.Empty;
        public string WorldClusterId = string.Empty;
        public string InstanceId = string.Empty;
        public string SceneId = string.Empty;
        public long Epoch;

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(HomeRealmId) || string.IsNullOrWhiteSpace(WorldClusterId) ||
                string.IsNullOrWhiteSpace(InstanceId) || string.IsNullOrWhiteSpace(SceneId) || Epoch < 0)
                throw new ArgumentException("A complete, server-issued world route is required.");
        }

        public WorldRoute Copy() => new WorldRoute
        {
            HomeRealmId = HomeRealmId, WorldClusterId = WorldClusterId,
            InstanceId = InstanceId, SceneId = SceneId, Epoch = Epoch
        };
    }

    [Serializable]
    public sealed class BattleTicket
    {
        public string RoomId = string.Empty;
        public string Ticket = string.Empty;
        public string SimulationVersion = string.Empty;
        public string ConfigHash = string.Empty;
        public string MapHash = string.Empty;
        public ulong Seed;
        public long ExpiresAtUnixSeconds;
        public ServiceEndpoint Endpoint;
    }

    [Serializable]
    public sealed class PlayerSummary
    {
        public long PlayerId;
        public string DisplayName = string.Empty;
        public string HomeRealmId = string.Empty;
        public int CharacterConfigId;
        public int RealmLevel;
        public long Revision;
    }

    [Serializable]
    public sealed class EquipmentSlot
    {
        public int SlotId;
        public long InstanceId;
        public int ConfigId;
    }

    [Serializable]
    public sealed class GuSlot
    {
        public int SlotId;
        public long InstanceId;
        public int ConfigId;
        public int Rank;
    }

    [Serializable]
    public sealed class WorldActorSnapshot
    {
        public long ActorId;
        public int AppearanceId;
        public int XMillimeters;
        public int ZMillimeters;
        public int FacingMilliDegrees;
        public long ServerTick;
    }

    public interface IIdentityProvider
    {
        // One-time platform code only. The backend exchanges and validates it.
        System.Threading.Tasks.Task<string> GetLoginCodeAsync(System.Threading.CancellationToken cancellation);
    }

    public interface IWorldPresence
    {
        IReadOnlyCollection<WorldActorSnapshot> Actors { get; }
        event Action Changed;
    }

    [Serializable]
    public sealed class WorldMoveIntent
    {
        public string InstanceId = string.Empty;
        public long RouteEpoch;
        public long Sequence;
        public int XMillimeters;
        public int ZMillimeters;
    }

    public interface IWorldMovementSink
    {
        // Sends intent to the world authority. It does not mutate the local replica.
        bool Submit(WorldMoveIntent intent);
    }

    public interface IWorldRouter
    {
        // A friend ID is a routing request, never authority to pick an instance.
        System.Threading.Tasks.Task<WorldRoute> JoinFriendAsync(long friendId,
            System.Threading.CancellationToken cancellation);
    }

    public interface IRewardSettlement
    {
        // Idempotency key + server verification, never client-computed rewards.
        System.Threading.Tasks.Task SubmitReplayAsync(string battleId, string replayHash,
            string idempotencyKey, System.Threading.CancellationToken cancellation);
    }
}
