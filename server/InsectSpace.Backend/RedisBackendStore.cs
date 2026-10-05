using System.Text.Json;
using StackExchange.Redis;

namespace InsectSpace.Backend;

public sealed class RedisBackendStore : IAsyncDisposable
{
    private const string ReleaseLockScript = "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";
    private readonly ConnectionMultiplexer connection;
    public IDatabase Database { get; }

    private RedisBackendStore(ConnectionMultiplexer connection)
    {
        this.connection = connection;
        Database = connection.GetDatabase();
    }

    public static async Task<RedisBackendStore> ConnectAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        var connection = await ConnectionMultiplexer.ConnectAsync(connectionString).WaitAsync(cancellationToken);
        return new RedisBackendStore(connection);
    }

    public static string SessionKey(string tokenHash) => $"insectspace:session:{tokenHash}";
    public static string RouteKey(long playerId, string homeRealmId) => $"insectspace:route:{homeRealmId}:{playerId}";
    public static string WorldKey(string clusterId, string instanceId) => $"insectspace:world:{clusterId}:{instanceId}";
    public static string BattleKey(string roomId) => $"insectspace:battle:{roomId}";
    public static string CommandKey(string domain, long playerId, string operationId) => $"insectspace:command:{domain}:{playerId}:{operationId}";
    public static string LockKey(string name) => $"insectspace:lock:{name}";

    public Task<bool> SetJsonAsync<T>(string key, T value, TimeSpan expiry, When when = When.Always)
        => Database.StringSetAsync(key, JsonSerializer.Serialize(value), expiry, when);

    public async Task<T?> GetJsonAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var value = await Database.StringGetAsync(key).WaitAsync(cancellationToken);
        return value.IsNullOrEmpty ? default : JsonSerializer.Deserialize<T>(value.ToString());
    }

    public async Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default)
    {
        var value = await Database.StringGetAsync(key).WaitAsync(cancellationToken);
        return value.IsNullOrEmpty ? null : value.ToString();
    }

    public Task<bool> SetStringAsync(string key, string value, TimeSpan expiry)
        => Database.StringSetAsync(key, value, expiry);

    public Task<bool> DeleteAsync(string key) => Database.KeyDeleteAsync(key);

    public async Task<IAsyncDisposable?> TryLockAsync(string name, TimeSpan expiry, CancellationToken cancellationToken = default)
    {
        var key = LockKey(name);
        var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        if (!await Database.StringSetAsync(key, token, expiry, When.NotExists).WaitAsync(cancellationToken)) return null;
        return new Lease(Database, key, token);
    }

    public async ValueTask DisposeAsync() => await connection.CloseAsync();

    private sealed class Lease : IAsyncDisposable
    {
        private readonly IDatabase database;
        private readonly RedisKey key;
        private readonly RedisValue token;
        private int released;
        public Lease(IDatabase database, RedisKey key, RedisValue token) { this.database = database; this.key = key; this.token = token; }
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
                await database.ScriptEvaluateAsync(ReleaseLockScript, new[] { key }, new[] { token });
        }
    }
}
