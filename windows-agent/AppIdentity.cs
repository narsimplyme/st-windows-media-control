using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Windows.ApplicationModel;

namespace STMediaBridge;

internal static class AppIdentity
{
    public static string FriendlyName(string appId, string fallback)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            try
            {
                var name = AppInfo.GetFromAppUserModelId(appId)?.DisplayInfo?.DisplayName;
                if (!string.IsNullOrWhiteSpace(name)) return Clip(name);
            }
            catch (Exception) { /* Not every desktop app is registered. */ }
        }
        return Clip(fallback);
    }
    public static SavedAudioApp? Resolve(uint pid)
    {
        if (pid == 0) return null;
        using var process = OpenProcess(0x1000, false, pid); // QUERY_LIMITED_INFORMATION
        if (process.IsInvalid) return null;
        var path = new StringBuilder(32768); var size = path.Capacity;
        if (!QueryFullProcessImageName(process, 0, path, ref size)) return null;
        var executable = path.ToString();
        var name = Path.GetFileNameWithoutExtension(executable);
        if (name.Equals("audiodg", StringComparison.OrdinalIgnoreCase) || name.Equals("svchost", StringComparison.OrdinalIgnoreCase)) return null;
        var idSize = 0;
        GetApplicationUserModelId(process, ref idSize, null);
        string? appId = null;
        if (idSize is > 0 and < 4096)
        {
            var id = new StringBuilder(idSize);
            if (GetApplicationUserModelId(process, ref idSize, id) == 0) appId = id.ToString();
        }
        var friendly = name.ToLowerInvariant() switch
        {
            "chrome" => "Google Chrome", "msedge" => "Microsoft Edge", "spotify" => "Spotify",
            "discord" => "Discord", "steam" => "Steam", "powershell" => "Windows PowerShell",
            "pwsh" => "PowerShell", "lostark" => "LOST ARK", _ => name
        };
        if (friendly == name)
        {
            try { var product = FileVersionInfo.GetVersionInfo(executable).ProductName; friendly = !string.IsNullOrWhiteSpace(product) ? product : name; }
            catch (Exception) { }
        }
        if (appId is not null) friendly = FriendlyName(appId, friendly);
        return new(AppCatalog.KeyFor(appId is null ? "exe:" + executable : "aumid:" + appId), Clip(friendly), executable);
    }
    private static string Clip(string value) => string.Concat(value.EnumerateRunes().Take(80));
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder path, ref int size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetApplicationUserModelId(SafeProcessHandle process, ref int length, StringBuilder? id);
}
