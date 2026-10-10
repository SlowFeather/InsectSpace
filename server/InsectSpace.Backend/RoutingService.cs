using MySqlConnector;

namespace InsectSpace.Backend;

public sealed class RoutingService
{
    private readonly MySqlBackendStore mysql;
    private readonly RedisBackendStore redis;
    public RoutingService(MySqlBackendStore mysql, RedisBackendStore redis) { this.mysql = mysql; this.redis = redis; }

    public async Task RegisterWorldAsync(WorldRegistration registration, CancellationToken cancellationToken = default)
    {
        ValidateRegistration(registration);
        var lease = await redis.TryLockAsync($"world:{registration.WorldClusterId}:{registration.InstanceId}", TimeSpan.FromSeconds(5), cancellationToken);
        if (lease is null) throw new InvalidOperationException("World instance registration is busy.");
        await using (lease)
        await using (var connection = await mysql.OpenAsync(cancellationToken))
        await using (var command = new MySqlCommand("INSERT INTO world_instances(world_cluster_id,instance_id,scene_id,endpoint,capacity,epoch,heartbeat_at) VALUES (@cluster,@instance,@scene,@endpoint,@capacity,@epoch,UTC_TIMESTAMP(6)) ON DUPLICATE KEY UPDATE scene_id=VALUES(scene_id),endpoint=VALUES(endpoint),capacity=VALUES(capacity),epoch=GREATEST(epoch,VALUES(epoch)),heartbeat_at=UTC_TIMESTAMP(6)", connection))
        {
            command.Parameters.AddWithValue("@cluster", registration.WorldClusterId); command.Parameters.AddWithValue("@instance", registration.InstanceId);
            command.Parameters.AddWithValue("@scene", registration.SceneId); command.Parameters.AddWithValue("@endpoint", registration.Endpoint);
            command.Parameters.AddWithValue("@capacity", registration.Capacity); command.Parameters.AddWithValue("@epoch", registration.Epoch);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await redis.SetJsonAsync(RedisBackendStore.WorldKey(registration.WorldClusterId, registration.InstanceId), registration, TimeSpan.FromSeconds(30));
    }

    public async Task<WorldRoute> JoinWorldAsync(RouteRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRouteRequest(request);
        var lease = await redis.TryLockAsync($"route:{request.HomeRealmId}:{request.PlayerId}", TimeSpan.FromSeconds(5), cancellationToken);
        if (lease is null) throw new InvalidOperationException("World route is being changed; retry the request.");
        await using (lease)
        await using (var connection = await mysql.OpenAsync(cancellationToken))
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            long currentEpoch = 0;
            await using (var select = new MySqlCommand("SELECT epoch FROM world_routes WHERE player_id=@player AND home_realm_id=@home FOR UPDATE", connection, transaction))
            {
                select.Parameters.AddWithValue("@player", request.PlayerId); select.Parameters.AddWithValue("@home", request.HomeRealmId);
                var value = await select.ExecuteScalarAsync(cancellationToken); if (value is not null) currentEpoch = Convert.ToInt64(value);
            }
            var instance = await SelectInstanceAsync(connection, transaction, request, cancellationToken);
            var route = new WorldRoute(request.HomeRealmId, instance.Cluster, instance.Instance, instance.Scene, checked(currentEpoch + 1), instance.Endpoint);
            await using var upsert = new MySqlCommand("INSERT INTO world_routes(player_id,home_realm_id,world_cluster_id,instance_id,scene_id,endpoint,epoch,updated_at) VALUES (@player,@home,@cluster,@instance,@scene,@endpoint,@epoch,UTC_TIMESTAMP(6)) ON DUPLICATE KEY UPDATE world_cluster_id=VALUES(world_cluster_id),instance_id=VALUES(instance_id),scene_id=VALUES(scene_id),endpoint=VALUES(endpoint),epoch=VALUES(epoch),updated_at=UTC_TIMESTAMP(6)", connection, transaction);
            upsert.Parameters.AddWithValue("@player", request.PlayerId); upsert.Parameters.AddWithValue("@home", route.HomeRealmId);
            upsert.Parameters.AddWithValue("@cluster", route.WorldClusterId); upsert.Parameters.AddWithValue("@instance", route.InstanceId);
            upsert.Parameters.AddWithValue("@scene", route.SceneId); upsert.Parameters.AddWithValue("@endpoint", route.Endpoint); upsert.Parameters.AddWithValue("@epoch", route.Epoch);
            await upsert.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await redis.SetJsonAsync(RedisBackendStore.RouteKey(request.PlayerId, request.HomeRealmId), route, TimeSpan.FromSeconds(30));
            return route;
        }
    }

    public async Task<WorldRoute?> ResolveRouteAsync(long playerId, string homeRealmId, CancellationToken cancellationToken = default)
    {
        var cached = await redis.GetJsonAsync<WorldRoute>(RedisBackendStore.RouteKey(playerId, homeRealmId), cancellationToken);
        if (cached is not null) return cached;
        await using var connection = await mysql.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand("SELECT world_cluster_id,instance_id,scene_id,endpoint,epoch FROM world_routes WHERE player_id=@player AND home_realm_id=@home", connection);
        command.Parameters.AddWithValue("@player", playerId); command.Parameters.AddWithValue("@home", homeRealmId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var route = new WorldRoute(homeRealmId, reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(4), reader.GetString(3));
        await redis.SetJsonAsync(RedisBackendStore.RouteKey(playerId, homeRealmId), route, TimeSpan.FromSeconds(30));
        return route;
    }

    private static async Task<(string Cluster, string Instance, string Scene, string Endpoint)> SelectInstanceAsync(MySqlConnection connection, MySqlTransaction transaction, RouteRequest request, CancellationToken cancellationToken)
    {
        var sql = "SELECT world_cluster_id,instance_id,scene_id,endpoint FROM world_instances WHERE heartbeat_at >= UTC_TIMESTAMP(6) - INTERVAL 30 SECOND AND capacity > 0";
        if (!string.IsNullOrWhiteSpace(request.PreferredClusterId)) sql += " AND world_cluster_id=@cluster";
        if (!string.IsNullOrWhiteSpace(request.PreferredSceneId)) sql += " AND scene_id=@scene";
        sql += " ORDER BY heartbeat_at DESC, world_cluster_id, instance_id LIMIT 1 FOR UPDATE";
        await using var command = new MySqlCommand(sql, connection, transaction);
        if (!string.IsNullOrWhiteSpace(request.PreferredClusterId)) command.Parameters.AddWithValue("@cluster", request.PreferredClusterId);
        if (!string.IsNullOrWhiteSpace(request.PreferredSceneId)) command.Parameters.AddWithValue("@scene", request.PreferredSceneId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("No healthy world instance is available.");
        return (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
    }

    private static void ValidateRegistration(WorldRegistration r)
    {
        if (r is null || !ValidId(r.WorldClusterId) || !ValidId(r.InstanceId) || !ValidId(r.SceneId) || r.Endpoint.Length is < 1 or > 255 || r.Capacity <= 0 || r.Epoch < 0)
            throw new ArgumentException("Invalid world registration.");
    }
    private static void ValidateRouteRequest(RouteRequest r)
    {
        if (r is null || r.PlayerId <= 0 || !ValidId(r.HomeRealmId) || (r.PreferredClusterId is not null && !ValidId(r.PreferredClusterId)) || (r.PreferredSceneId is not null && !ValidId(r.PreferredSceneId)))
            throw new ArgumentException("Invalid route request.");
    }
    private static bool ValidId(string value) => value.Length is >= 1 and <= 96 && value.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.');
}
