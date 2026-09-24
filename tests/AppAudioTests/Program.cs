using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using STMediaBridge;

if (args.Contains("--session"))
{
    using var player = new WasapiOut();
    player.Init(new SilenceProvider(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)));
    player.Play();
    Console.ReadLine();
    return;
}
using var devices = new MMDeviceEnumerator();
MMDevice endpoint;
try { endpoint = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); }
catch (Exception) { Console.WriteLine("SKIP live app audio: no render endpoint"); return; }
using var output = endpoint;
var root = Path.Combine(Path.GetTempPath(), "stwmc-live-app-" + Guid.NewGuid().ToString("N"));
var state = new StateStore("test");
var catalog = new AppCatalog(Path.Combine(root, "apps.json"), state);
using var controller = new AppAudioController(catalog, NullLogger<AppAudioController>.Instance);
var children = new List<Process>();
Process StartSession()
{
    var exe = Path.ChangeExtension(Assembly.GetExecutingAssembly().Location, ".exe");
    var process = Process.Start(new ProcessStartInfo(exe, "--session") { UseShellExecute=false, RedirectStandardInput=true, CreateNoWindow=true })!;
    children.Add(process); return process;
}
async Task Until(Func<bool> condition, string message)
{
    var watch = Stopwatch.StartNew();
    while (!condition() && watch.Elapsed < TimeSpan.FromSeconds(8)) await Task.Delay(50);
    if (!condition()) throw new Exception(message);
    Console.WriteLine("PASS " + message + " (" + watch.ElapsedMilliseconds + "ms)");
}
AudioSessionControl[] Controls(params int[] pids)
{
    output.AudioSessionManager.RefreshSessions();
    var list = new List<AudioSessionControl>();
    var sessions = output.AudioSessionManager.Sessions;
    for (var i=0; i<sessions.Count; i++)
    {
        var session = sessions[i];
        if (pids.Contains((int)session.GetProcessID)) list.Add(session); else session.Dispose();
    }
    return list.ToArray();
}
try
{
    await controller.StartAsync(default);
    var first = StartSession(); var second = StartSession();
    await Until(() => catalog.Read().Any(x => x.App.Name.Contains("AppAudioTests") && x.State.Active), "live session discovery callback");
    var row = catalog.Read().Single(x => x.App.Name.Contains("AppAudioTests"));
    var key = row.App.Key;
    await Until(() => { var c = Controls(first.Id, second.Id); var n=c.Length; foreach(var x in c)x.Dispose(); return n>=2; }, "two real Windows sessions for one executable");
    // Wait for both callbacks to be processed before issuing the grouped command.
    await Task.Delay(300);
    if (controller.Set(key, volume:25)) throw new Exception("unexposed app accepted a command");
    catalog.SetExposed(key,true);
    if (!controller.Set(key, volume:25)) throw new Exception("volume command rejected");
    await Until(() => { var c=Controls(first.Id,second.Id); var ok=c.Length>=2 && c.All(x=>Math.Abs(x.SimpleAudioVolume.Volume-.25)<.01); foreach(var x in c)x.Dispose(); return ok; }, "all grouped sessions receive 25 percent");
    var external = Controls(first.Id,second.Id);
    try
    {
        external[0].SimpleAudioVolume.Volume=.7f;
        await Until(() => state.Read().Apps.Single().Volume==70, "Windows volume callback updates shared snapshot");
        if (!controller.Set(key,muted:true) || external.Any(x=>!x.SimpleAudioVolume.Mute)) throw new Exception("mute did not reach all sessions");
        Console.WriteLine("PASS mute reaches every grouped session");
        external[0].SimpleAudioVolume.Mute=false;
        await Until(() => !state.Read().Apps.Single().Muted, "Windows unmute callback updates shared snapshot");
    }
    finally { foreach(var x in external)x.Dispose(); }
    first.StandardInput.WriteLine(); second.StandardInput.WriteLine();
    await first.WaitForExitAsync(); await second.WaitForExitAsync();
    await Until(() => !state.Read().Apps.Single().Active, "process exit keeps selected app but marks session absent");
    var again = StartSession();
    await Until(() => state.Read().Apps.Single().Active, "process restart reconnects same app key");
    if (state.Read().Apps.Single().Key != key) throw new Exception("app key changed after restart");
    catalog.SetExposed(key,false);
    if (state.Read().Apps.Length!=0 || controller.Set(key,volume:25)) throw new Exception("unselected app remains exposed");
    catalog.SetExposed(key,true);
    if (state.Read().Apps.Single().Key!=key) throw new Exception("reselection failed");
    Console.WriteLine("PASS deselection and reselection");
}
finally
{
    foreach(var process in children) { if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(); } process.Dispose(); }
    await controller.StopAsync(default);
    var catalogFile = Path.Combine(root, "apps.json");
    if (File.Exists(catalogFile)) File.Delete(catalogFile);
    if (Directory.Exists(root)) Directory.Delete(root);
}
