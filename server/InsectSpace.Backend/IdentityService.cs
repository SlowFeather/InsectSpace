using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Frozen;
using MySqlConnector;

namespace InsectSpace.Backend;

public interface IIdentityProvider
{
    Task<ExternalIdentity> ResolveAsync(LoginRequest request, CancellationToken cancellationToken = default);
}

public sealed class LocalIdentityProvider : IIdentityProvider
{
    public Task<ExternalIdentity> ResolveAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        if (Environment.GetEnvironmentVariable("INSECTSPACE_LOCAL_BACKEND") != "true")
            throw new NotSupportedException("Local/test identity requires the explicit local backend topology.");
        if (request is null || string.IsNullOrWhiteSpace(request.Credential) || request.Credential.Length > 191)
            throw new ArgumentException("A local identity credential is required.");
        if (!string.Equals(request.Provider, "local", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(request.Provider, "test", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"Identity provider '{request.Provider}' is not installed.");
        return Task.FromResult(new ExternalIdentity(request.Provider.ToLowerInvariant(), request.Credential.Trim(), request.Credential.Trim()));
    }
}

public interface IPhoneCodeSender
{
    Task SendAsync(string phoneNumber, string code, CancellationToken cancellationToken = default);
}

public sealed class DisabledPhoneCodeSender : IPhoneCodeSender
{
    public Task SendAsync(string phoneNumber, string code, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("No production SMS provider is configured.");
}

public sealed class IdentityService
{
    private static readonly Regex PhonePattern = new("^\\+[1-9][0-9]{6,14}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly MySqlBackendStore mysql;
    private readonly RedisBackendStore redis;
    private readonly BackendOptions options;
    private readonly IIdentityProvider provider;
    private readonly IPhoneCodeSender phoneSender;
    private readonly string phoneMode;
    private readonly FrozenSet<string> phoneWhitelist;

    public IdentityService(MySqlBackendStore mysql, RedisBackendStore redis, BackendOptions options, IIdentityProvider provider,
        IPhoneCodeSender? phoneSender = null)
    {
        this.mysql = mysql; this.redis = redis; this.options = options; this.provider = provider;
        this.phoneSender = phoneSender ?? new DisabledPhoneCodeSender();
        phoneMode = Environment.GetEnvironmentVariable("INSECTSPACE_PHONE_AUTH_MODE") ?? "production";
        if (phoneMode is not ("test" or "production")) throw new InvalidOperationException("Phone authentication mode must be explicitly test or production.");
        phoneWhitelist = LoadPhoneWhitelist();
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var identity = await provider.ResolveAsync(request, cancellationToken);
        return await CreateSessionAsync(identity, cancellationToken);
    }

    public async Task<PhoneOtpResponse> RequestPhoneCodeAsync(PhoneOtpRequest request, CancellationToken cancellationToken = default)
    {
        var phone = NormalizePhone(request?.PhoneNumber);
        var lease = await redis.TryLockAsync($"otp:{phone}", TimeSpan.FromSeconds(2), cancellationToken);
        if (lease is null) throw new InvalidOperationException("Please wait before requesting another code.");
        await using (lease)
        {
            var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            var expires = DateTimeOffset.UtcNow.AddMinutes(5);
            var digest = HashCode(phone + ":" + code);
            if (phoneMode.Equals("production", StringComparison.OrdinalIgnoreCase))
                await phoneSender.SendAsync(phone, code, cancellationToken);
            else if (!phoneMode.Equals("test", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("INSECTSPACE_PHONE_AUTH_MODE must be 'test' or 'production'.");
            await redis.SetStringAsync($"insectspace:otp:{phone}", digest, TimeSpan.FromMinutes(5));
            return new PhoneOtpResponse(true, phone, expires, phoneMode.Equals("test", StringComparison.OrdinalIgnoreCase) ? code : null);
        }
    }

    public async Task<LoginResponse> VerifyPhoneAsync(PhoneLoginRequest request, CancellationToken cancellationToken = default)
    {
        var phone = NormalizePhone(request?.PhoneNumber);
        if (IsPhoneWhitelisted(phone))
            return await CreateSessionAsync(new ExternalIdentity("phone", phone, phone), cancellationToken);

        if (request is null || request.Code is null || !Regex.IsMatch(request.Code, "^[0-9]{6}$"))
            throw new ArgumentException("A six digit verification code is required.");
        var consumed = await redis.Database.ScriptEvaluateAsync(
            "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end",
            new StackExchange.Redis.RedisKey[] { $"insectspace:otp:{phone}" },
            new StackExchange.Redis.RedisValue[] { HashCode(phone + ":" + request.Code) }).WaitAsync(cancellationToken);
        if ((long)consumed != 1)
            throw new UnauthorizedAccessException("Verification code is invalid or expired.");
        return await CreateSessionAsync(new ExternalIdentity("phone", phone, phone), cancellationToken);
    }

    public bool IsPhoneWhitelisted(string phoneNumber)
        => phoneWhitelist.Contains(NormalizePhone(phoneNumber));

    public async Task<SessionContext?> ResolveSessionAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length != 64 || !token.All(Uri.IsHexDigit)) return null;
        var tokenHash = HashToken(token);
        // Long-lived credentials always check the durable revocation/expiry authority.
        // A stale Redis value must never resurrect a concurrently revoked session.
        await using var connection = await mysql.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand("SELECT player_id, home_realm_id, expires_at FROM sessions WHERE token_hash=@hash AND revoked_at IS NULL", connection);
        command.Parameters.AddWithValue("@hash", tokenHash);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var context = new SessionContext(tokenHash, reader.GetInt64(0), reader.GetString(1),
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc)));
        if (context.ExpiresAt <= DateTimeOffset.UtcNow) return null;
        await redis.SetJsonAsync(RedisBackendStore.SessionKey(tokenHash), context, context.ExpiresAt - DateTimeOffset.UtcNow);
        return context;
    }

