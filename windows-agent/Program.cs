using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using STMediaBridge;

var configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductInfo.DataDirectory, "agent.json");
for (var i = 0; i < args.Length; i++)
    if (args[i] == "--config" && i + 1 < args.Length) configPath = Path.GetFullPath(args[++i]);
AgentConfig config;
try
{
    if (args.Contains("--init")) { AgentConfig.Create(configPath); return; }
    if (args.Contains("--rotate-token")) { AgentConfig.RotateToken(configPath); return; }
    config = AgentConfig.Load(configPath);
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
{
    using var logs = new FileLog(Path.Combine(Path.GetDirectoryName(configPath)!, "logs"));
    logs.CreateLogger("Startup").LogError("Configuration could not be loaded/created ({Type}). Check agent.json and setup instructions; legacy tokens require --rotate-token.", ex.GetType().Name);
    Environment.ExitCode = 1;
    return;
}
bool showPairing = false;
while (true)
{
AgentConfig? replacement = null;
var builder = WebApplication.CreateSlimBuilder();
builder.Logging.ClearProviders();
builder.Logging.AddProvider(new FileLog(Path.Combine(Path.GetDirectoryName(configPath)!, "logs")));
builder.WebHost.ConfigureKestrel(server =>
{
    server.Listen(IPAddress.Parse(config.BindAddress), config.Port);
    server.Limits.MaxRequestBodySize = 1024;
    server.Limits.MaxConcurrentConnections = 16;
    server.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(5);
});
var pairingSession = new PairingSession();
var state = new StateStore(config.DeviceId);
builder.Services.AddSingleton(state);
builder.Services.AddSingleton<AudioController>();
builder.Services.AddSingleton<MediaController>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AudioController>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<MediaController>());
if (!args.Contains("--no-tray"))
    builder.Services.AddSingleton<IHostedService>(sp => new TrayService(config, state,
        sp.GetRequiredService<IHostApplicationLifetime>(), sp.GetRequiredService<ILogger<TrayService>>(),
        regenerate: () => replacement = AgentConfig.RegenerateIdentity(configPath), showPairing: showPairing, session: pairingSession,
        startup: new StartupSettings(Path.Combine(AppContext.BaseDirectory, "STMediaBridge.Agent.exe"), configPath)));
var app = builder.Build();
// AgentConfig enforces the 32-hex-character contract. Compare the complete
// credential in constant time; never truncate or normalize a supplied token.
var expected = Encoding.ASCII.GetBytes("Bearer " + config.Token);
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers.CacheControl = "no-store";
    var remote = ctx.Connection.RemoteIpAddress?.MapToIPv4();
    if (remote is null || (!IPAddress.IsLoopback(remote) && remote.ToString() != config.HubAddress))
    { ctx.Response.StatusCode = 403; return; }
    if (ctx.Request.Path == "/v1/pair" && HttpMethods.IsPost(ctx.Request.Method))
    {
        await next(ctx);
        return;
    }
    var supplied = Encoding.UTF8.GetBytes(ctx.Request.Headers.Authorization.ToString());
    if (!CryptographicOperations.FixedTimeEquals(expected, supplied))
    { ctx.Response.StatusCode = 401; return; }
    try { await next(ctx); }
    catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested || app.Lifetime.ApplicationStopping.IsCancellationRequested) { }
});
app.MapPost("/v1/pair", async (HttpContext ctx) =>
{
    JsonDocument body;
    try { body = await JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ctx.RequestAborted); }
    catch (JsonException) { return Results.BadRequest(); }
    using (body)
    {
        var root = body.RootElement;
        var code = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("code", out var value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        var status = pairingSession.Exchange(code);
        return status == 200 ? Results.Ok(new { deviceId = config.DeviceId, token = config.Token }) : Results.StatusCode(status);
    }
});
app.MapGet("/v1/state", () => state.Read());
app.MapGet("/v1/events", async (string? epoch, long? after, HttpContext ctx) =>
{
    using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted, app.Lifetime.ApplicationStopping);
    return await state.WaitAsync(epoch, after ?? -1, TimeSpan.FromSeconds(20), cancellation.Token);
});
app.MapPost("/v1/command", async (HttpContext ctx, AudioController audio, MediaController media) =>
{
    JsonDocument body;
    try { body = await JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ctx.RequestAborted); }
    catch (JsonException) { return Results.BadRequest(new { error = "invalid_json" }); }
    using (body)
    {
        var root = body.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("command", out var c) || c.ValueKind != JsonValueKind.String)
            return Results.BadRequest(new { error = "invalid_command" });
        var command = c.GetString();
        var hasValue = root.TryGetProperty("value", out var value);
        bool accepted;
        switch (command)
        {
            case "setVolume":
                if (!hasValue || !value.TryGetInt32Safe(out var volume) || volume is < 0 or > 100)
                    return Results.BadRequest(new { error = "volume_must_be_integer_0_100" });
                accepted = audio.Set(volume: volume); break;
            case "adjustVolume":
                if (!hasValue || !value.TryGetInt32Safe(out var delta) || delta is < -100 or > 100)
                    return Results.BadRequest(new { error = "invalid_delta" });
                accepted = audio.Set(delta: delta); break;
            case "setMute":
                if (!hasValue || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return Results.BadRequest(new { error = "mute_must_be_boolean" });
                accepted = audio.Set(muted: value.GetBoolean()); break;
            case "play": case "pause": case "toggle": case "next": case "previous":
                accepted = await media.CommandAsync(command, ctx.RequestAborted); break;
            default: return Results.BadRequest(new { error = "unknown_command" });
        }
        // Acknowledgement is not optimistic state; the event stream reports what Windows observes.
        return accepted ? Results.Ok(new { accepted = true }) : Results.Conflict(new { error = "unavailable_or_unsupported" });
    }
});
await using (app)
{
    await app.StartAsync();
    await app.WaitForShutdownAsync();
}
if (replacement is null) break;
config = replacement;
showPairing = true;
}

internal static class JsonValueExtensions
{
    public static bool TryGetInt32Safe(this JsonElement value, out int number)
    {
        number = 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out number);
    }
}
