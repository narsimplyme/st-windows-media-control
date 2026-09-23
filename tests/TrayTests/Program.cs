using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using STMediaBridge;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
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
        using var form = new PairingForm(config, state, SystemIcons.Application, pairingSession);
        var controls = Descendants(form).ToArray();
        var fields = controls.OfType<TextBox>().ToArray();
        Check(fields.Length == 3 && fields.All(f => f.ReadOnly), "only address, port and short code are shown");
        var code = fields.Single(f => f.Text.Length == 10);
        Check(code.Text.All(char.IsAsciiDigit) && !code.UseSystemPasswordChar, "ten-digit code is readable for manual entry");
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
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        form.Hide();
        var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/tray-pairing-preview.png");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        bitmap.Save(output);
        Check(fields.All(f => f.Width >= 250), "pairing values have readable width");
        Console.WriteLine("Preview: " + output);
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
