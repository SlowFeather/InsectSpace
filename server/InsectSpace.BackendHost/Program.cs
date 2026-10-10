using System.Net;
using System.Text.Json;
using InsectSpace.Backend;

var role = BackendServiceRoleParser.Parse(args);
var options = BackendOptions.FromEnvironment();
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://{options.BindAddress}:{options.Port(role)}");
builder.Services.ConfigureHttpJsonOptions(settings => settings.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

await using var runtime = await BackendRuntime.CreateAsync(options);
builder.Services.AddSingleton(runtime);
if (role == BackendServiceRole.World && Environment.GetEnvironmentVariable("INSECTSPACE_LOCAL_BACKEND") == "true")
    builder.Services.AddHostedService<LocalWorldHeartbeat>();
var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { service = role.ToString().ToLowerInvariant(), port = options.Port(role) }));
app.MapGet("/healthz", async (BackendRuntime state, CancellationToken cancellationToken)
    => Results.Ok(await state.HealthAsync(role, cancellationToken)));

if (role is BackendServiceRole.Gateway or BackendServiceRole.Identity)
{
    app.MapPost("/v1/identity/login", async (LoginRequest request, BackendRuntime state, CancellationToken cancellationToken)
        => await ExecuteAsync(() => state.Identity.LoginAsync(request, cancellationToken)));
    app.MapPost("/v1/identity/phone/code", async (PhoneOtpRequest request, BackendRuntime state, CancellationToken cancellationToken)
        => await ExecuteAsync(() => state.Identity.RequestPhoneCodeAsync(request, cancellationToken)));
    app.MapPost("/v1/identity/phone/login", async (PhoneLoginRequest request, BackendRuntime state, CancellationToken cancellationToken)
        => await ExecuteAsync(() => state.Identity.VerifyPhoneAsync(request, cancellationToken)));
    app.MapPost("/v1/identity/session/resolve", async (SessionTokenRequest request, BackendRuntime state, CancellationToken cancellationToken)
        => await ExecuteAsync(() => state.Identity.ResolveSessionAsync(request.Token, cancellationToken)));
    app.MapPost("/v1/identity/session/revoke", async (HttpRequest http, BackendRuntime state, CancellationToken cancellationToken)
        => await ExecuteAsync(async () => { await state.Identity.RevokeAsync(Bearer(http), cancellationToken); return new { revoked = true }; }));
    app.MapPost("/v1/identity/session/login", async (HttpRequest http, BackendRuntime state, CancellationToken cancellationToken)
        => await ExecuteAsync(async () => {
            var session = await state.Identity.ResolveSessionAsync(Bearer(http), cancellationToken)
                ?? throw new UnauthorizedAccessException("The session is invalid, expired, or revoked.");
            return new SessionProfile(session.PlayerId, session.HomeRealmId, session.ExpiresAt);
        }));
}

if (role is BackendServiceRole.Gateway or BackendServiceRole.Lobby or BackendServiceRole.World)
{
    if (role == BackendServiceRole.World)
        app.MapPost("/v1/world/register", async (HttpRequest http, WorldRegistration request, BackendRuntime state, CancellationToken cancellationToken)
            => await ExecuteAsync(async () => { RequireService(http); await state.Routing.RegisterWorldAsync(request, cancellationToken); return request; }));
    app.MapPost("/v1/world/join", async (HttpRequest http, RouteRequest request, BackendRuntime state, CancellationToken cancellationToken)
        => await ExecuteAsync(async () => { RequireHomeRealm(http, request.PlayerId, request.HomeRealmId, state); return await state.Routing.JoinWorldAsync(request, cancellationToken); }));
    app.MapGet("/v1/world/route/{playerId:long}", async (HttpRequest http, long playerId, string homeRealmId, BackendRuntime state, CancellationToken cancellationToken)
        => await ExecuteAsync(async () => { RequireHomeRealm(http, playerId, homeRealmId, state); return await state.Routing.ResolveRouteAsync(playerId, homeRealmId, cancellationToken); }));
}

if (role is BackendServiceRole.Gateway or BackendServiceRole.Battle)
{
    app.MapPost("/v1/battle/rooms", async (HttpRequest http, BattleRoomRequest request, BackendRuntime state, CancellationToken cancellationToken)
        => await ExecuteAsync(async () => { RequireHomeRealm(http, request.PlayerId, request.HomeRealmId, state); return await state.Battles.CreateAsync(request, cancellationToken); }));
    app.MapGet("/v1/battle/rooms/{roomId}", async (string roomId, BackendRuntime state, CancellationToken cancellationToken)
        => await ExecuteAsync(() => state.Battles.GetAsync(roomId, cancellationToken)));
}

if (role is BackendServiceRole.Gateway or BackendServiceRole.Worker)
{
    app.MapPost("/v1/domain/commands", async (HttpRequest http, DomainCommandRequest request, BackendRuntime state, CancellationToken cancellationToken)
        => await ExecuteAsync(async () => { RequirePlayer(http, request.PlayerId, state); return await state.Commands.AcceptAsync(request, cancellationToken); }));
}

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    var status = exception switch
    {
        UnauthorizedAccessException => (int)HttpStatusCode.Unauthorized,
        ArgumentException => (int)HttpStatusCode.BadRequest,
        NotSupportedException => (int)HttpStatusCode.NotImplemented,
        _ => (int)HttpStatusCode.Conflict
    };
    context.Response.StatusCode = status;
    await Results.Problem(exception?.Message ?? "Backend request failed", statusCode: status).ExecuteAsync(context);
}));

await app.RunAsync();

static async Task<IResult> ExecuteAsync<T>(Func<Task<T>> action)
{
    var result = await action();
    return result is null ? Results.NotFound() : Results.Ok(result);
}

static SessionContext RequirePlayer(HttpRequest request, long playerId, BackendRuntime runtime)
{
    var token = Bearer(request);
    var session = runtime.Identity.ResolveSessionAsync(token).GetAwaiter().GetResult();
    if (session is null || session.PlayerId != playerId) throw new UnauthorizedAccessException("A valid player session is required.");
    return session;
}

static void RequireHomeRealm(HttpRequest request, long playerId, string homeRealmId, BackendRuntime runtime)
{
    var session = RequirePlayer(request, playerId, runtime);
    if (!string.Equals(session.HomeRealmId, homeRealmId, StringComparison.Ordinal))
        throw new UnauthorizedAccessException("The session does not belong to the requested home realm.");
}

static string Bearer(HttpRequest request)
{
    var header = request.Headers.Authorization.ToString();
    if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("A bearer session token is required.");
    var token = header[7..].Trim();
    if (token.Length == 0) throw new UnauthorizedAccessException("A bearer session token is required.");
    return token;
}

static void RequireService(HttpRequest request)
{
    var expected = Environment.GetEnvironmentVariable("INSECTSPACE_INTERNAL_SERVICE_KEY");
    var actual = request.Headers["X-InsectSpace-Service-Key"].ToString();
    if (string.IsNullOrEmpty(expected) || expected.Length < 32 ||
        !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(expected), System.Text.Encoding.UTF8.GetBytes(actual)))
        throw new UnauthorizedAccessException("Internal service authentication is required.");
}

public sealed record SessionTokenRequest(string Token);

public sealed class LocalWorldHeartbeat(BackendRuntime runtime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Explicit local topology fixture, not an AOI transport implementation.
        var registration = new WorldRegistration("local-cluster", "local-1", "home", "127.0.0.1:19000", 100, 1);
        while (!stoppingToken.IsCancellationRequested)
        {
            await runtime.Routing.RegisterWorldAsync(registration, stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }
}
