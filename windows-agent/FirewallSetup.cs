using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace STMediaBridge;

// Only public rule parameters cross the UAC boundary. Never pass credentials,
// config paths, certificate files, or arbitrary executable paths to the helper.
internal sealed record FirewallSetup(string Address, int Port, string RuleId)
{
    public static FirewallSetup Parse(string[] args)
    {
        if (args.Length != 4 || args[0] != "--configure-firewall" ||
            !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out var port))
            throw new InvalidDataException("Invalid firewall arguments.");
        return new FirewallSetup(args[1], port, args[3]).Validate();
    }

    public FirewallSetup Validate()
    {
        if (!IPAddress.TryParse(Address, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork ||
            ip.ToString() != Address || ip.GetAddressBytes()[0] == 0 || ip.GetAddressBytes()[0] >= 224 ||
            IPAddress.IsLoopback(ip) || Port is < 1024 or > 65535 ||
            !Guid.TryParseExact(RuleId, "D", out var id) || id == Guid.Empty)
            throw new InvalidDataException("Invalid firewall address, port or rule ID.");
        return this;
    }

    public ProcessStartInfo Elevation(string executable)
    {
        Validate();
        var info = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var arg in new[] { "--configure-firewall", Address, Port.ToString(CultureInfo.InvariantCulture), RuleId })
            info.ArgumentList.Add(arg);
        return info;
    }

    public void Apply(string executable, Func<string[], bool> run)
    {
        Validate();
        var name = "name=STMediaBridge-" + RuleId;
        var settings = new[] { "dir=in", "action=allow", "protocol=TCP", "profile=private",
            "localip=" + Address, "localport=" + Port.ToString(CultureInfo.InvariantCulture),
            "remoteip=LocalSubnet", "program=" + executable, "enable=yes" };
        if (!run(new[] { "advfirewall", "firewall", "set", "rule", name, "new" }.Concat(settings).ToArray()) &&
            !run(new[] { "advfirewall", "firewall", "add", "rule", name }.Concat(settings).ToArray()))
            throw new IOException("Windows firewall update failed.");
    }
}
