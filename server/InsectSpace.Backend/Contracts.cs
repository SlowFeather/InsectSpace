using System.Text.Json;

namespace InsectSpace.Backend;

public enum BackendServiceRole
{
    Gateway,
    Identity,
    Lobby,
    World,
    Battle,
    Worker
}

public sealed record BackendOptions(
    string MySqlConnectionString,
    string RedisConnectionString,
    string SessionSigningKey,
    TimeSpan SessionLifetime,
    string BindAddress,
    int GatewayPort,
    int IdentityPort,
    int LobbyPort,
    int WorldPort,
    int BattlePort,
    int WorkerPort)
{
    public static BackendOptions FromEnvironment()
    {
        var mysql = Environment.GetEnvironmentVariable("INSECTSPACE_MYSQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(mysql))
            throw new InvalidOperationException("INSECTSPACE_MYSQL_CONNECTION is required; do not put database credentials in source.");

        var key = Environment.GetEnvironmentVariable("INSECTSPACE_SESSION_SIGNING_KEY");
        if (string.IsNullOrWhiteSpace(key) || key.Length < 32)
            throw new InvalidOperationException("INSECTSPACE_SESSION_SIGNING_KEY must contain at least 32 characters.");

        return new BackendOptions(
            mysql,
            Environment.GetEnvironmentVariable("INSECTSPACE_REDIS_CONNECTION") ?? "127.0.0.1:6379,abortConnect=false",
            key,
            TimeSpan.FromHours(ParseInt("INSECTSPACE_SESSION_HOURS", 24)),
            Environment.GetEnvironmentVariable("INSECTSPACE_BIND_ADDRESS") ?? "127.0.0.1",
            ParseInt("INSECTSPACE_GATEWAY_PORT", 8080),
            ParseInt("INSECTSPACE_IDENTITY_PORT", 8081),
            ParseInt("INSECTSPACE_LOBBY_PORT", 8082),
            ParseInt("INSECTSPACE_WORLD_PORT", 8083),
            ParseInt("INSECTSPACE_BATTLE_PORT", 8084),
            ParseInt("INSECTSPACE_WORKER_PORT", 8085));
    }

    public int Port(BackendServiceRole role) => role switch
    {
        BackendServiceRole.Gateway => GatewayPort,
        BackendServiceRole.Identity => IdentityPort,
        BackendServiceRole.Lobby => LobbyPort,
        BackendServiceRole.World => WorldPort,
        BackendServiceRole.Battle => BattlePort,
        BackendServiceRole.Worker => WorkerPort,
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    private static int ParseInt(string name, int fallback)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value > 0 ? value : fallback;
}

public sealed record ExternalIdentity(string Provider, string Subject, string? DisplayName);
public sealed record LoginRequest(string Provider, string Credential);
public sealed record LoginResponse(string SessionToken, long PlayerId, string HomeRealmId, DateTimeOffset ExpiresAt);
public sealed record SessionContext(string TokenHash, long PlayerId, string HomeRealmId, DateTimeOffset ExpiresAt);
public sealed record PhoneOtpRequest(string PhoneNumber);
public sealed record PhoneOtpResponse(bool Accepted, string PhoneNumber, DateTimeOffset ExpiresAt, string? DevelopmentCode);
public sealed record PhoneLoginRequest(string PhoneNumber, string? Code, bool Register);

public sealed record WorldRegistration(
    string WorldClusterId,
    string InstanceId,
    string SceneId,
    string Endpoint,
    int Capacity,
    long Epoch);

public sealed record RouteRequest(long PlayerId, string HomeRealmId, string? PreferredClusterId, string? PreferredSceneId);
public sealed record WorldRoute(string HomeRealmId, string WorldClusterId, string InstanceId, string SceneId, long Epoch, string Endpoint);

public sealed record BattleRoomRequest(
    long PlayerId,
    string HomeRealmId,
    string WorldClusterId,
    string InstanceId,
    string SceneId,
    long WorldEpoch,
    string SimulationVersion,
    string ConfigVersion);

public sealed record BattleRoom(
    string RoomId,
    long PlayerId,
    string HomeRealmId,
    string WorldClusterId,
    string InstanceId,
    string SceneId,
    long WorldEpoch,
    string SimulationVersion,
    string ConfigVersion,
    string State,
    DateTimeOffset CreatedAt);

public sealed record DomainCommandRequest(
    string Domain,
    string OperationId,
    long PlayerId,
    JsonElement Payload);

public sealed record DurableCommand(
    string Domain,
    string OperationId,
    long PlayerId,
    string PayloadJson,
    string RequestHash,
    string Status,
    DateTimeOffset AcceptedAt);

public sealed record ServiceHealth(string Service, bool MySql, bool Redis, DateTimeOffset CheckedAt);

public static class BackendServiceRoleParser
{
    public static BackendServiceRole Parse(string[] args)
    {
        var value = Environment.GetEnvironmentVariable("INSECTSPACE_SERVICE") ?? "gateway";
        for (var i = 0; i + 1 < args.Length; i++)
            if (args[i] is "--service" or "-s") value = args[i + 1];
        if (!Enum.TryParse<BackendServiceRole>(value, true, out var role))
            throw new ArgumentException($"Unknown backend service role '{value}'.");
        return role;
    }
}