    public async Task RevokeAsync(string token, CancellationToken cancellationToken = default)
    {
        var hash = HashToken(token);
        await using var connection = await mysql.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand("UPDATE sessions SET revoked_at=UTC_TIMESTAMP(6) WHERE token_hash=@hash", connection);
        command.Parameters.AddWithValue("@hash", hash);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await redis.DeleteAsync(RedisBackendStore.SessionKey(hash));
    }

    public static string NormalizePhone(string? value)
    {
        var phone = (value ?? string.Empty).Trim().Replace(" ", string.Empty).Replace("-", string.Empty);
        if (!PhonePattern.IsMatch(phone)) throw new ArgumentException("Phone number must be in E.164 format.");
        return phone;
    }

    private async Task<LoginResponse> CreateSessionAsync(ExternalIdentity identity, CancellationToken cancellationToken)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var tokenHash = HashToken(token);
        var expires = DateTimeOffset.UtcNow.Add(options.SessionLifetime);
        // Match DATETIME(6) so every Token login returns the original fixed expiry.
        expires = new DateTimeOffset(expires.Ticks - expires.Ticks % 10, TimeSpan.Zero);
        await using var connection = await mysql.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var insert = new MySqlCommand("INSERT INTO players(external_provider, external_subject, display_name, home_realm_id, created_at) VALUES (@provider,@subject,@display,'home-default',UTC_TIMESTAMP(6)) ON DUPLICATE KEY UPDATE display_name=VALUES(display_name)", connection, transaction))
        {
            insert.Parameters.AddWithValue("@provider", identity.Provider);
            insert.Parameters.AddWithValue("@subject", identity.Subject);
            insert.Parameters.AddWithValue("@display", identity.DisplayName ?? identity.Subject);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        long playerId;
        string homeRealm;
        await using (var select = new MySqlCommand("SELECT player_id, home_realm_id FROM players WHERE external_provider=@provider AND external_subject=@subject", connection, transaction))
        {
            select.Parameters.AddWithValue("@provider", identity.Provider);
            select.Parameters.AddWithValue("@subject", identity.Subject);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Player creation did not produce an identity.");
            playerId = reader.GetInt64(0); homeRealm = reader.GetString(1);
        }
        await using (var session = new MySqlCommand("INSERT INTO sessions(token_hash, player_id, home_realm_id, expires_at, created_at) VALUES (@hash,@player,@home,@expires,UTC_TIMESTAMP(6))", connection, transaction))
        {
            session.Parameters.AddWithValue("@hash", tokenHash); session.Parameters.AddWithValue("@player", playerId);
            session.Parameters.AddWithValue("@home", homeRealm); session.Parameters.AddWithValue("@expires", expires.UtcDateTime);
            await session.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        await redis.SetJsonAsync(RedisBackendStore.SessionKey(tokenHash), new SessionContext(tokenHash, playerId, homeRealm, expires), options.SessionLifetime);
        return new LoginResponse(token, playerId, homeRealm, expires);
    }

    private string HashToken(string token) => HashCode(options.SessionSigningKey + ":" + token);
    private static string HashCode(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static FrozenSet<string> LoadPhoneWhitelist()
    {
        var configured = Environment.GetEnvironmentVariable("INSECTSPACE_PHONE_WHITELIST");
        if (string.IsNullOrWhiteSpace(configured))
            return Array.Empty<string>().ToFrozenSet(StringComparer.Ordinal);

        var phones = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in configured.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                phones.Add(NormalizePhone(item));
            }
            catch (ArgumentException)
            {
                throw new InvalidOperationException("INSECTSPACE_PHONE_WHITELIST contains an invalid E.164 phone number.");
            }
        }
        return phones.ToFrozenSet(StringComparer.Ordinal);
    }
}
