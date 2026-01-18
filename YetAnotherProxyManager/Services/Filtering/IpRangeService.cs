using System.Net;
using System.Net.Sockets;

namespace YetAnotherProxyManager.Services.Filtering;

public class IpRangeService
{
    // Private network ranges (RFC 1918)
    private static readonly (IPAddress Start, IPAddress End)[] PrivateRanges = new[]
    {
        (IPAddress.Parse("10.0.0.0"), IPAddress.Parse("10.255.255.255")),
        (IPAddress.Parse("172.16.0.0"), IPAddress.Parse("172.31.255.255")),
        (IPAddress.Parse("192.168.0.0"), IPAddress.Parse("192.168.255.255"))
    };

    // Loopback range
    private static readonly (IPAddress Start, IPAddress End)[] LoopbackRanges = new[]
    {
        (IPAddress.Parse("127.0.0.0"), IPAddress.Parse("127.255.255.255")),
        (IPAddress.IPv6Loopback, IPAddress.IPv6Loopback)
    };

    // Link-local ranges
    private static readonly (IPAddress Start, IPAddress End)[] LinkLocalRanges = new[]
    {
        (IPAddress.Parse("169.254.0.0"), IPAddress.Parse("169.254.255.255")),
        (IPAddress.Parse("fe80::"), IPAddress.Parse("fe80::ffff:ffff:ffff:ffff"))
    };

    public bool IsInRange(string ipString, string startIp, string endIp)
    {
        if (!IPAddress.TryParse(ipString, out var ip))
            return false;
        if (!IPAddress.TryParse(startIp, out var start))
            return false;
        if (!IPAddress.TryParse(endIp, out var end))
            return false;

        return IsInRange(ip, start, end);
    }

    public bool IsInRange(IPAddress ip, IPAddress start, IPAddress end)
    {
        if (ip.AddressFamily != start.AddressFamily || ip.AddressFamily != end.AddressFamily)
            return false;

        var ipBytes = ip.GetAddressBytes();
        var startBytes = start.GetAddressBytes();
        var endBytes = end.GetAddressBytes();

        bool greaterThanOrEqualStart = true;
        bool lessThanOrEqualEnd = true;

        for (int i = 0; i < ipBytes.Length; i++)
        {
            if (ipBytes[i] < startBytes[i]) greaterThanOrEqualStart = false;
            if (ipBytes[i] > endBytes[i]) lessThanOrEqualEnd = false;

            if (ipBytes[i] > startBytes[i]) break;
        }

        for (int i = 0; i < ipBytes.Length; i++)
        {
            if (ipBytes[i] > endBytes[i]) lessThanOrEqualEnd = false;
            if (ipBytes[i] < endBytes[i]) break;
        }

        return greaterThanOrEqualStart && lessThanOrEqualEnd;
    }

    public bool IsInCidr(string ipString, string cidr)
    {
        if (!IPAddress.TryParse(ipString, out var ip))
            return false;

        return IsInCidr(ip, cidr);
    }

    public bool IsInCidr(IPAddress ip, string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2)
            return false;

        if (!IPAddress.TryParse(parts[0], out var network))
            return false;

        if (!int.TryParse(parts[1], out var prefixLength))
            return false;

        if (ip.AddressFamily != network.AddressFamily)
            return false;

        var ipBytes = ip.GetAddressBytes();
        var networkBytes = network.GetAddressBytes();
        var totalBits = ipBytes.Length * 8;

        if (prefixLength < 0 || prefixLength > totalBits)
            return false;

        for (int i = 0; i < ipBytes.Length; i++)
        {
            var bitsToCompare = Math.Min(8, prefixLength - i * 8);
            if (bitsToCompare <= 0) break;

            var mask = (byte)(0xFF << (8 - bitsToCompare));
            if ((ipBytes[i] & mask) != (networkBytes[i] & mask))
                return false;
        }

        return true;
    }

    public bool MatchesSingleIp(string ipString, string targetIp)
    {
        if (!IPAddress.TryParse(ipString, out var ip))
            return false;
        if (!IPAddress.TryParse(targetIp, out var target))
            return false;

        return ip.Equals(target);
    }

    public bool IsPrivateIp(string ipString)
    {
        if (!IPAddress.TryParse(ipString, out var ip))
            return false;

        return IsPrivateIp(ip);
    }

    public bool IsPrivateIp(IPAddress ip)
    {
        // Check loopback
        if (IPAddress.IsLoopback(ip))
            return true;

        // IPv6 handling
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // Check if it's a mapped IPv4 address
            if (ip.IsIPv4MappedToIPv6)
            {
                ip = ip.MapToIPv4();
            }
            else
            {
                // Check IPv6 unique local (fc00::/7)
                var bytes = ip.GetAddressBytes();
                if ((bytes[0] & 0xFE) == 0xFC)
                    return true;

                // Check link-local (fe80::/10)
                if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80)
                    return true;

                return false;
            }
        }

        // Check private IPv4 ranges
        foreach (var (start, end) in PrivateRanges)
        {
            if (IsInRange(ip, start, end))
                return true;
        }

        // Check link-local
        foreach (var (start, end) in LinkLocalRanges)
        {
            if (start.AddressFamily == ip.AddressFamily && IsInRange(ip, start, end))
                return true;
        }

        return false;
    }

    public bool IsPublicIp(string ipString)
    {
        if (!IPAddress.TryParse(ipString, out var ip))
            return false;

        return !IsPrivateIp(ip) && !IPAddress.IsLoopback(ip);
    }

    public bool IsLocalNetworkIp(string ipString)
    {
        // Checks for loopback, private, and link-local addresses
        return IsPrivateIp(ipString) || IsLoopback(ipString);
    }

    public bool IsLoopback(string ipString)
    {
        if (!IPAddress.TryParse(ipString, out var ip))
            return false;

        return IPAddress.IsLoopback(ip);
    }

    /// <summary>
    /// Parse a CIDR notation and return the start and end IP addresses
    /// </summary>
    public (IPAddress Start, IPAddress End)? ParseCidr(string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2)
            return null;

        if (!IPAddress.TryParse(parts[0], out var network))
            return null;

        if (!int.TryParse(parts[1], out var prefixLength))
            return null;

        var networkBytes = network.GetAddressBytes();
        var totalBits = networkBytes.Length * 8;

        if (prefixLength < 0 || prefixLength > totalBits)
            return null;

        var startBytes = (byte[])networkBytes.Clone();
        var endBytes = (byte[])networkBytes.Clone();

        // Calculate start (network address) and end (broadcast address)
        for (int i = 0; i < networkBytes.Length; i++)
        {
            var bitsInThisByte = Math.Min(8, Math.Max(0, prefixLength - i * 8));
            var mask = (byte)(0xFF << (8 - bitsInThisByte));
            var inverseMask = (byte)~mask;

            startBytes[i] = (byte)(networkBytes[i] & mask);
            endBytes[i] = (byte)(networkBytes[i] | inverseMask);
        }

        return (new IPAddress(startBytes), new IPAddress(endBytes));
    }
}
