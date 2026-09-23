using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace STMediaBridge;

// All COM reads, writes and endpoint replacements use one lock. Callbacks only
// enqueue a refresh: never unregister a Core Audio callback from inside itself.
public sealed class AudioController(StateStore state, ILogger<AudioController> log) : BackgroundService, IMMNotificationClient
{
    private readonly object gate = new();
    private readonly SemaphoreSlim wake = new(0, 1);
    private MMDeviceEnumerator? enumerator;
    private MMDevice? endpoint;
    private bool? available;
    private void Signal() { try { wake.Release(); } catch (SemaphoreFullException) { } }
    private void VolumeChanged(AudioVolumeNotificationData _) => Signal();

    private void RefreshLocked()
    {
        try
        {
            using var current = enumerator!.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (endpoint?.ID != current.ID)
            {
                ReleaseEndpoint();
                endpoint = enumerator.GetDevice(current.ID);
                endpoint.AudioEndpointVolume.OnVolumeNotification += VolumeChanged;
                log.LogInformation("Default multimedia output attached");
            }
            state.SetAudio(new(true, (int)Math.Round(endpoint!.AudioEndpointVolume.MasterVolumeLevelScalar * 100,
                MidpointRounding.AwayFromZero), endpoint.AudioEndpointVolume.Mute));
            if (available != true) log.LogInformation("Audio available");
            available = true;
        }
        catch (Exception ex)
        {
            ReleaseEndpoint();
            state.SetAudio(new());
            if (available != false) log.LogWarning("Audio unavailable ({Type}); will retry", ex.GetType().Name);
            available = false;
        }
    }
    public bool Set(int? volume = null, bool? muted = null, int? delta = null)
    {
        lock (gate)
        {
            RefreshLocked();
            if (endpoint is null) return false;
            try
            {
                var control = endpoint.AudioEndpointVolume;
                if (volume.HasValue) control.MasterVolumeLevelScalar = volume.Value / 100f;
                if (delta.HasValue) control.MasterVolumeLevelScalar = Math.Clamp(control.MasterVolumeLevelScalar + delta.Value / 100f, 0, 1);
                if (muted.HasValue) control.Mute = muted.Value;
                RefreshLocked();
                return true;
            }
            catch (Exception ex)
            {
                log.LogWarning("Audio command failed ({Type})", ex.GetType().Name);
                Signal();
                return false;
            }
        }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        enumerator = new MMDeviceEnumerator();
        enumerator.RegisterEndpointNotificationCallback(this);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                lock (gate) RefreshLocked();
                await wake.WaitAsync(TimeSpan.FromSeconds(20), stoppingToken);
                await Task.Delay(60, stoppingToken); // Coalesce headset-wheel bursts.
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            lock (gate)
            {
                enumerator.UnregisterEndpointNotificationCallback(this);
                ReleaseEndpoint();
                enumerator.Dispose();
                enumerator = null;
            }
        }
    }
    private void ReleaseEndpoint()
    {
        if (endpoint is null) return;
        try { endpoint.AudioEndpointVolume.OnVolumeNotification -= VolumeChanged; }
        catch (Exception) { /* An unplugged device may already be invalid. */ }
        endpoint.Dispose();
        endpoint = null;
    }
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string id) => Signal();
    public void OnDeviceStateChanged(string id, DeviceState deviceState) => Signal();
    public void OnDeviceAdded(string id) => Signal();
    public void OnDeviceRemoved(string id) => Signal();
    public void OnPropertyValueChanged(string id, PropertyKey key) { }
}
