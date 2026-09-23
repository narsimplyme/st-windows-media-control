using System.Net;
using System.Text;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace STMediaBridge;

internal static class TlsIdentity
{
    private static string KeyPath(string configPath) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath))!, "server-tls.pfx");

    // Explicit provisioning only. Never silently replace an established identity.
    public static void Enable(string configPath)
    {
        var config = AgentConfig.Load(configPath);
        Provision(configPath, config, !config.TlsEnabled);
        AgentConfig.ReplacePrivate(configPath, config with { TlsEnabled = true });
    }

    public static void Initialize(string configPath, string bindAddress)
    {
        if (File.Exists(configPath)) throw new IOException("Configuration already exists.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(configPath))!);
        var config = new AgentConfig(Guid.NewGuid().ToString(), AgentConfig.NewToken(), bindAddress, TlsEnabled: true);
        Provision(configPath, config, true);
        AgentConfig.Create(configPath, config);
    }

    public static string Fingerprint(string pem)
    {
        var body = pem.Replace("-----BEGIN CERTIFICATE-----", "").Replace("-----END CERTIFICATE-----", "");
        body = string.Concat(body.Where(c => !char.IsWhiteSpace(c)));
        var hex = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(body)))[..32];
        return string.Join(" ", Enumerable.Range(0, 4).Select(i => hex.Substring(i * 8, 8)));
    }

    private static void Provision(string configPath, AgentConfig config, bool allowCreate)
    {
        var path = KeyPath(configPath);
        if (!File.Exists(path))
        {
            if (!allowCreate) throw new InvalidDataException("TLS identity is missing; restore it before starting.");
            using var key = RSA.Create(3072);
            var request = new CertificateRequest("CN=ST Windows Media Control " + config.DeviceId,
                key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyCertSign, true));
            var names = new SubjectAlternativeNameBuilder();
            names.AddIpAddress(IPAddress.Parse(config.BindAddress));
            names.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(names.Build());
            using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(2));
            var bytes = cert.Export(X509ContentType.Pfx);
            try
            {
                using var file = AgentConfig.CreatePrivateFile(path);
                file.Write(bytes);
                file.Flush(true);
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        using var identity = Load(configPath);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!, "server-tls.pem"), identity.ExportCertificatePem());
    }

    public static X509Certificate2 Load(string configPath)
    {
        var certificate = new X509Certificate2(KeyPath(configPath), (string?)null, X509KeyStorageFlags.DefaultKeySet);
        if (!certificate.HasPrivateKey || certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow || certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow)
        { certificate.Dispose(); throw new InvalidDataException("TLS identity is invalid or expired."); }
        return certificate;
    }
}
