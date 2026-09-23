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
        "regeneration preserves network and artwork settings");
    Check(reset.FirewallRuleId == beforeReset.DeviceId, "existing firewall rule remains addressable after identity reset");
    Check(new FileInfo(path).GetAccessControl().GetSecurityDescriptorSddlForm(System.Security.AccessControl.AccessControlSections.Access) == aclBefore,
        "atomic replacement retains configuration access permissions");
    var again = AgentConfig.RegenerateIdentity(path);
    Check(again.DeviceId != reset.DeviceId && again.Token != reset.Token && again.FirewallRuleId == reset.FirewallRuleId,
        "repeated regeneration keeps firewall identity stable");

}
finally { File.Delete(path); }
Console.WriteLine($"{checks} checks passed");
var artConfig = new AgentConfig(Guid.NewGuid().ToString(), AgentConfig.NewToken());
var artwork = new ArtworkStore(artConfig);
var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
var url = artwork.Set(png);
var key = url.Split('/')[^1];
Check(key == "cover.jpg" && !url.Contains(artConfig.Token), "fixed artwork URL contains no control credential");
Check(artwork.Get(key)?.Bytes.Take(3).SequenceEqual(new byte[] {255,216,255}) == true && artwork.Get(key)?.ContentType == "image/jpeg", "PNG converted to actual JPEG with matching MIME");
using (var decoded = System.Drawing.Image.FromStream(new MemoryStream(artwork.Get(key)!.Bytes)))
    Check(decoded.Width == 1 && decoded.Height == 1, "converted JPEG decodes successfully");
var jpeg = artwork.Get(key)!.Bytes;
Check(artwork.Set(jpeg) == url, "JPEG input uses same fixed URL");
Check(artwork.Set(new byte[] {255,216,255,0}) == "" && artwork.Get(key) is null, "corrupt raster clears current artwork");
artwork.Set(png);
Check(artwork.Set(png) == url, "identical artwork keeps URL stable");
Check(artwork.Get(new string('0', 32)) is null, "wrong artwork key rejected");
Check(artwork.Set(new byte[ArtworkStore.MaxBytes + 1]) == "" && artwork.Get(key) is null, "oversized art rejected and old image revoked");
Check(artwork.Set(System.Text.Encoding.UTF8.GetBytes("<svg/>")) == "", "active/non-raster image rejected");
url = artwork.Set(png);
artwork.Clear();
Check(artwork.Get(url.Split('/')[^1]) is null, "session clear revokes URL");
Check(new ArtworkStore(artConfig with { ArtworkEnabled = false }).Set(png) == "", "artwork can be disabled explicitly");
System.Net.IPAddress IP(string value) => System.Net.IPAddress.Parse(value);
Check(LanAccess.SameSubnet(IP("192.168.50.170"), IP("192.168.50.23"), IP("255.255.255.0")), "any same-LAN phone allowed without registration");
Check(!LanAccess.SameSubnet(IP("192.168.50.170"), IP("192.168.51.23"), IP("255.255.255.0")), "different private subnet rejected");
Check(LanAccess.SameSubnet(IP("10.1.2.3"), IP("10.1.3.4"), IP("255.255.254.0")), "actual subnet mask used instead of hard-coded /24");
Check(!LanAccess.SameSubnet(IP("192.168.50.170"), IP("8.8.8.8"), IP("255.255.255.0")), "internet source rejected");
Check(!LanAccess.SameSubnet(IP("192.168.50.170"), IP("192.168.50.23"), IP("0.0.0.0")), "unknown mask fails closed");
Check(!LanAccess.SameSubnet(IP("192.168.50.170"), IP("::1"), IP("255.255.255.0")), "IPv6 rejected on IPv4 policy");
Console.WriteLine($"{checks} total checks passed");

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
