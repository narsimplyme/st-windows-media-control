using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace STMediaBridge;

public sealed record AgentConfig(string DeviceId, string Token, string BindAddress = "127.0.0.1", int Port = 8765, string HubAddress = "", string? FirewallRuleId = null)
{
    public const int TokenBytes = 16;
    public const int TokenLength = TokenBytes * 2;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static AgentConfig Load(string path)
    {
        var config = JsonSerializer.Deserialize<AgentConfig>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException("Missing configuration");
        return Validate(config);
    }
    private static AgentConfig Validate(AgentConfig config)
    {
        if (!Guid.TryParseExact(config.DeviceId, "D", out var deviceId) || deviceId == Guid.Empty ||
            config.Token is null || config.Token.Length != TokenLength ||
            !config.Token.All(Uri.IsHexDigit) || config.Token.All(c => c == '0') || config.Port is < 1024 or > 65535 ||
            !IPAddress.TryParse(config.BindAddress, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.Broadcast))
            throw new InvalidDataException("Use a nonzero device UUID, nonzero 32-character hex token, explicit IPv4 address and port 1024-65535. Rotate legacy tokens with --rotate-token.");
        if (!IPAddress.IsLoopback(ip) && (!IPAddress.TryParse(config.HubAddress, out var hub) ||
            hub.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || hub.Equals(IPAddress.Any)))
            throw new InvalidDataException("A LAN listener requires the hub's IPv4 address.");
        return config;
    }
    public static void Create(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        JsonSerializer.Serialize(file, new AgentConfig(Guid.NewGuid().ToString(), NewToken()), Json);
    }
    // Replace atomically on the same volume, retaining the destination ACL.
    // The host restarts after this returns, dropping old authenticated requests.
    public static AgentConfig RegenerateIdentity(string path)
    {
        var previous = Load(path);
        var next = Validate(previous with { DeviceId = Guid.NewGuid().ToString(), Token = NewToken(),
            FirewallRuleId = previous.FirewallRuleId ?? previous.DeviceId });
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            // Restrict the staging file before writing any credentials.
            using (File.Create(temporary)) { }
            if (OperatingSystem.IsWindows())
                new FileInfo(temporary).SetAccessControl(new FileInfo(path).GetAccessControl());
            using (var stream = new FileStream(temporary, FileMode.Open, FileAccess.Write))
            {
                JsonSerializer.Serialize(stream, next, Json);
                stream.Flush(true);
            }
            File.Replace(temporary, path, null);
            return next;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static string NewToken()
    {
        string token;
        do { token = Convert.ToHexString(RandomNumberGenerator.GetBytes(TokenBytes)); }
        while (token.All(c => c == '0'));
        return token;
    }
    // Explicit migration/rotation: preserve the PC identity and network settings,
    // generate a fresh secret (never truncate), and retain the file's existing ACL.
    // Stop the running agent first; it retains its old token until restarted.
    public static void RotateToken(string path)
    {
        var previous = JsonSerializer.Deserialize<AgentConfig>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException("Missing configuration");
        var next = Validate(previous with { Token = NewToken() });
        File.WriteAllText(path, JsonSerializer.Serialize(next, Json));
    }
}
