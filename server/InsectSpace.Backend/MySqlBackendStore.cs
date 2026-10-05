using MySqlConnector;

namespace InsectSpace.Backend;

public sealed class MySqlBackendStore : IAsyncDisposable
{
    private readonly string connectionString;
    public MySqlBackendStore(string connectionString) => this.connectionString = connectionString;

    public async Task<MySqlConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public async Task ApplyMigrationsAsync(string migrationDirectory, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await ExecuteAsync(connection, null, "CREATE TABLE IF NOT EXISTS schema_migrations (version VARCHAR(128) NOT NULL PRIMARY KEY, applied_at DATETIME(6) NOT NULL)", cancellationToken);
        foreach (var path in Directory.EnumerateFiles(migrationDirectory, "*.sql").OrderBy(x => x, StringComparer.Ordinal))
        {
            var version = Path.GetFileNameWithoutExtension(path);
            await using var check = new MySqlCommand("SELECT 1 FROM schema_migrations WHERE version=@version", connection);
            check.Parameters.AddWithValue("@version", version);
            if (await check.ExecuteScalarAsync(cancellationToken) != null) continue;
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                foreach (var statement in SplitStatements(await File.ReadAllTextAsync(path, cancellationToken)))
                    await ExecuteAsync(connection, transaction, statement, cancellationToken);
                await ExecuteAsync(connection, transaction, "INSERT INTO schema_migrations(version, applied_at) VALUES (@version, UTC_TIMESTAMP(6))", cancellationToken, ("@version", version));
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
    }

    public static async Task<bool> CanConnectAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand("SELECT 1", connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    internal static async Task ExecuteAsync(MySqlConnection connection, MySqlTransaction? transaction, string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
    {
        await using var command = new MySqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static IEnumerable<string> SplitStatements(string sql)
        => sql.Split(';').Select(x => x.Trim()).Where(x => x.Length > 0 && !x.StartsWith("--", StringComparison.Ordinal));
}
