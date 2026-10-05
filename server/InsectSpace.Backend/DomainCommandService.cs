using System.Security.Cryptography;
using System.Text;
using MySqlConnector;

namespace InsectSpace.Backend;

public sealed class DomainCommandService
{
    private static readonly HashSet<string> Domains = new(StringComparer.OrdinalIgnoreCase) { "economy", "inventory", "quest", "activity" };
    private readonly MySqlBackendStore mysql;
    private readonly RedisBackendStore redis;
    public DomainCommandService(MySqlBackendStore mysql, RedisBackendStore redis) { this.mysql = mysql; this.redis = redis; }

    // This is a durable command inbox only. Domain rules and settlement handlers are deliberately versioned separately.
    public async Task<DurableCommand> AcceptAsync(DomainCommandRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null || !Domains.Contains(request.Domain) || request.PlayerId <= 0 || !ValidOperation(request.OperationId))
            throw new ArgumentException("Invalid domain command envelope.");
        var payload = request.Payload.GetRawText();
        if (payload.Length > 32768) throw new ArgumentException("Domain command payload is too large.");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        var cacheKey = RedisBackendStore.CommandKey(request.Domain, request.PlayerId, request.OperationId);
        var cached = await redis.GetJsonAsync<DurableCommand>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            if (!cached.RequestHash.Equals(hash, StringComparison.Ordinal)) throw new InvalidOperationException("Operation id was reused with a different payload.");
            return cached;
        }
        var lease = await redis.TryLockAsync($"command:{request.Domain}:{request.PlayerId}:{request.OperationId}", TimeSpan.FromSeconds(5), cancellationToken);
        if (lease is null) throw new InvalidOperationException("Command is being processed; retry with the same operation id.");
        await using (lease)
        await using (var connection = await mysql.OpenAsync(cancellationToken))
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using (var select = new MySqlCommand("SELECT request_hash,payload_json,status,accepted_at FROM domain_command_inbox WHERE domain_name=@domain AND player_id=@player AND operation_id=@operation", connection, transaction))
            {
                select.Parameters.AddWithValue("@domain", request.Domain); select.Parameters.AddWithValue("@player", request.PlayerId); select.Parameters.AddWithValue("@operation", request.OperationId);
                await using var reader = await select.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    var storedHash = reader.GetString(0);
                    if (!storedHash.Equals(hash, StringComparison.Ordinal)) throw new InvalidOperationException("Operation id was reused with a different payload.");
                    var existing = new DurableCommand(request.Domain, request.OperationId, request.PlayerId, reader.GetString(1), storedHash, reader.GetString(2), reader.GetDateTime(3));
                    await transaction.CommitAsync(cancellationToken);
                    await redis.SetJsonAsync(cacheKey, existing, TimeSpan.FromHours(24));
                    return existing;
                }
            }
            await using (var insert = new MySqlCommand("INSERT INTO domain_command_inbox(domain_name,player_id,operation_id,request_hash,payload_json,status,accepted_at) VALUES (@domain,@player,@operation,@hash,@payload,'Accepted',UTC_TIMESTAMP(6))", connection, transaction))
            {
                insert.Parameters.AddWithValue("@domain", request.Domain); insert.Parameters.AddWithValue("@player", request.PlayerId); insert.Parameters.AddWithValue("@operation", request.OperationId);
                insert.Parameters.AddWithValue("@hash", hash); insert.Parameters.AddWithValue("@payload", payload); await insert.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        var accepted = new DurableCommand(request.Domain, request.OperationId, request.PlayerId, payload, hash, "Accepted", DateTimeOffset.UtcNow);
        await redis.SetJsonAsync(cacheKey, accepted, TimeSpan.FromHours(24));
        return accepted;
    }

    private static bool ValidOperation(string operation) => !string.IsNullOrWhiteSpace(operation) && operation.Length <= 96 && operation.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or ':');
}
