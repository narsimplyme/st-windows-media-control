using System.Security.Cryptography;
using System.Drawing;
using System.Drawing.Imaging;

namespace STMediaBridge;

public sealed record Artwork(string Key, byte[] Bytes, string ContentType, string Hash);

// Only the current image is retained, served at a fixed LAN-only JPEG URL.
public sealed class ArtworkStore(AgentConfig config)
{
    public const int MaxBytes = 1024 * 1024;
    private readonly object gate = new();
    private Artwork? current;
    public bool Enabled => config.ArtworkEnabled;
    public void Clear() { lock (gate) current = null; }
    public Artwork? Get(string key)
    {
        lock (gate) return current?.Key == key ? current : null;
    }
    public string Set(byte[] bytes)
    {
        if (!Enabled || bytes.Length == 0 || bytes.Length > MaxBytes) { Clear(); return ""; }
        var type = bytes.AsSpan().StartsWith(new byte[] {137,80,78,71,13,10,26,10}) ? "image/png" :
            bytes.AsSpan().StartsWith(new byte[] {255,216,255}) ? "image/jpeg" : null;
        if (type is null) { Clear(); return ""; }
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        lock (gate)
        {
            if (current?.Hash != hash)
            {
                try
                {
                    using var input = new MemoryStream(bytes);
                    using var source = Image.FromStream(input, false, true);
                    if (source.Width > 8192 || source.Height > 8192) { current = null; return ""; }
                    var scale = Math.Min(1.0, 1024.0 / Math.Max(source.Width, source.Height));
                    using var bitmap = new Bitmap(Math.Max(1, (int)(source.Width * scale)), Math.Max(1, (int)(source.Height * scale)));
                    using (var graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.Clear(Color.Black);
                        graphics.DrawImage(source, 0, 0, bitmap.Width, bitmap.Height);
                    }
                    using var output = new MemoryStream();
                    bitmap.Save(output, ImageFormat.Jpeg);
                    if (output.Length > MaxBytes) { current = null; return ""; }
                    current = new("cover.jpg", output.ToArray(), "image/jpeg", hash);
                }
                catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException or System.Runtime.InteropServices.ExternalException)
                { current = null; return ""; }
            }
            return $"http://{config.BindAddress}:{config.Port}/v1/artwork/{current.Key}";
        }
    }
}
