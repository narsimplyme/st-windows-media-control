using System.Net;
using System.Security.Authentication;
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
        var path = KeyPath(configPath);
        if (!File.Exists(path))
        {
            if (config.TlsEnabled) throw new InvalidDataException("TLS identity is missing; restore it before starting.");
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
        AgentConfig.ReplacePrivate(configPath, config with { TlsEnabled = true });
    }

    public static X509Certificate2 Load(string configPath)
    {
        var certificate = new X509Certificate2(KeyPath(configPath), (string?)null, X509KeyStorageFlags.DefaultKeySet);
        if (!certificate.HasPrivateKey || certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow || certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow)
        { certificate.Dispose(); throw new InvalidDataException("TLS identity is invalid or expired."); }
        return certificate;
    }
}
