using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Principal;

namespace STMediaBridge;

internal static class FirstRun
{
    public static string Executable => Environment.ProcessPath ?? throw new IOException("Executable path unavailable.");

    public static bool InstallPortable()
    {
        if (Path.GetFileNameWithoutExtension(Executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase) ||
            File.Exists(Path.Combine(AppContext.BaseDirectory, "STMediaBridge.Agent.dll"))) return false;
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductInfo.DataDirectory, "bin");
        var target = Path.Combine(directory, "STMediaBridge.Agent.exe");
        if (string.Equals(Path.GetFullPath(Executable), target, StringComparison.OrdinalIgnoreCase)) return false;
        Directory.CreateDirectory(directory);
        File.Copy(Executable, target, true);
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        return true;
    }

    public static string[] Addresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
        .SelectMany(n => n.GetIPProperties().UnicastAddresses)
        .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address) && !a.Address.ToString().StartsWith("169.254."))
        .Select(a => a.Address.ToString()).Distinct().ToArray();

    public static bool IsLocalPeer(IPAddress remote, string bindAddress)
    {
        if (remote.AddressFamily != AddressFamily.InterNetwork || remote.GetAddressBytes()[0] == 0 || remote.GetAddressBytes()[0] >= 224) return false;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        foreach (var address in nic.GetIPProperties().UnicastAddresses)
        {
            if (address.Address.ToString() != bindAddress) continue;
            var local = address.Address.GetAddressBytes();
            var peer = remote.GetAddressBytes();
            var mask = address.IPv4Mask.GetAddressBytes();
            return local.Length == 4 && mask.Length == 4 && Enumerable.Range(0, 4).All(i => (local[i] & mask[i]) == (peer[i] & mask[i]));
        }
        return false;
    }

    public static bool Configure(string configPath, string[]? availableAddresses = null,
        Action<string, bool>? complete = null, Action<Form>? ready = null)
    {
        var completed = false;
        var thread = new Thread(() =>
        {
            Application.EnableVisualStyles();
            using var form = new Form { Text = ProductInfo.DisplayName, ClientSize = new Size(590, 330),
                StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false,
                Font = new Font("Segoe UI", 10), AutoScaleMode = AutoScaleMode.Dpi };
            var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(22), WrapContents = false };
            form.Controls.Add(layout);
            layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(535, 0), Text = TrayContext.T(
                "SmartThings와 연결할 PC 네트워크를 선택하세요.\n설정 파일과 인증서는 자동으로 생성됩니다.",
                "Choose the PC network used by SmartThings.\nConfiguration and certificate are created automatically.") });
            var addresses = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 530, Margin = new Padding(0, 15, 0, 15) };
            addresses.Items.AddRange(availableAddresses ?? Addresses());
            if (addresses.Items.Count > 0) addresses.SelectedIndex = 0;
            layout.Controls.Add(addresses);
            var startup = new CheckBox { Text = TrayContext.T("Windows 로그인 시 자동 실행", "Start automatically at Windows sign-in"), AutoSize = true };
            layout.Controls.Add(startup);
            layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(535, 0), Margin = new Padding(0, 12, 0, 12), Text = TrayContext.T(
                "시작하면 Windows 방화벽 승인 창이 표시됩니다.\n허브와 같은 사설 네트워크에서 사용하세요.",
                "Starting opens a Windows firewall permission prompt.\nUse a Private network shared with your hub.") });
            var start = new Button { Text = TrayContext.T("설정하고 시작", "Set up and start"), AutoSize = true, Enabled = addresses.Items.Count > 0 };
            layout.Controls.Add(start);
            start.Click += (_, _) =>
            {
                try
                {
                    if (complete is not null)
                    {
                        complete((string)addresses.SelectedItem!, startup.Checked);
                        completed = true;
                        form.Close();
                        return;
                    }
                    if (!File.Exists(configPath)) TlsIdentity.Initialize(configPath, (string)addresses.SelectedItem!);
                    new StartupSettings(Executable, configPath).SetEnabled(startup.Checked);
                    try { RequestFirewall(configPath); }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
                    {
                        MessageBox.Show(form, TrayContext.T("방화벽 승인이 완료되지 않았습니다. 트레이 메뉴의 방화벽 설정에서 다시 시도하세요.",
                            "Firewall setup was not completed. Retry from the tray's firewall menu."), ProductInfo.DisplayName);
                    }
                    completed = true;
                    form.Close();
                }
                catch (Exception ex) { MessageBox.Show(form, ex.Message, ProductInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error); }
            };
            form.AcceptButton = start;
            if (ready is not null) form.Shown += (_, _) => ready(form);
            Application.Run(form);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        return completed;
    }

    public static void RequestFirewall(string configPath)
    {
        var info = new ProcessStartInfo(Executable) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
        info.ArgumentList.Add("--configure-firewall"); info.ArgumentList.Add("--config"); info.ArgumentList.Add(configPath);
        using var process = Process.Start(info) ?? throw new IOException("Firewall setup could not start.");
        process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("Firewall setup failed.");
    }

    public static void ConfigureFirewall(string configPath)
    {
        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) throw new UnauthorizedAccessException();
        var config = AgentConfig.Load(configPath);
        var id = config.FirewallRuleId ?? config.DeviceId;
        if (!Guid.TryParseExact(id, "D", out var parsed) || parsed == Guid.Empty) throw new InvalidDataException("Invalid firewall ID.");
        // No shell command interpolation: netsh receives separate, validated arguments.
        bool Netsh(params string[] args)
        {
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "netsh.exe")) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!; process.WaitForExit();
            return process.ExitCode == 0;
        }
        var name = "name=STMediaBridge-" + id;
        // Update the exact UUID-named rule; repeated setup must not add duplicates.
        var settings = new[] { "dir=in", "action=allow", "protocol=TCP", "profile=private",
            "localip=" + config.BindAddress, "localport=" + config.Port, "remoteip=LocalSubnet", "program=" + Executable, "enable=yes" };
        if (!Netsh(new[] { "advfirewall", "firewall", "set", "rule", name, "new" }.Concat(settings).ToArray()) &&
            !Netsh(new[] { "advfirewall", "firewall", "add", "rule", name }.Concat(settings).ToArray()))
            throw new IOException("Windows firewall update failed.");
    }
}
