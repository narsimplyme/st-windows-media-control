using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace STMediaBridge;

// WinForms owns a separate STA thread; native media observers and HTTP remain
// on the host's background threads. The icon appears only after the server starts.
public sealed class TrayService(AgentConfig config, StateStore state,
    IHostApplicationLifetime lifetime, ILogger<TrayService> log, Func<AgentConfig>? regenerate = null, bool showPairing = false, PairingSession? session = null, IStartupSettings? startup = null) : IHostedService, IDisposable
{
    private static int uiInitialized;
    private readonly object gate = new();
    private readonly CancellationTokenSource stopping = new();
    private readonly TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenRegistration started;
    private Thread? thread;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        started = lifetime.ApplicationStarted.Register(() =>
        {
            lock (gate)
            {
                if (stopping.IsCancellationRequested) return;
                thread = new Thread(Run) { IsBackground = true, Name = ProductInfo.DisplayName + " tray" };
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
            }
        });
        return Task.CompletedTask;
    }
    private void Run()
    {
        try
        {
            if (Interlocked.Exchange(ref uiInitialized, 1) == 0)
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
            }
            using var context = new TrayContext(config, state, lifetime, regenerate, showPairing, session, startup);
            log.LogInformation("Tray icon ready");
            using var registration = stopping.Token.Register(context.RequestClose);
            if (!stopping.IsCancellationRequested) Application.Run(context);
        }
        catch (Exception ex)
        {
            log.LogError("Tray UI failed ({Type}); the companion is still running", ex.GetType().Name);
        }
        finally { finished.TrySetResult(); }
    }
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            stopping.Cancel();
            if (thread is null) finished.TrySetResult();
        }
        await finished.Task.WaitAsync(cancellationToken);
    }
    public void Dispose() { started.Dispose(); stopping.Dispose(); }
}

internal sealed class TrayContext : ApplicationContext
{
    private readonly Control dispatcher = new();
    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip menu = new();
    private readonly Icon icon;
    private PairingForm? pairing;
    internal static string T(string korean, string english) =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko" ? korean : english;

