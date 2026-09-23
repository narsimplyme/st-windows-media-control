using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

// Optional LAN test serves only a fixed probe response, never production data.
var lan = args.Length == 4 && args[0] == "--lan";
var bind = lan ? IPAddress.Parse(args[1]) : IPAddress.Loopback;
var allowedHub = lan ? IPAddress.Parse(args[2]) : IPAddress.Loopback;
var output = lan ? Path.GetFullPath(args[3]) : null;
using var key = RSA.Create(2048);
var request = new CertificateRequest("CN=ST Windows Media Control TLS probe", key,
    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
request.CertificateExtensions.Add(new X509KeyUsageExtension(
    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyCertSign, true));
var names = new SubjectAlternativeNameBuilder();
names.AddIpAddress(IPAddress.Loopback);
if (lan) names.AddIpAddress(bind);
request.CertificateExtensions.Add(names.Build());
using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
using var certificate = new X509Certificate2(generated.Export(X509ContentType.Pfx), (string?)null, X509KeyStorageFlags.DefaultKeySet);
var builder = WebApplication.CreateSlimBuilder();
builder.Logging.ClearProviders();
builder.WebHost.ConfigureKestrel(options => options.Listen(bind, lan ? 8766 : 0,
    listener => listener.UseHttps(tls => {
        tls.ServerCertificate = certificate;
        tls.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
    })));
await using var app = builder.Build();
var requests = 0;
app.Use(async (context, next) => {
    var remote = context.Connection.RemoteIpAddress?.MapToIPv4();
    if (remote is null || (!IPAddress.IsLoopback(remote) && !remote.Equals(allowedHub)))
    { context.Response.StatusCode = 403; return; }
    await next(context);
});
app.MapGet("/probe", () => { Interlocked.Increment(ref requests); return "TLS probe"; });
await app.StartAsync();
if (lan)
{
    Directory.CreateDirectory(output!);
    File.WriteAllText(Path.Combine(output!, "trusted.pem"), certificate.ExportCertificatePem());
    using var wrongKey = RSA.Create(2048);
    var wrongRequest = new CertificateRequest("CN=Untrusted probe", wrongKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    wrongRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
    using var wrong = wrongRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
    File.WriteAllText(Path.Combine(output!, "untrusted.pem"), wrong.ExportCertificatePem());
    Console.WriteLine("LAN TLS probe ready on port 8766; public certificates exported.");
    using var expiry = new CancellationTokenSource(TimeSpan.FromMinutes(30));
    try { await Task.Delay(Timeout.Infinite, expiry.Token); } catch (OperationCanceledException) { }
    await app.StopAsync();
    return;
}
try
{
    var address = app.Urls.Single();
    using var trust = new HttpClientHandler();
    trust.ServerCertificateCustomValidationCallback = (_, peer, _, errors) => {
        if (peer is null || (errors & System.Net.Security.SslPolicyErrors.RemoteCertificateNameMismatch) != 0) return false;
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(certificate);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        return chain.Build(peer);
    };
    using var trusted = new HttpClient(trust);
    if (await trusted.GetStringAsync(address + "/probe") != "TLS probe") throw new Exception("Trusted TLS failed");
    using var untrusted = new HttpClient();
    try { await untrusted.GetStringAsync(address + "/probe"); throw new Exception("Untrusted certificate accepted"); }
    catch (HttpRequestException) { }
    using var plain = new HttpClient();
    try {
        using var response = await plain.GetAsync(address.Replace("https:", "http:") + "/probe");
        if (response.IsSuccessStatusCode) throw new Exception("Plain HTTP accepted");
    }
    catch (HttpRequestException) { }
    if (requests != 1) throw new Exception("Rejected request reached handler");
    Console.WriteLine("PASS: trusted TLS succeeds; unknown certificate and plaintext cannot reach the endpoint.");
}
finally { await app.StopAsync(); }

