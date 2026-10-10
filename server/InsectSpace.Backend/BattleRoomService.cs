using MySqlConnector;

namespace InsectSpace.Backend;

public sealed class BattleRoomService
{
    private readonly MySqlBackendStore mysql;
    private readonly RedisBackendStore redis;
    public BattleRoomService(MySqlBackendStore mysql, RedisBackendStore redis) { this.mysql = mysql; this.redis = redis; }

    public async Task<BattleRoom> CreateAsync(BattleRoomRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        var lease = await redis.TryLockAsync($"battle:{request.HomeRealmId}:{request.PlayerId}", TimeSpan.FromSeconds(5), cancellationToken);
        if (lease is null) throw new InvalidOperationException("Battle room allocation is busy.");
        await using var allocationLease = lease;
        await using var lookupConnection = await mysql.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand("SELECT room_id,world_cluster_id,instance_id,scene_id,world_epoch,simulation_version,config_version,state,created_at FROM battle_rooms WHERE player_id=@player AND home_realm_id=@home AND state IN ('Created','Running') ORDER BY created_at DESC LIMIT 1", lookupConnection);
        command.Parameters.AddWithValue("@player", request.PlayerId); command.Parameters.AddWithValue("@home", request.HomeRealmId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            var existing = new BattleRoom(reader.GetString(0), request.PlayerId, request.HomeRealmId, reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetDateTime(8));
            await redis.SetJsonAsync(RedisBackendStore.BattleKey(existing.RoomId), existing, TimeSpan.FromHours(1));
            return existing;
        }
        await reader.DisposeAsync();
        var room = new BattleRoom(Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(), request.PlayerId, request.HomeRealmId,
            request.WorldClusterId, request.InstanceId, request.SceneId, request.WorldEpoch, request.SimulationVersion, request.ConfigVersion, "Created", DateTimeOffset.UtcNow);
        await using (var insertConnection = await mysql.OpenAsync(cancellationToken))
        await using (var insert = new MySqlCommand("INSERT INTO battle_rooms(room_id,player_id,home_realm_id,world_cluster_id,instance_id,scene_id,world_epoch,simulation_version,config_version,state,created_at) VALUES (@room,@player,@home,@cluster,@instance,@scene,@epoch,@simulation,@config,@state,@created)", insertConnection))
        {
            insert.Parameters.AddWithValue("@room", room.RoomId); insert.Parameters.AddWithValue("@player", room.PlayerId); insert.Parameters.AddWithValue("@home", room.HomeRealmId);
            insert.Parameters.AddWithValue("@cluster", room.WorldClusterId); insert.Parameters.AddWithValue("@instance", room.InstanceId); insert.Parameters.AddWithValue("@scene", room.SceneId);
            insert.Parameters.AddWithValue("@epoch", room.WorldEpoch); insert.Parameters.AddWithValue("@simulation", room.SimulationVersion); insert.Parameters.AddWithValue("@config", room.ConfigVersion);
            insert.Parameters.AddWithValue("@state", room.State); insert.Parameters.AddWithValue("@created", room.CreatedAt.UtcDateTime);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await redis.SetJsonAsync(RedisBackendStore.BattleKey(room.RoomId), room, TimeSpan.FromHours(1));
        return room;
    }

    public async Task<BattleRoom?> GetAsync(string roomId, CancellationToken cancellationToken = default)
    {
        var cached = await redis.GetJsonAsync<BattleRoom>(RedisBackendStore.BattleKey(roomId), cancellationToken);
        if (cached is not null) return cached;
        await using var connection = await mysql.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand("SELECT player_id,home_realm_id,world_cluster_id,instance_id,scene_id,world_epoch,simulation_version,config_version,state,created_at FROM battle_rooms WHERE room_id=@room", connection);
        command.Parameters.AddWithValue("@room", roomId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var room = new BattleRoom(roomId, reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetInt64(5), reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetDateTime(9));
        await redis.SetJsonAsync(RedisBackendStore.BattleKey(roomId), room, TimeSpan.FromHours(1));
        return room;
    }

    private static void Validate(BattleRoomRequest r)
    {
        if (r is null || r.PlayerId <= 0 || !Valid(r.HomeRealmId) || !Valid(r.WorldClusterId) || !Valid(r.InstanceId) || !Valid(r.SceneId) || r.WorldEpoch < 1 || !Valid(r.SimulationVersion) || !Valid(r.ConfigVersion))
            throw new ArgumentException("Invalid battle room request.");
    }
    private static bool Valid(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 96 && value.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.');
}
