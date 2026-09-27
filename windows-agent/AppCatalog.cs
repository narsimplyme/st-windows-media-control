using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace STMediaBridge;

public record AppVolumeState(string Key, string Name, bool Active, int Volume, bool Muted, int Slot = 0);
public record SavedAudioApp(string Key, string Name, string ExecutablePath, bool Exposed = false, int Slot = 0);
public record AudioAppRow(SavedAudioApp App, AppVolumeState State);

// Only this local catalog controls exposure. Network commands cannot select apps.
public sealed class AppCatalog
{
    private readonly string path;
    private readonly StateStore state;
    public AppCatalog(string path, StateStore state)
    {
        this.path = path; this.state = state; saved = Load(path);
        if (File.Exists(path)) Save(saved); // Persist the one-time legacy selection migration.
        Publish(); // Never publish an empty selection while Core Audio initializes.
    }
    private readonly object gate = new();
    private Dictionary<string, SavedAudioApp> saved;
    private Dictionary<string, AppVolumeState> live = new();
    public static string KeyFor(string identity) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.ToUpperInvariant()))).ToLowerInvariant();
    private static Dictionary<string, SavedAudioApp> Load(string path)
    {
        if (!File.Exists(path)) return new();
        var entries = JsonSerializer.Deserialize<SavedAudioApp[]>(File.ReadAllText(path)) ?? throw new InvalidDataException("Invalid app catalog");
        if (entries.Any(x => x.Key.Length != 64 || x.Key.Any(c => !char.IsAsciiHexDigit(c)) || string.IsNullOrWhiteSpace(x.Name))
            || entries.Count(x => x.Exposed) > 64) throw new InvalidDataException("Invalid app catalog entries");
        var used = new HashSet<int>();
        foreach (var entry in entries.Where(x => x.Slot != 0))
            if (!entry.Exposed || entry.Slot is < 1 or > 5 || !used.Add(entry.Slot))
                throw new InvalidDataException("Invalid or duplicate app slot");
        // Legacy catalogs had selections but no slots. Keep their saved order once.
        return entries.Select(x =>
        {
            if (!x.Exposed || x.Slot != 0) return x;
            var slot = Enumerable.Range(1, 5).FirstOrDefault(i => !used.Contains(i));
            if (slot != 0) used.Add(slot);
            return x with { Slot = slot, Exposed = slot != 0 };
        }).ToDictionary(x => x.Key);
    }
    public AudioAppRow[] Read()
    {
        lock (gate) return saved.Values.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => new AudioAppRow(x, live.GetValueOrDefault(x.Key) ?? new(x.Key, x.Name, false, 0, false))).ToArray();
    }
    public bool IsExposed(string key) { lock (gate) return saved.TryGetValue(key, out var app) && app.Exposed; }
    // Keep mapping validation and the audio write atomic with local deselection.
    public bool WithSelection(string key, int? slot, Func<bool> action)
    {
        lock (gate) return saved.TryGetValue(key, out var app) && app.Exposed
            && (!slot.HasValue || app.Slot == slot.Value) && action();
    }
    public void SetExposed(string key, bool exposed)
    {
        lock (gate)
        {
            if (!saved.TryGetValue(key, out var app)) throw new InvalidOperationException("Unknown audio app");
            if (app.Exposed == exposed) return;
            if (exposed && saved.Values.Count(x => x.Exposed) >= 5) throw new InvalidOperationException("최대 5개 앱까지 선택할 수 있습니다. / Select up to 5 apps.");
            var next = new Dictionary<string, SavedAudioApp>(saved) { [key] = app with { Exposed = exposed, Slot = exposed ? Enumerable.Range(1, 5).First(i => !saved.Values.Any(x => x.Slot == i)) : 0 } };
            Save(next); saved = next; Publish();
        }
    }
    public void Observe(IEnumerable<SavedAudioApp> discovered, IEnumerable<AppVolumeState> current)
    {
        lock (gate)
        {
            var next = new Dictionary<string, SavedAudioApp>(saved);
            foreach (var app in discovered)
            {
                next[app.Key] = app with { Exposed = saved.GetValueOrDefault(app.Key)?.Exposed ?? false, Slot = saved.GetValueOrDefault(app.Key)?.Slot ?? 0 };
            }
            if (next.Count != saved.Count || next.Any(x => !saved.TryGetValue(x.Key, out var old) || old != x.Value))
            { Save(next); saved = next; }
            live = live.ToDictionary(x => x.Key, x => x.Value with { Active = false });
            foreach (var app in current) live[app.Key] = app;
            Publish();
        }
    }
    private void Publish() => state.SetApps(saved.Values.Where(x => x.Exposed).OrderBy(x => x.Slot)
        .Select(x => (live.GetValueOrDefault(x.Key) ?? new(x.Key, x.Name, false, 0, false)) with { Name = x.Name, Slot = x.Slot }).ToArray());
    private void Save(Dictionary<string, SavedAudioApp> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = AgentConfig.CreatePrivateFile(temp))
            { JsonSerializer.Serialize(file, entries.Values.ToArray()); file.Flush(true); }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
