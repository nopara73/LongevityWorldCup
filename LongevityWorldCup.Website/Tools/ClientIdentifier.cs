using System.Net;
using System.Net.Sockets;

namespace LongevityWorldCup.Website.Tools;

public static class ClientIdentifier
{
    public const string Unknown = "unknown";

    public static string From(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var remoteIp = context.Connection.RemoteIpAddress;
        if (IsTrustedProxyAddress(remoteIp))
        {
            var forwardedIp = TryGetForwardedIp(context);
            if (forwardedIp is not null)
                return forwardedIp.ToString();
        }

        return remoteIp?.ToString() ?? Unknown;
    }

    private static IPAddress? TryGetForwardedIp(HttpContext context)
    {
        var cloudflareIp = context.Request.Headers["CF-Connecting-IP"].FirstOrDefault();
        if (TryParseIp(cloudflareIp, out var parsedCloudflareIp))
            return parsedCloudflareIp;

        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            var firstHop = forwardedFor.Split(',')[0].Trim();
            if (TryParseIp(firstHop, out var parsedForwardedIp))
                return parsedForwardedIp;
        }

        return null;
    }

    private static bool TryParseIp(string? value, out IPAddress? ipAddress)
    {
        ipAddress = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (!IPAddress.TryParse(value.Trim(), out var parsed))
            return false;

        ipAddress = parsed;
        return true;
    }

    private static bool IsTrustedProxyAddress(IPAddress? ipAddress)
    {
        if (ipAddress is null)
            return false;

        if (IPAddress.IsLoopback(ipAddress))
            return true;

        if (ipAddress.IsIPv4MappedToIPv6)
            ipAddress = ipAddress.MapToIPv4();

        if (ipAddress.AddressFamily != AddressFamily.InterNetwork)
            return false;

        var bytes = ipAddress.GetAddressBytes();
        return bytes[0] == 10 ||
               bytes[0] == 127 ||
               bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31 ||
               bytes[0] == 192 && bytes[1] == 168;
    }
}
