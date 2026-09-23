using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace STMediaBridge;

public interface IStartupSettings
{
    bool Enabled { get; }
    void SetEnabled(bool enabled);
}

public sealed class StartupSettings : IStartupSettings
{
    private const string Entry = "ST Windows Media Control";
    private readonly string command;
    private readonly string runKey;
    private readonly string taskName;
    private readonly string? approvedKey;
    public StartupSettings(string executable, string configPath,
        string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run", string? taskName = null,
        string? approvedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run")
    {
        executable = Path.GetFullPath(executable);
        configPath = Path.GetFullPath(configPath);
        if (executable.Contains('"') || configPath.Contains('"')) throw new ArgumentException("Invalid path");
        command = $"\"{executable}\" --config \"{configPath}\"";
        this.runKey = runKey;
        this.taskName = taskName ?? "ST MediaBridge-" + WindowsIdentity.GetCurrent().User!.Value;
        this.approvedKey = approvedKey;
    }
    public bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(runKey);
            using var approved = approvedKey is null ? null : Registry.CurrentUser.OpenSubKey(approvedKey);
            var disabled = approved?.GetValue(Entry) is byte[] bytes && bytes.Length > 0 && (bytes[0] == 3 || bytes[0] == 7);
            var registered = key?.GetValue(Entry) is string value && !string.IsNullOrWhiteSpace(value) && !disabled;
            return registered || WithLegacyTask(task => (bool)task.Enabled);
        }
    }
    public void SetEnabled(bool enabled)
    {
        using var beforeKey = Registry.CurrentUser.OpenSubKey(runKey);
        var previousCommand = beforeKey?.GetValue(Entry) as string;
        using var beforeApproved = approvedKey is null ? null : Registry.CurrentUser.OpenSubKey(approvedKey);
        var previousApproval = beforeApproved?.GetValue(Entry) as byte[];
        // Disable the old installer task before registering Run, without stopping
        // the running companion. Failure leaves the old startup method intact.
        bool wasEnabled = false;
        WithLegacyTask(task => { wasEnabled = task.Enabled; task.Enabled = false; return true; });
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(runKey);
            if (enabled)
            {
                key.SetValue(Entry, command, RegistryValueKind.String);
                if (approvedKey is not null)
                {
                    using var approved = Registry.CurrentUser.OpenSubKey(approvedKey, writable: true);
                    approved?.DeleteValue(Entry, throwOnMissingValue: false);
                }
            }
            else key.DeleteValue(Entry, throwOnMissingValue: false);
        }
        catch
        {
            using (var key = Registry.CurrentUser.CreateSubKey(runKey))
            {
                if (previousCommand is null) key.DeleteValue(Entry, false);
                else key.SetValue(Entry, previousCommand, RegistryValueKind.String);
            }
            if (approvedKey is not null && previousApproval is not null)
            {
                using var approved = Registry.CurrentUser.CreateSubKey(approvedKey);
                approved.SetValue(Entry, previousApproval, RegistryValueKind.Binary);
            }
            if (wasEnabled) WithLegacyTask(task => { task.Enabled = true; return true; });
            throw;
        }
    }
    private bool WithLegacyTask(Func<dynamic, bool> action)
    {
        if (taskName.Length == 0) return false;
        object? service = null, folder = null, task = null;
        try
        {
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!);
            ((dynamic)service!).Connect();
            folder = ((dynamic)service!).GetFolder(@"\");
            try { task = ((dynamic)folder).GetTask(taskName); }
            catch (COMException ex) when (ex.HResult == unchecked((int)0x80070002)) { return false; }
            return action(task!);
        }
        finally
        {
            foreach (var value in new[] { task, folder, service })
                if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
        }
    }
}