    public TrayContext(AgentConfig config, StateStore state, IHostApplicationLifetime lifetime, Func<AgentConfig>? regenerate = null, bool showPairing = false, PairingSession? session = null, IStartupSettings? startup = null)
    {
        session ??= new PairingSession();
        _ = dispatcher.Handle; // Created on STA before cancellation can post to it.
        icon = CreateIcon();
        var status = new ToolStripMenuItem(T("실행 중", "Running")) { Enabled = false };
        menu.Items.Add(status);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(T("페어링 정보…", "Pairing information…"), null, (_, _) => ShowPairing(config, state, session));
        if (regenerate is not null)
        {
            var reset = menu.Items.Add(T("페어링 초기화…", "Reset pairing…"));
            reset.Click += (_, _) =>
            {
                if (MessageBox.Show(T("페어링을 초기화할까요?\n기존 연결은 끊어집니다. 새 10자리 코드를 SmartThings 설정에 입력하면 다시 연결됩니다.",
                    "Reset pairing?\nThe existing connection will be disconnected. Enter the new 10-digit code in SmartThings to reconnect."),
                    ProductInfo.DisplayName, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
                reset.Enabled = false;
                try
                {
                    regenerate();
                    lifetime.StopApplication();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException or System.Security.SecurityException)
                {
                    reset.Enabled = true;
                    MessageBox.Show(T("재생성하지 못했습니다. 설정 파일 권한을 확인하세요. 기존 연결은 유지됩니다.",
                        "Could not regenerate. Check configuration file permissions. The existing connection remains active."),
                        ProductInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
        }
        if (startup is not null)
        {
            var autoStart = new ToolStripMenuItem(T("Windows 로그인 시 자동 실행", "Start automatically at Windows sign-in"));
            menu.Items.Add(autoStart);
            void RefreshStartup()
            {
                try { autoStart.Checked = startup.Enabled; autoStart.Enabled = true; autoStart.ToolTipText = ""; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or System.Security.SecurityException)
                {
                    autoStart.Enabled = false;
                    autoStart.ToolTipText = T("자동 실행 설정을 읽을 수 없습니다.", "Unable to read startup settings.");
                }
            }
            menu.Opening += (_, _) => RefreshStartup();
            autoStart.Click += (_, _) =>
            {
                try { startup.SetEnabled(!startup.Enabled); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or System.Security.SecurityException)
                {
                    MessageBox.Show(T("자동 실행 설정을 변경하지 못했습니다. 시작프로그램 또는 예약 작업의 접근 권한을 확인하세요.",
                        "Unable to change startup settings. Check access to startup settings or the scheduled task."),
                        ProductInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                RefreshStartup();
            };
        }
        menu.Items.Add(new ToolStripSeparator());
        var exit = menu.Items.Add(T(ProductInfo.DisplayName + " 종료", "Exit " + ProductInfo.DisplayName));
        exit.Click += (_, _) =>
        {
            exit.Enabled = false;
            lifetime.StopApplication(); // Gracefully stop HTTP and native observers too.
        };
        menu.Opening += (_, _) => status.Text = state.Read().Audio.Available
            ? T("실행 중 · 오디오 연결됨", "Running · audio available")
            : T("실행 중 · 오디오 출력 없음", "Running · no audio output");
        tray = new NotifyIcon
        {
            Icon = icon, ContextMenuStrip = menu,
            Text = ProductInfo.DisplayName + " — " + T("실행 중", "Running"), Visible = true
        };
        tray.DoubleClick += (_, _) => ShowPairing(config, state, session);
        if (showPairing) dispatcher.BeginInvoke((Action)(() => ShowPairing(config, state, session)));
    }
    private void ShowPairing(AgentConfig config, StateStore state, PairingSession session)
    {
        if (pairing is null || pairing.IsDisposed) pairing = new PairingForm(config, state, icon, session);
        if (!pairing.Visible) pairing.Show();
        if (pairing.WindowState == FormWindowState.Minimized) pairing.WindowState = FormWindowState.Normal;
        pairing.Activate();
    }
    public void RequestClose()
    {
        try { dispatcher.BeginInvoke((Action)ExitThread); }
        catch (InvalidOperationException) { /* Already shutting down. */ }
    }
    protected override void ExitThreadCore()
    {
        tray.Visible = false;
        pairing?.Close();
        base.ExitThreadCore();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            pairing?.Dispose();
            tray.Visible = false;
            tray.Dispose();
            menu.Dispose();
            icon.Dispose();
            dispatcher.Dispose();
        }
        base.Dispose(disposing);
    }
    private static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        using var blue = new SolidBrush(Color.FromArgb(52, 113, 225));
        using var white = new SolidBrush(Color.White);
        using var stem = new Pen(Color.White, 3);
        graphics.FillEllipse(blue, 1, 1, 30, 30);
        graphics.DrawLine(stem, 14, 9, 14, 22);
        graphics.DrawLine(stem, 23, 7, 23, 20);
        graphics.DrawLine(stem, 14, 9, 23, 7);
        graphics.FillEllipse(white, 7, 19, 8, 6);
        graphics.FillEllipse(white, 16, 17, 8, 6);
        var handle = bitmap.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}

internal sealed class PairingForm : Form
{
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly Label feedback = new() { AutoSize = true, ForeColor = Color.DimGray };
    public PairingForm(AgentConfig config, StateStore state, Icon icon, PairingSession? session = null)
    {
        session ??= new PairingSession();
        Text = ProductInfo.DisplayName + " — " + TrayContext.T("페어링 정보", "Pairing information");
        Icon = icon;
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        ClientSize = new Size(680, 435);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 3, RowCount = 10,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        Controls.Add(layout);
        var heading = new Label { Text = TrayContext.T("SmartThings 연결 정보", "Connect to SmartThings"),
            AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 10) };
        layout.Controls.Add(heading, 0, 0); layout.SetColumnSpan(heading, 3);
        var status = new Label { AutoSize = true, Margin = new Padding(0, 0, 0, 10) };
        layout.Controls.Add(status, 0, 1); layout.SetColumnSpan(status, 3);
        AddField(layout, 2, TrayContext.T("PC IPv4 주소", "PC IPv4 address"), config.BindAddress);
        AddField(layout, 3, TrayContext.T("PC 포트", "PC port"), config.Port.ToString(CultureInfo.InvariantCulture));
        var code = AddField(layout, 4, TrayContext.T("페어링 코드", "Pairing code"), session.Generate());
        code.Font = new Font("Consolas", 18, FontStyle.Bold);
        var expiry = new Label { AutoSize = true };
        layout.Controls.Add(expiry, 1, 5);
        var renew = new Button { Text = TrayContext.T("새 코드 만들기", "New code"), AutoSize = true };
        renew.Click += (_, _) => code.Text = session.Generate();
        layout.Controls.Add(renew, 1, 6);
        var help = new Label { AutoSize = true, MaximumSize = new Size(625, 0), Margin = new Padding(0, 12, 0, 12),
            Text = TrayContext.T("SmartThings 설정에 PC 주소와 10자리 페어링 코드만 입력하세요.\n연결 후 코드는 만료되어도 됩니다. 기기 ID·토큰은 자동으로 처리됩니다.",
                "Enter the PC address and 10-digit pairing code in SmartThings settings.\nOnce paired, the code may expire. Device ID and token are handled automatically.") };
        layout.Controls.Add(help, 0, 7); layout.SetColumnSpan(help, 3);
        layout.Controls.Add(feedback, 0, 8); layout.SetColumnSpan(feedback, 3);
        var close = new Button { Text = TrayContext.T("닫기", "Close"), AutoSize = true, Anchor = AnchorStyles.Right };
        close.Click += (_, _) => Close();
        layout.Controls.Add(close, 2, 9);
        CancelButton = close;
        void UpdateStatus() => status.Text = state.Read().Audio.Available
            ? TrayContext.T("실행 중 · 오디오 연결됨", "Running · audio available")
            : TrayContext.T("실행 중 · 오디오 출력 없음", "Running · no audio output");
        void UpdateExpiry()
        {
            var remaining = session.Remaining;
            expiry.Text = remaining > TimeSpan.Zero
                ? TrayContext.T("코드 유효 시간: ", "Code expires in: ") + remaining.ToString(@"mm\:ss")
                : TrayContext.T("코드가 만료되었습니다. 새 코드를 만드세요.", "Code expired. Generate a new code.");
        }
        timer.Tick += (_, _) => { UpdateStatus(); UpdateExpiry(); };
        UpdateStatus(); UpdateExpiry(); timer.Start();
    }
    private TextBox AddField(TableLayoutPanel layout, int row, string label, string value, bool secret = false)
    {
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        var text = new TextBox { Text = value, ReadOnly = true, Dock = DockStyle.Fill,
            UseSystemPasswordChar = secret, AccessibleName = label, Margin = new Padding(3, 6, 8, 6) };
        layout.Controls.Add(text, 1, row);
        var copy = new Button { Text = TrayContext.T("복사", "Copy"), AutoSize = true,
            AccessibleName = label + " " + TrayContext.T("복사", "Copy"), Anchor = AnchorStyles.Right };
        copy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(text.Text);
                feedback.Text = label + TrayContext.T(" 복사됨", " copied");
            }
            catch (ExternalException)
            {
                feedback.Text = TrayContext.T("클립보드가 사용 중입니다. 다시 시도하세요.", "Clipboard is busy. Try again.");
            }
        };
        layout.Controls.Add(copy, 2, row);
        return text;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Stop(); timer.Dispose(); }
        base.Dispose(disposing);
    }
}
