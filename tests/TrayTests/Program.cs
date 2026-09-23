using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using STMediaBridge;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Isolated registry key: never modify the real Run key or installed task.
        var testKey = @"Software\STWindowsMediaControl.Tests\" + Guid.NewGuid().ToString("N");
        try
        {
            var executable = Path.GetFullPath("artifacts/Test App/Agent.exe");
            var configPath = Path.GetFullPath("artifacts/Test App/agent.json");
            var startup = new StartupSettings(executable, configPath, testKey, taskName: "", approvedKey: null);
            Check(!startup.Enabled, "startup defaults off without registration");
            startup.SetEnabled(true);
            Check(startup.Enabled, "startup can be enabled");
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(testKey))
                Check((string?)key!.GetValue("ST Windows Media Control") == $"\"{executable}\" --config \"{configPath}\"",
                    "startup quotes executable and config paths containing spaces");
            startup.SetEnabled(true);
            Check(startup.Enabled, "repeated enable remains one registration");
            startup.SetEnabled(false);
            Check(!startup.Enabled, "startup can be disabled");
            startup.SetEnabled(false);
            Check(!startup.Enabled, "repeated disable is harmless");
        }
        finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(testKey, false); }
        // Dummy credentials only. Never load installed configuration or touch clipboard.
        var config = new AgentConfig("12345678-1234-1234-1234-123456789abc", new string('a', 32), "192.168.50.170");
        var state = new StateStore(config.DeviceId);
        state.SetAudio(new(true, 25, false));
        using (var lifetime = new Lifetime())
        {
            var log = new TestLog();
            using var service = new TrayService(config, state, lifetime, log);
            service.StartAsync(default).GetAwaiter().GetResult();
            Check(!log.Ready.Task.IsCompleted, "tray waits for successful host start");
            lifetime.Start();
            log.Ready.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            service.StopAsync(deadline.Token).GetAwaiter().GetResult();
            Check(!log.Failed, "STA tray starts and shuts down cleanly");
        }
        using (var lifetime = new Lifetime())
        {
            var log = new TestLog();
            using var service = new TrayService(config, state, lifetime, log);
            service.StartAsync(default).GetAwaiter().GetResult();
            service.StopAsync(default).GetAwaiter().GetResult();
            lifetime.Start();
            Check(!log.Ready.Task.IsCompleted, "failed/stopped host never creates a tray");
        }
        // Restarting the web host creates another STA tray within the same process.
        using (var lifetime = new Lifetime())
        {
            var log = new TestLog();
            using var service = new TrayService(config, state, lifetime, log, () => config, showPairing: true);
            service.StartAsync(default).GetAwaiter().GetResult();
            lifetime.Start();
            log.Ready.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            service.StopAsync(deadline.Token).GetAwaiter().GetResult();
            Check(!log.Failed, "tray can restart in-process and open replacement pairing information");
        }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        var pairingSession = new PairingSession();
        using var form = new PairingForm(config, state, SystemIcons.Application, pairingSession,
            "-----BEGIN CERTIFICATE-----\nYWJj\n-----END CERTIFICATE-----");
        var controls = Descendants(form).ToArray();
        var fields = controls.OfType<TextBox>().ToArray();
        Check(fields.Length == 3 && fields.All(f => f.ReadOnly), "only address, port and short code are shown");
        var code = fields.Single(f => f.Text.Length == 8);
        Check(code.Text.All(char.IsAsciiDigit) && !code.UseSystemPasswordChar, "eight-digit code is readable for manual entry");
        Check(!fields.Any(f => f.Text == config.Token || f.Text == config.DeviceId), "internal UUID and token are not user-facing");
        Check(pairingSession.Exchange(code.Text) == 200, "displayed code is active for pairing");
        Check(controls.OfType<Button>().Count() == 5, "three copy buttons, renew code and close");
        // Materialize child windows off-screen before DrawToBitmap; a never-shown
        // Form otherwise produces a blank preview even though its controls exist.
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-30000, -30000);
        form.Show();
        Application.DoEvents();
        var oldCode = code.Text;
        controls.OfType<Button>().Single(b => b.Text is "New code" or "새 코드 만들기").PerformClick();
        Check(code.Text != oldCode && pairingSession.Exchange(oldCode) == 401 && pairingSession.Exchange(code.Text) == 200,
            "new-code button updates displayed code and revokes old code");
        Check(!pairingSession.IsPaired, "code exchange alone does not report completed pairing");
        pairingSession.ConfirmAuthenticatedHub();
        var uiDeadline = DateTime.UtcNow.AddSeconds(3);
        while (!controls.OfType<Label>().Any(l => l.Text.Contains("SmartThings authenticated") || l.Text.Contains("SmartThings 인증 연결")) && DateTime.UtcNow < uiDeadline)
        { Application.DoEvents(); Thread.Sleep(20); }
        Check(controls.OfType<Label>().Any(l => l.Text.Contains("SmartThings authenticated") || l.Text.Contains("SmartThings 인증 연결")),
            "authenticated hub updates pairing completion without reopening window");
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        form.Hide();
        var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/tray-pairing-preview.png");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        bitmap.Save(output);
        Check(fields.All(f => f.Width >= 250), "pairing values have readable width");
        Console.WriteLine("Preview: " + output);
        form.Show();
        NativeShowWindow(form.Handle, 0);
        Check(!NativeIsWindowVisible(form.Handle), "reproduce natively hidden pairing window");
        TrayContext.Present(form);
        Check(NativeIsWindowVisible(form.Handle), "tray presentation restores a natively hidden window");
        form.WindowState = FormWindowState.Minimized;
        TrayContext.Present(form);
        Check(form.WindowState == FormWindowState.Normal && NativeIsWindowVisible(form.Handle), "tray presentation restores a minimized window");
        form.Location = new Point(-30000, -30000);
        TrayContext.Present(form);
        Check(Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(form.Bounds)), "tray presentation brings off-screen window back");
        form.Hide();
        var instructions = controls.OfType<Label>().Single(l => l.Text.Contains("35D95694"));
        Check(instructions.Text.Contains("D3F16021") && instructions.Text.Contains("5DB293C7") && instructions.Text.Contains("899DAA59"), "pairing shows all 128 fingerprint bits for comparison");
        Check(instructions.Bottom <= form.ClientSize.Height, "certificate comparison instructions fit inside dialog");
        using (var licenseWindow = new LicenseForm(File.ReadAllText("THIRD_PARTY_NOTICES.md")))
        {
            licenseWindow.ShowInTaskbar = false;
            licenseWindow.Show();
            Application.DoEvents();
            using var licensePreview = new Bitmap(licenseWindow.Width, licenseWindow.Height);
            licenseWindow.DrawToBitmap(licensePreview, new Rectangle(Point.Empty, licensePreview.Size));
            licensePreview.Save(Path.Combine(Path.GetDirectoryName(output)!, "licenses-preview.png"));
            licenseWindow.Hide();
        }
        string? selected = null;
        var setup = FirstRun.Configure("unused-test-config", new[] { "192.168.1.20", "192.168.1.30" },
            (ip, enabled) => { selected = ip; Check(!enabled, "first-run autostart is an explicit opt-in"); },
            dialog => {
                dialog.ShowInTaskbar = false;
                var content = Descendants(dialog).ToArray();
                Check(content.OfType<ComboBox>().Single().SelectedItem?.ToString() == "192.168.1.20", "first run preselects detected PC address");
                using var preview = new Bitmap(dialog.Width, dialog.Height);
                dialog.DrawToBitmap(preview, new Rectangle(Point.Empty, preview.Size));
                preview.Save(Path.Combine(Path.GetDirectoryName(output)!, "first-run-preview.png"));
                content.OfType<Button>().Single().PerformClick();
            });
        Check(setup && selected == "192.168.1.20", "first-run setup completes without manual config editing");
    }
    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    private static void Check(bool value, string description)
    {
        if (!value) throw new Exception(description);
        Console.WriteLine("PASS " + description);
    }
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint="ShowWindow")]
    private static extern bool NativeShowWindow(IntPtr handle, int command);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint="IsWindowVisible")]
    private static extern bool NativeIsWindowVisible(IntPtr handle);
    private sealed class Lifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource started = new(), stopping = new(), stopped = new();
        public CancellationToken ApplicationStarted => started.Token;
        public CancellationToken ApplicationStopping => stopping.Token;
        public CancellationToken ApplicationStopped => stopped.Token;
        public void Start() => started.Cancel();
        public void StopApplication() => stopping.Cancel();
        public void Dispose() { started.Dispose(); stopping.Dispose(); stopped.Dispose(); }
    }
    private sealed class TestLog : ILogger<TrayService>
    {
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Failed { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter)
        {
            if (level >= LogLevel.Error) { Failed = true; Ready.TrySetException(new Exception(formatter(state, error))); }
            else if (formatter(state, error) == "Tray icon ready") Ready.TrySetResult();
        }
    }
}
