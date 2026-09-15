using System.Net;

namespace UniversalRemote.Provider.OrangeTv;

internal static class OrangeTvEndpoint
{
    public const int Port = 8080;
    private const string Path = "/remoteControl/cmd";

    public static bool TryNormalizeLocalAddress(string? value, out string address)
    {
        address = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var candidate = value.Trim();
        if (!IPAddress.TryParse(candidate, out var ip)) return false;
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) return false;

        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (!IsAllowedLocalAddress(ip)) return false;

        address = ip.ToString();
        return true;
    }

    public static Uri Status(string address)
        => Build(address, "operation=10");

    public static Uri Command(string address, int keyCode)
        => Build(address, $"operation=01&key={keyCode}&mode=0");

    private static Uri Build(string address, string query)
    {
        if (!TryNormalizeLocalAddress(address, out var normalized))
            throw new ArgumentException("Orange TV endpoint must be a private local IP address.", nameof(address));

        return new UriBuilder(Uri.UriSchemeHttp, normalized, Port, Path, query).Uri;
    }

    private static bool IsAllowedLocalAddress(IPAddress ip)
    {
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            return bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 169 && bytes[1] == 254);
        }

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var bytes = ip.GetAddressBytes();
            return ip.IsIPv6LinkLocal || (bytes[0] & 0xFE) == 0xFC; // fc00::/7 unique-local
        }

        return false;
    }
}
