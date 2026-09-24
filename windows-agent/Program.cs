using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using STMediaBridge;

var configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductInfo.DataDirectory, "agent.json");
for (var i = 0; i < args.Length; i++)
    if (args[i] == "--config" && i + 1 < args.Length) configPath = Path.GetFullPath(args[++i]);
AgentConfig config;
var firstRun = false;
try
{
    if (args.Contains("--configure-firewall")) { FirstRun.ConfigureFirewall(configPath); return; }
    if (args.Contains("--init")) { AgentConfig.Create(configPath); return; }
    if (args.Contains("--rotate-token")) { AgentConfig.RotateToken(configPath); return; }
    if (args.Contains("--enable-https")) { TlsIdentity.Enable(configPath); return; }
    if (!args.Contains("--no-tray"))
    {
        if (!args.Contains("--config") && FirstRun.InstallPortable()) return;
        if (!File.Exists(configPath))
        {
            if (!FirstRun.Configure(configPath)) return;
            firstRun = true;
        }
    }
    config = AgentConfig.Load(configPath);
    if (!args.Contains("--no-tray") && !config.TlsEnabled)
    {
        TlsIdentity.Enable(configPath);
        config = AgentConfig.Load(configPath);
        firstRun = true;
    }
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or CryptographicException or System.ComponentModel.Win32Exception)
{
    using var logs = new FileLog(Path.Combine(Path.GetDirectoryName(configPath)!, "logs"));
    logs.CreateLogger("Startup").LogError("Configuration could not be loaded/created ({Type}). Check agent.json and setup instructions; legacy tokens require --rotate-token.", ex.GetType().Name);
    Environment.ExitCode = 1;
    if (!args.Contains("--no-tray") && !args.Contains("--configure-firewall")) MessageBox.Show("Could not start. " + ex.Message, ProductInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
    return;
}
using var singleInstance = new Semaphore(1, 1, "Local\\STWMC-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(configPath.ToUpperInvariant())))[..24]);
if (!singleInstance.WaitOne(0))
{
    if (!args.Contains("--no-tray")) MessageBox.Show(TrayContext.T("이미 실행 중입니다. 트레이 아이콘에서 페어링 정보를 여세요.", "Already running. Open pairing information from the tray."), ProductInfo.DisplayName);
    return;
}
try
{
bool showPairing = firstRun || args.Contains("--pairing");
while (true)
{
AgentConfig? replacement = null;
using var tlsIdentity = config.TlsEnabled ? TlsIdentity.Load(configPath) : null;
var builder = WebApplication.CreateSlimBuilder();
builder.Logging.ClearProviders();
builder.Logging.AddProvider(new FileLog(Path.Combine(Path.GetDirectoryName(configPath)!, "logs")));
builder.WebHost.ConfigureKestrel(server =>
{
    server.Listen(IPAddress.Parse(config.BindAddress), config.Port, listener =>
    {
        if (tlsIdentity is not null) listener.UseHttps(tls =>
        {
            tls.ServerCertificate = tlsIdentity;
            tls.SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13;
        });
    });
    server.Limits.MaxRequestBodySize = 1024;
    server.Limits.MaxConcurrentConnections = 16;
    server.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(5);
});
var pairingSession = new PairingSession();
var allowedHub = config.HubAddress;
var enrollmentGate = new object();
var state = new StateStore(config.DeviceId);
builder.Services.AddSingleton(state);
var apps = new AppCatalog(Path.Combine(Path.GetDirectoryName(configPath)!, "audio-apps.json"), state);
builder.Services.AddSingleton(apps);
builder.Services.AddSingleton<AppAudioController>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AppAudioController>());
builder.Services.AddSingleton<AudioController>();
builder.Services.AddSingleton<MediaController>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AudioController>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<MediaController>());
if (!args.Contains("--no-tray"))
    builder.Services.AddSingleton<IHostedService>(sp => new TrayService(config, state,
        sp.GetRequiredService<IHostApplicationLifetime>(), sp.GetRequiredService<ILogger<TrayService>>(),
        regenerate: () => replacement = AgentConfig.RegenerateIdentity(configPath), showPairing: showPairing, session: pairingSession,
        startup: new StartupSettings(FirstRun.Executable, configPath),
        certificate: tlsIdentity?.ExportCertificatePem(), configureFirewall: () => FirstRun.RequestFirewall(configPath), apps: apps));
var app = builder.Build();
// AgentConfig enforces the 32-hex-character contract. Compare the complete
// credential in constant time; never truncate or normalize a supplied token.
var expected = Encoding.ASCII.GetBytes("Bearer " + config.Token);
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers.CacheControl = "no-store";
    var remote = ctx.Connection.RemoteIpAddress?.MapToIPv4();
    var enrollmentPath = ctx.Request.Path == "/v1/identity" || ctx.Request.Path == "/v1/pair";
    var enrolling = config.TlsEnabled && allowedHub == "" && enrollmentPath && pairingSession.Remaining > TimeSpan.Zero
        && remote is not null && FirstRun.IsLocalPeer(remote, config.BindAddress);
    if (remote is null || (!IPAddress.IsLoopback(remote) && remote.ToString() != allowedHub && !enrolling))
    { ctx.Response.StatusCode = 403; return; }
    if (ctx.Request.Path == "/v1/identity" && HttpMethods.IsGet(ctx.Request.Method) && tlsIdentity is not null)
    {
        await next(ctx);
        return;
    }
    if (ctx.Request.Path == "/v1/pair" && HttpMethods.IsPost(ctx.Request.Method))
    {
        await next(ctx);
        return;
    }
    var supplied = Encoding.UTF8.GetBytes(ctx.Request.Headers.Authorization.ToString());
    if (!CryptographicOperations.FixedTimeEquals(expected, supplied))
    { ctx.Response.StatusCode = 401; return; }
    // A successful authenticated state read proves the hub received and uses its credentials.
    // Public certificate discovery and code submission alone are not completion.
    if (remote.ToString() == allowedHub && HttpMethods.IsGet(ctx.Request.Method)
        && (ctx.Request.Path == "/v1/state" || ctx.Request.Path == "/v1/events"))
        pairingSession.ConfirmAuthenticatedHub();
    try { await next(ctx); }
    catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested || app.Lifetime.ApplicationStopping.IsCancellationRequested) { }
});
app.MapGet("/v1/identity", () => tlsIdentity is null ? Results.NotFound() : Results.Ok(new { certificate = tlsIdentity.ExportCertificatePem() }));
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
        if (status == 200 && config.TlsEnabled)
        {
            lock (enrollmentGate)
            {
                var remote = ctx.Connection.RemoteIpAddress!.MapToIPv4().ToString();
                if (allowedHub != "" && allowedHub != remote) return Results.StatusCode(403);
                if (allowedHub == "")
                {
                    AgentConfig.ReplacePrivate(configPath, AgentConfig.Load(configPath) with { HubAddress = remote });
                    allowedHub = remote;
                }
            }
        }
        return status == 200 ? Results.Ok(new { deviceId = config.DeviceId, token = config.Token }) : Results.StatusCode(status);
    }
});
app.MapGet("/v1/state", () => state.Read());
app.MapGet("/v1/events", async (string? epoch, long? after, HttpContext ctx) =>
{
    using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted, app.Lifetime.ApplicationStopping);
    return await state.WaitAsync(epoch, after ?? -1, TimeSpan.FromSeconds(20), cancellation.Token);
});
app.MapPost("/v1/apps/command", async (HttpContext ctx, AppAudioController audio) =>
{
    JsonDocument body;
    try { body = await JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ctx.RequestAborted); }
    catch (JsonException) { return Results.BadRequest(); }
    using (body)
    {
        var root = body.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("key", out var key)
            || key.ValueKind != JsonValueKind.String || key.GetString() is not { Length: 64 } appKey
            || appKey.Any(c => !char.IsAsciiHexDigit(c)) || !root.TryGetProperty("command", out var cmd)
            || cmd.ValueKind != JsonValueKind.String || !root.TryGetProperty("value", out var value)) return Results.BadRequest();
        bool accepted;
        switch (cmd.GetString())
        {
            case "setVolume":
                if (!value.TryGetInt32Safe(out var volume) || volume is < 0 or > 100) return Results.BadRequest();
                accepted = audio.Set(appKey, volume: volume); break;
            case "adjustVolume":
                if (!value.TryGetInt32Safe(out var delta) || delta is < -100 or > 100) return Results.BadRequest();
                accepted = audio.Set(appKey, delta: delta); break;
            case "setMute":
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return Results.BadRequest();
                accepted = audio.Set(appKey, muted: value.GetBoolean()); break;
            default: return Results.BadRequest();
        }
        return accepted ? Results.Ok(new { accepted = true }) : Results.Conflict(new { accepted = false });
    }
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
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or InvalidDataException or InvalidOperationException)
{
    using var logs = new FileLog(Path.Combine(Path.GetDirectoryName(configPath)!, "logs"));
    logs.CreateLogger("Startup").LogError("Host could not start ({Type}). Check the selected PC address and TLS identity.", ex.GetType().Name);
    Environment.ExitCode = 1;
    if (!args.Contains("--no-tray")) MessageBox.Show(TrayContext.T("시작할 수 없습니다. 선택한 PC 주소와 인증서 파일을 확인하세요.\n", "Could not start. Check the selected PC address and certificate files.\n") + ex.Message,
        ProductInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
}
finally { singleInstance.Release(); }

internal static class JsonValueExtensions
{
    public static bool TryGetInt32Safe(this JsonElement value, out int number)
    {
        number = 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out number);
    }
}
