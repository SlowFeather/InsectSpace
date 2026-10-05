namespace InsectSpace.Backend;

public sealed class BackendRuntime : IAsyncDisposable
{
    public BackendOptions Options { get; }
    public MySqlBackendStore MySql { get; }
    public RedisBackendStore Redis { get; }
    public IdentityService Identity { get; }
    public RoutingService Routing { get; }
    public BattleRoomService Battles { get; }
    public DomainCommandService Commands { get; }

    private BackendRuntime(BackendOptions options, MySqlBackendStore mysql, RedisBackendStore redis, IIdentityProvider provider)
    {
        Options = options; MySql = mysql; Redis = redis;
        Identity = new IdentityService(mysql, redis, options, provider);
        Routing = new RoutingService(mysql, redis);
        Battles = new BattleRoomService(mysql, redis);
        Commands = new DomainCommandService(mysql, redis);
    }

    public static async Task<BackendRuntime> CreateAsync(BackendOptions options, IIdentityProvider? provider = null, CancellationToken cancellationToken = default)
    {
        var mysql = new MySqlBackendStore(options.MySqlConnectionString);
        var redis = await RedisBackendStore.ConnectAsync(options.RedisConnectionString, cancellationToken);
        var runtime = new BackendRuntime(options, mysql, redis, provider ?? new LocalIdentityProvider());
        var migrations = Path.Combine(AppContext.BaseDirectory, "Database", "Migrations");
        if (!Directory.Exists(migrations)) migrations = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "server", "Database", "Migrations"));
        IAsyncDisposable? migrationLease = null;
        for (var attempt = 0; attempt < 30 && migrationLease is null; attempt++)
        {
            migrationLease = await redis.TryLockAsync("schema-migrations", TimeSpan.FromSeconds(10), cancellationToken);
            if (migrationLease is null) await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }
        if (migrationLease is null) throw new InvalidOperationException("Could not acquire the database migration lock.");
        await using (migrationLease)
            await mysql.ApplyMigrationsAsync(migrations, cancellationToken);
        return runtime;
    }

    public async Task<ServiceHealth> HealthAsync(BackendServiceRole role, CancellationToken cancellationToken = default)
    {
        var mysql = await MySqlBackendStore.CanConnectAsync(Options.MySqlConnectionString, cancellationToken);
        var redis = await Redis.Database.PingAsync() < TimeSpan.FromSeconds(5);
        return new ServiceHealth(role.ToString(), mysql, redis, DateTimeOffset.UtcNow);
    }

    public async ValueTask DisposeAsync()
    {
        await Redis.DisposeAsync();
        await MySql.DisposeAsync();
    }
}
