namespace STMediaBridge;

public record AudioState(bool Available = false, int Volume = 0, bool Muted = false);
public record MediaState(bool Available = false, string Playback = "stopped",
    string Title = "", string Artist = "", string Source = "",
    bool CanPlay = false, bool CanPause = false, bool CanNext = false,
    bool CanPrevious = false, bool CanToggle = false, string Album = "");
public record Snapshot(string DeviceId, string Epoch, long Revision, AudioState Audio, MediaState Media);

// Each response is a complete immutable snapshot. The lock closes the gap between
// checking a revision and subscribing; no notification can be lost in that gap.
public sealed class StateStore(string deviceId)
{
    private readonly object gate = new();
    private Snapshot state = new(deviceId, Guid.NewGuid().ToString("N"), 1, new(), new());
    private TaskCompletionSource changed = NewSignal();
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Snapshot Read() { lock (gate) return state; }
    public void SetAudio(AudioState value) => Update(s => s with { Audio = value });
    public void SetMedia(MediaState value) => Update(s => s with { Media = value });
    private void Update(Func<Snapshot, Snapshot> update)
    {
        lock (gate)
        {
            var next = update(state);
            if (next == state) return;
            state = next with { Revision = state.Revision + 1 };
            var previous = changed;
            changed = NewSignal();
            previous.TrySetResult();
        }
    }
    public async Task<Snapshot> WaitAsync(string? epoch, long revision, TimeSpan timeout, CancellationToken ct)
    {
        Task signal;
        lock (gate)
        {
            if (epoch != state.Epoch || revision != state.Revision) return state;
            signal = changed.Task;
        }
        try { await signal.WaitAsync(timeout, ct); }
        catch (TimeoutException) { }
        return Read();
    }
}
