using Windows.Media.Control;
using Windows.ApplicationModel;

namespace STMediaBridge;

public sealed class MediaController(StateStore state, ILogger<MediaController> log) : BackgroundService
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly SemaphoreSlim wake = new(0, 1);
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private GlobalSystemMediaTransportControlsSession? session;
    private bool failed;
    private string? sourceId;
    private string sourceName = "";
    private void Signal() { try { wake.Release(); } catch (SemaphoreFullException) { } }
    private void CurrentChanged(GlobalSystemMediaTransportControlsSessionManager _, CurrentSessionChangedEventArgs args) => Signal();
    private void SessionsChanged(GlobalSystemMediaTransportControlsSessionManager _, SessionsChangedEventArgs args) => Signal();
    private void PlaybackChanged(GlobalSystemMediaTransportControlsSession _, PlaybackInfoChangedEventArgs args) => Signal();
    private void PropertiesChanged(GlobalSystemMediaTransportControlsSession _, MediaPropertiesChangedEventArgs args) => Signal();
    private void Detach()
    {
        if (session is null) return;
        session.PlaybackInfoChanged -= PlaybackChanged;
        session.MediaPropertiesChanged -= PropertiesChanged;
        session = null;
        sourceId = null;
    }
    private async Task RefreshAsync(CancellationToken ct)
    {
        if (manager is null)
        {
            manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(ct).WaitAsync(TimeSpan.FromSeconds(3), ct);
            manager.CurrentSessionChanged += CurrentChanged;
            manager.SessionsChanged += SessionsChanged;
        }
        var current = manager.GetCurrentSession(); // Let Windows select; never pick an arbitrary player.
        var sessionChanged = !Equals(current, session);
        if (sessionChanged)
        {
            Detach();
            session = current;
            if (session is not null)
            {
                session.PlaybackInfoChanged += PlaybackChanged;
                session.MediaPropertiesChanged += PropertiesChanged;
            }
        }
        if (session is null) { state.SetMedia(new()); return; }
        var info = session.GetPlaybackInfo();
        var controls = info.Controls;
        var playback = info.PlaybackStatus switch
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => "playing",
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => "paused",
            _ => "stopped"
        };
        var source = GetSourceName(session.SourceAppUserModelId);
        var previous = state.Read().Media;
        var next = new MediaState(true, playback,
            !sessionChanged && previous.Source == source ? previous.Title : "", !sessionChanged && previous.Source == source ? previous.Artist : "", source,
            controls.IsPlayEnabled, controls.IsPauseEnabled, controls.IsNextEnabled,
            controls.IsPreviousEnabled, controls.IsPlayPauseToggleEnabled,
            !sessionChanged ? previous.Album : "");
        state.SetMedia(next); // Metadata failures must not delay playback or audio state.
        try
        {
            var properties = await session.TryGetMediaPropertiesAsync().AsTask(ct).WaitAsync(TimeSpan.FromSeconds(2), ct);
            var title = Clip(properties.Title);
            var artist = Clip(properties.Artist);
            if (!Equals(manager.GetCurrentSession(), session)) { Signal(); return; }
            state.SetMedia(next with { Title = title, Artist = artist, Album = Clip(properties.AlbumTitle) });
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            state.SetMedia(next with { Title = "", Artist = "", Album = "" });
        }
    }
    private static string Clip(string? text) => string.Concat((text ?? "").EnumerateRunes().Take(256));
    private string GetSourceName(string appId)
    {
        if (sourceId == appId) return sourceName;
        sourceId = appId;
        sourceName = Clip(appId);
        // Resolve once per session/source, not on every playback notification.
        // Older Windows and unregistered desktop apps retain their original ID.
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            try
            {
                var name = AppInfo.GetFromAppUserModelId(appId)?.DisplayInfo?.DisplayName;
                if (!string.IsNullOrWhiteSpace(name)) sourceName = Clip(name);
            }
            catch (Exception)
            {
                // Cosmetic lookup failure must not make media control unavailable.
            }
        }
        return sourceName;
    }
    public async Task<bool> CommandAsync(string command, CancellationToken ct)
    {
        if (!await gate.WaitAsync(TimeSpan.FromSeconds(4), ct)) return false;
        try
        {
            // Re-read Windows' current session at execution time, not a cached player.
            var target = manager?.GetCurrentSession();
            if (target is null) return false;
            var c = target.GetPlaybackInfo().Controls;
            var operation = command switch
            {
                "play" when c.IsPlayEnabled => target.TryPlayAsync(),
                "pause" when c.IsPauseEnabled => target.TryPauseAsync(),
                "next" when c.IsNextEnabled => target.TrySkipNextAsync(),
                "previous" when c.IsPreviousEnabled => target.TrySkipPreviousAsync(),
                "toggle" when c.IsPlayPauseToggleEnabled => target.TryTogglePlayPauseAsync(),
                _ => null
            };
            return operation is not null && await operation.AsTask(ct).WaitAsync(TimeSpan.FromSeconds(3), ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogWarning("Media command failed ({Type})", ex.GetType().Name);
            return false;
        }
        finally { gate.Release(); Signal(); }
    }
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await gate.WaitAsync(ct);
                try
                {
                    await RefreshAsync(ct);
                    if (failed) log.LogInformation("Media API recovered");
                    failed = false;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    state.SetMedia(new());
                    if (!failed) log.LogWarning("Media API unavailable ({Type}); will retry", ex.GetType().Name);
                    failed = true;
                    Reset();
                }
                finally { gate.Release(); }
                await wake.WaitAsync(TimeSpan.FromSeconds(20), ct);
                await Task.Delay(80, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally { Reset(); }
    }
    private void Reset()
    {
        try
        {
            Detach();
            if (manager is not null)
            {
                manager.CurrentSessionChanged -= CurrentChanged;
                manager.SessionsChanged -= SessionsChanged;
            }
        }
        catch (Exception) { /* Session can disappear during sign-out. */ }
        manager = null;
    }
}
