using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace STMediaBridge;

public sealed class LanAccess(string bindAddress)
{
    private readonly IPAddress address = IPAddress.Parse(bindAddress);

    public bool AllowsArtwork(IPAddress? remote)
    {
        if (remote is null) return false;
        if (IPAddress.IsLoopback(remote)) return true;
        // Use the actual subnet of the bound interface, not all RFC1918 ranges
        // or a hard-coded /24. Re-read so mask changes do not require a restart.
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
                    if (unicast.Address.Equals(address) && address.AddressFamily == AddressFamily.InterNetwork &&
                        SameSubnet(address, remote, unicast.IPv4Mask)) return true;
            }
        }
        catch (NetworkInformationException) { }
        return false;
    }

    public static bool SameSubnet(IPAddress local, IPAddress remote, IPAddress mask)
    {
        if (local.AddressFamily != AddressFamily.InterNetwork || remote.AddressFamily != AddressFamily.InterNetwork ||
            mask.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(local)) return false;
        var l = local.GetAddressBytes();
        var r = remote.GetAddressBytes();
        var m = mask.GetAddressBytes();
        if (r[0] == 0 || r[0] >= 224 || IPAddress.IsLoopback(remote) || m.All(b => b == 0)) return false;
        for (var i = 0; i < 4; i++)
            if ((l[i] & m[i]) != (r[i] & m[i])) return false;
        return true;
    }
}
