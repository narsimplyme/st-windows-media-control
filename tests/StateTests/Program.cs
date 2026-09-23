using STMediaBridge;

var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine("PASS " + name);
    checks++;
}
var store = new StateStore("test");
var first = store.Read();
var pending = store.WaitAsync(first.Epoch, first.Revision, TimeSpan.FromSeconds(2), default);
Check(!pending.IsCompleted, "unchanged revision waits");
store.SetAudio(new(true, 67, false));
var update = await pending;
Check(update.Audio.Volume == 67 && update.Revision > first.Revision, "audio event wakes subscriber");
store.SetAudio(new(true, 67, false));
Check(store.Read().Revision == update.Revision, "identical samples do not create events");
Check((await store.WaitAsync("old-epoch", 99999, TimeSpan.FromSeconds(2), default)).Epoch == first.Epoch, "restart resets cursor");
Check((await store.WaitAsync(first.Epoch, 99999, TimeSpan.FromSeconds(2), default)).Revision == update.Revision, "future cursor resynchronizes");
using var cancel = new CancellationTokenSource();
var cancelled = store.WaitAsync(update.Epoch, update.Revision, TimeSpan.FromMinutes(1), cancel.Token);
cancel.Cancel();
try { await cancelled; throw new Exception("not cancelled"); }
catch (OperationCanceledException) { Check(true, "disconnected request cancels"); }
var heartbeat = await store.WaitAsync(update.Epoch, update.Revision, TimeSpan.FromMilliseconds(10), default);
Check(heartbeat == update, "heartbeat returns full unchanged state");
await Task.WhenAll(Task.Run(() => store.SetAudio(new(true, 12, true))), Task.Run(() => store.SetMedia(new(true, "playing", "Song"))));
Check(store.Read().Audio.Volume == 12 && store.Read().Media.Title == "Song", "concurrent observers preserve each other's fields");
for (var i = 0; i < 100; i++)
{
    var before = store.Read();
    var waiter = Task.Run(() => store.WaitAsync(before.Epoch, before.Revision, TimeSpan.FromSeconds(1), default));
    store.SetAudio(new(true, i, false));
    Check((await waiter).Revision > before.Revision, "subscribe/update race " + i);
}
var path = Path.Combine(Path.GetTempPath(), "st-mediabridge-test-" + Guid.NewGuid() + ".json");
try
{
    AgentConfig.Create(path);
    var c = AgentConfig.Load(path);
    var privateAcl = new FileInfo(path).GetAccessControl();
    Check(privateAcl.AreAccessRulesProtected, "new token file disables inherited permissions at creation");
    var allowedSids = new[] { System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value, "S-1-5-18" };
    Check(privateAcl.GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier))
        .Cast<System.Security.AccessControl.FileSystemAccessRule>().All(rule => allowedSids.Contains(rule.IdentityReference.Value)),
        "token file grants access only to current user and LocalSystem");
    Check(c.BindAddress == "127.0.0.1" && c.Token.Length == 32, "new config is loopback with random token");
    try { AgentConfig.Create(path); throw new Exception("overwritten"); }
    catch (IOException) { Check(true, "init cannot overwrite existing identity"); }
    File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(c with { BindAddress = "0.0.0.0" }, AgentConfig.Json));
    try { AgentConfig.Load(path); throw new Exception("wildcard accepted"); }
    catch (InvalidDataException) { Check(true, "wildcard listener rejected"); }
    void Reject(AgentConfig invalid, string name)
    {
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(invalid, AgentConfig.Json));
        try { AgentConfig.Load(path); throw new Exception("invalid config accepted: " + name); }
        catch (InvalidDataException) { Check(true, name); }
    }
    foreach (var invalid in new[] { "", new string('a',31), new string('a',33), new string('a',36), new string('a',64), new string('0',32), new string('g',32) })
        Reject(c with { Token = invalid }, "reject invalid/legacy/sentinel token length " + invalid.Length);
    Reject(c with { DeviceId = Guid.Empty.ToString() }, "reject sentinel UUID");
    Reject(c with { DeviceId = Guid.NewGuid().ToString("N") }, "require canonical UUID format");
    var legacy = c with { Token = new string('b', 64) };
    File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(legacy, AgentConfig.Json));
    AgentConfig.RotateToken(path);
    var migrated = AgentConfig.Load(path);
    Check(migrated.Token.Length == 32 && migrated.Token.All(Uri.IsHexDigit) && migrated.Token != legacy.Token[..32], "migration generates fresh secret, never truncates");
    Check(migrated with { Token = c.Token } == c, "migration preserves identity and network settings");
    AgentConfig.RotateToken(path);
    Check(AgentConfig.Load(path).Token != migrated.Token, "explicit rotation replaces current secret");
    var beforeReset = AgentConfig.Load(path);
    var aclBefore = new FileInfo(path).GetAccessControl().GetSecurityDescriptorSddlForm(System.Security.AccessControl.AccessControlSections.Access);
    var reset = AgentConfig.RegenerateIdentity(path);
    Check(reset == AgentConfig.Load(path), "regenerated configuration is saved before activation");
    Check(reset.DeviceId != beforeReset.DeviceId && reset.Token != beforeReset.Token, "regeneration replaces both identity and secret");
    Check(reset with { DeviceId = beforeReset.DeviceId, Token = beforeReset.Token, FirewallRuleId = beforeReset.FirewallRuleId } == beforeReset,
        "regeneration preserves network settings");
    Check(reset.FirewallRuleId == beforeReset.DeviceId, "existing firewall rule remains addressable after identity reset");
    Check(new FileInfo(path).GetAccessControl().GetSecurityDescriptorSddlForm(System.Security.AccessControl.AccessControlSections.Access) == aclBefore,
        "atomic replacement retains configuration access permissions");
    var again = AgentConfig.RegenerateIdentity(path);
    Check(again.DeviceId != reset.DeviceId && again.Token != reset.Token && again.FirewallRuleId == reset.FirewallRuleId,
        "repeated regeneration keeps firewall identity stable");

}
finally { File.Delete(path); }
Console.WriteLine($"{checks} checks passed");
var tlsDirectory = Path.Combine(Path.GetTempPath(), "st-wmc-tls-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tlsDirectory);
try
{
    var tlsConfig = Path.Combine(tlsDirectory, "agent.json");
    TlsIdentity.Initialize(tlsConfig, "192.168.1.20");
    Check(AgentConfig.Load(tlsConfig).BindAddress == "192.168.1.20" && AgentConfig.Load(tlsConfig).HubAddress == "", "first setup creates TLS configuration without manual hub ID");
    try { TlsIdentity.Initialize(tlsConfig, "192.168.1.21"); throw new Exception("Overwrote existing config"); }
    catch (IOException) { Check(true, "first setup never overwrites existing configuration"); }
    Check(TlsIdentity.Fingerprint("-----BEGIN CERTIFICATE-----\nYWJj\n-----END CERTIFICATE-----") == "35D95694 D3F16021 5DB293C7 899DAA59", "certificate fingerprint uses canonical base64 SHA256");
    Check(AgentConfig.Load(tlsConfig).TlsEnabled, "TLS mode persists in configuration");
    string thumbprint;
    using (var cert = TlsIdentity.Load(tlsConfig))
    {
        Check(cert.HasPrivateKey, "TLS identity contains private key");
        thumbprint = cert.Thumbprint;
    }
    TlsIdentity.Enable(tlsConfig);
    using (var cert = TlsIdentity.Load(tlsConfig))
        Check(cert.Thumbprint == thumbprint, "TLS provisioning never replaces existing trust");
    var keyFile = new FileInfo(Path.Combine(tlsDirectory, "server-tls.pfx"));
    Check(keyFile.GetAccessControl().AreAccessRulesProtected, "TLS private key has protected ACL from creation");
    AgentConfig.RegenerateIdentity(tlsConfig);
    Check(AgentConfig.Load(tlsConfig).TlsEnabled, "pairing reset preserves HTTPS requirement");
    using (var cert = TlsIdentity.Load(tlsConfig))
        Check(cert.Thumbprint == thumbprint, "pairing reset preserves PC certificate");
    keyFile.Delete();
    try { TlsIdentity.Enable(tlsConfig); throw new Exception("Missing TLS identity was silently regenerated"); }
    catch (InvalidDataException) { Check(true, "missing established TLS identity fails closed"); }
}
finally { Directory.Delete(tlsDirectory, true); }
var pairingClock = new PairingClock();
var shortPairing = new PairingSession(pairingClock);
Check(shortPairing.Exchange("1234567890") == 401, "no pairing accepted before user opens pairing window");
var firstCode = shortPairing.Generate();
Check(firstCode.Length == 10 && firstCode.All(char.IsAsciiDigit) && firstCode[0] != '0', "pairing code is exactly 10 easily typed digits");
Check(shortPairing.Exchange(firstCode) == 200, "short code exchanges credentials");
var secondCode = shortPairing.Generate();
Check(secondCode != firstCode && shortPairing.Exchange(firstCode) == 401, "new code invalidates previous code");
Check(shortPairing.Exchange(secondCode) == 200, "replacement code works");
shortPairing.Exchange("wrong");
Check(shortPairing.Exchange(secondCode) == 429, "pairing attempts are rate limited");
shortPairing.Generate();
Check(shortPairing.Exchange(secondCode) == 429, "generating a code cannot reset the attempt limit");
pairingClock.Advance(TimeSpan.FromMinutes(1));
var currentCode = shortPairing.Generate();
Check(shortPairing.Exchange(currentCode) == 200, "attempt budget recovers after one minute");
pairingClock.Advance(TimeSpan.FromMinutes(10));
Check(shortPairing.Exchange(currentCode) == 401 && shortPairing.Remaining <= TimeSpan.Zero, "pairing code expires after ten minutes");
Check(new PairingSession(pairingClock).Exchange(currentCode) == 401, "host restart invalidates outstanding code");
Console.WriteLine($"{checks} total checks including short pairing passed");

sealed class PairingClock : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan interval) => now += interval;
}
