using System.Collections.Concurrent;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace STMediaBridge;

// COM callbacks enqueue work only. Collection changes/disposal occur on the
// background worker, never inside a Core Audio callback.
public sealed class AppAudioController(AppCatalog catalog, ILogger<AppAudioController> log) : BackgroundService, IMMNotificationClient
{
    private readonly object gate = new();
    private readonly SemaphoreSlim wake = new(0, 1);
    private readonly Dictionary<string, MMDevice> outputs = new();
    private readonly Dictionary<string, Session> sessions = new();
    private readonly ConcurrentQueue<(string Id, int Volume, bool Muted)> changes = new();
    private readonly Dictionary<string, (int Volume, bool Muted)> last = new();
    private MMDeviceEnumerator? enumerator;
    private int rescan = 1;
    private void Signal() { try { wake.Release(); } catch (SemaphoreFullException) { } }
    private void ScanSoon() { Interlocked.Exchange(ref rescan, 1); Signal(); }
    private void Created(object _, IAudioSessionControl session) => ScanSoon();
    private sealed class Session(AppAudioController owner, string id, SavedAudioApp app, AudioSessionControl control) : IAudioSessionEventsHandler
    {
        public SavedAudioApp App { get; } = app;
        public AudioSessionControl Control { get; } = control;
        public void OnVolumeChanged(float volume, bool isMuted)
        { owner.changes.Enqueue((id, (int)Math.Round(volume * 100), isMuted)); owner.Signal(); }
        public void OnDisplayNameChanged(string _) => owner.ScanSoon();
        public void OnIconPathChanged(string _) { }
        public void OnChannelVolumeChanged(uint count, IntPtr values, uint index) { }
        public void OnGroupingParamChanged(ref Guid _) { }
        public void OnStateChanged(AudioSessionState _) => owner.ScanSoon();
        public void OnSessionDisconnected(AudioSessionDisconnectReason _) => owner.ScanSoon();
    }
    private static void DisposeSession(Session session)
    { try { session.Control.Dispose(); } catch (Exception) { } }
    private void Scan()
    {
        var found = new HashSet<string>();
        var devices = enumerator!.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        var ids = new HashSet<string>();
        foreach (var device in devices)
        {
            var id = device.ID; ids.Add(id);
            if (outputs.ContainsKey(id)) device.Dispose();
            else { outputs[id] = device; device.AudioSessionManager.OnSessionCreated += Created; }
            try
            {
                var manager = outputs[id].AudioSessionManager;
                manager.RefreshSessions();
                var collection = manager.Sessions;
                for (var i = 0; i < collection.Count; i++)
                {
                    var control = collection[i];
                    var keep = false;
                    try
                    {
                        if (control.IsSystemSoundsSession || control.State == AudioSessionState.AudioSessionStateExpired) continue;
                        var instance = id + "|" + control.GetSessionInstanceIdentifier;
                        found.Add(instance);
                        if (sessions.ContainsKey(instance)) continue;
                        var app = AppIdentity.Resolve(control.GetProcessID);
                        if (app is null) continue;
                        var session = new Session(this, instance, app, control);
                        control.RegisterEventClient(session);
                        sessions[instance] = session; keep = true;
                    }
                    catch (Exception) { /* Session/process can disappear during enumeration. */ }
                    finally { if (!keep) control.Dispose(); }
                }
            }
            catch (Exception ex) { log.LogDebug("App session enumeration failed ({Type})", ex.GetType().Name); }
        }
        foreach (var id in sessions.Keys.Where(x => !found.Contains(x)).ToArray())
        { DisposeSession(sessions[id]); sessions.Remove(id); }
        foreach (var id in outputs.Keys.Where(x => !ids.Contains(x)).ToArray())
        {
            outputs[id].AudioSessionManager.OnSessionCreated -= Created;
            outputs[id].Dispose(); outputs.Remove(id);
        }
    }
    private void Publish()
    {
        while (changes.TryDequeue(out var update))
            if (sessions.TryGetValue(update.Id, out var session)) last[session.App.Key] = (update.Volume, update.Muted);
        var apps = new Dictionary<string, SavedAudioApp>();
        var values = new Dictionary<string, AppVolumeState>();
        foreach (var session in sessions.OrderBy(x => x.Key).Select(x => x.Value))
        {
            try
            {
                if (session.Control.State == AudioSessionState.AudioSessionStateExpired) continue;
                var app = session.App; apps[app.Key] = app;
                if (values.ContainsKey(app.Key)) continue;
                var actual = session.Control.SimpleAudioVolume;
                var value = last.GetValueOrDefault(app.Key, ((int)Math.Round(actual.Volume * 100), actual.Mute));
                values[app.Key] = new(app.Key, app.Name, true, value.Item1, value.Item2);
            }
            catch (Exception) { ScanSoon(); }
        }
        foreach (var key in last.Keys.Where(x => !values.ContainsKey(x)).ToArray()) last.Remove(key);
        catalog.Observe(apps.Values, values.Values);
    }
    public bool Set(string key, int? volume = null, bool? muted = null, int? delta = null)
    {
        lock (gate)
        {
            if (!catalog.IsExposed(key)) return false;
            var targets = sessions.Values.Where(x => x.App.Key == key).ToArray();
            var applied = 0;
            var current = catalog.Read().FirstOrDefault(x => x.App.Key == key)?.State.Volume ?? 0;
            var targetVolume = delta.HasValue ? Math.Clamp(current + delta.Value, 0, 100) : volume;
            foreach (var session in targets)
            {
                try
                {
                    if (session.Control.State == AudioSessionState.AudioSessionStateExpired) continue;
                    if (targetVolume.HasValue) session.Control.SimpleAudioVolume.Volume = targetVolume.Value / 100f;
                    if (muted.HasValue) session.Control.SimpleAudioVolume.Mute = muted.Value;
                    var v = session.Control.SimpleAudioVolume;
                    last[key] = ((int)Math.Round(v.Volume * 100), v.Mute); applied++;
                }
                catch (Exception) { ScanSoon(); }
            }
            Publish();
            return applied > 0;
        }
    }
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        enumerator = new MMDeviceEnumerator();
        enumerator.RegisterEndpointNotificationCallback(this);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                lock (gate)
                {
                    try { if (Interlocked.Exchange(ref rescan, 0) != 0) Scan(); Publish(); }
                    catch (Exception ex) { log.LogWarning("App volume refresh failed ({Type})", ex.GetType().Name); Interlocked.Exchange(ref rescan, 1); }
                }
                if (!await wake.WaitAsync(TimeSpan.FromSeconds(20), ct)) Interlocked.Exchange(ref rescan, 1);
                await Task.Delay(40, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            lock (gate)
            {
                enumerator.UnregisterEndpointNotificationCallback(this);
                foreach (var session in sessions.Values) DisposeSession(session);
                sessions.Clear();
                foreach (var device in outputs.Values) { device.AudioSessionManager.OnSessionCreated -= Created; device.Dispose(); }
                outputs.Clear(); enumerator.Dispose(); enumerator = null;
            }
        }
    }
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string id) => ScanSoon();
    public void OnDeviceStateChanged(string id, DeviceState state) => ScanSoon();
    public void OnDeviceAdded(string id) => ScanSoon();
    public void OnDeviceRemoved(string id) => ScanSoon();
    public void OnPropertyValueChanged(string id, PropertyKey key) { }
}
