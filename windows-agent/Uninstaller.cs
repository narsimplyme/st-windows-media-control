using System.Diagnostics;

namespace STMediaBridge;

internal static class Uninstaller
{
    public static void Launch(string configPath)
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductInfo.DataDirectory, "agent.json");
        if (!string.Equals(Path.GetFullPath(configPath), expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Automatic uninstall supports the default installation only. Custom configuration files must be removed manually.");
        using var stream = typeof(Uninstaller).Assembly.GetManifestResourceStream("STMediaBridge.Uninstall")
            ?? throw new IOException("Uninstaller is missing.");
        var script = Path.Combine(Path.GetTempPath(), "STWMC-uninstall-" + Guid.NewGuid().ToString("N") + ".ps1");
        using (var output = File.Create(script)) stream.CopyTo(output);
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var arg in new[] { "-NoProfile", "-STA", "-ExecutionPolicy", "Bypass", "-File", script, "-DeleteSelf" })
            start.ArgumentList.Add(arg);
        try { using var process = Process.Start(start) ?? throw new IOException("Could not start uninstaller."); }
        catch { File.Delete(script); throw; }
    }
}
