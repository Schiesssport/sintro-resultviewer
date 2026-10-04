using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace Sintro.ResultViewer;

public static class StartupBanner
{
    public static void LogReachableAddresses(ILogger logger, IEnumerable<string> serverAddresses)
    {
        var displayUrls = new List<(string Label, string Url)>();

        foreach (var address in serverAddresses)
        {
            if (Uri.TryCreate(address.Replace("+", "0.0.0.0").Replace("*", "0.0.0.0"), UriKind.Absolute, out var uri))
                displayUrls.AddRange(ResolveDisplayUrls(address, uri));
            else
                logger.LogInformation("Listening on {Address}", address);
        }

        if (displayUrls.Count > 0) logger.LogVerbatim(RenderBanner(displayUrls));
    }

    // A wildcard address is nothing an operator can type into a TV browser; resolve it to this machine's real addresses.
    private static IEnumerable<(string Label, string Url)> ResolveDisplayUrls(string configured, Uri uri)
    {
        if (!IsWildcard(uri.Host)) return [(Label: "configured", Url: configured)];

        return LocalAddresses()
            .Select(entry => (Label: entry.Interface, Url: $"http://{Format(entry.Address)}:{uri.Port}"))
            .Prepend((Label: "this machine", Url: $"http://localhost:{uri.Port}"));
    }

    // A frame of its own, so the address does not scroll away behind the first connected display.
    private static string RenderBanner(List<(string Label, string Url)> displayUrls)
    {
        var column = displayUrls.Max(line => line.Url.Length) + 4;
        var rule = new string('#', Math.Max(column + displayUrls.Max(line => line.Label.Length) + 10, 58));

        var banner = new StringBuilder()
            .AppendLine()
            .AppendLine(rule)
            .AppendLine("#")
            .AppendLine("#  Sintro Resultviewer is running. Open a display at:")
            .AppendLine("#");
        foreach (var (label, url) in displayUrls)
            banner.AppendLine($"#      {url.PadRight(column)}{label}");
        banner
            .AppendLine("#")
            .AppendLine("#  Press Ctrl+C to stop.")
            .AppendLine("#")
            .AppendLine(rule);

        if (displayUrls.Count == 1)
            banner.AppendLine()
                .AppendLine("No network interface is up besides this machine's own, so no display can connect yet.");

        return banner.ToString();
    }

    private static bool IsWildcard(string host) =>
        host is "0.0.0.0" or "::" or "[::]" || IPAddress.TryParse(host.Trim('[', ']'), out var ip)
            && (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any));

    // Link-local addresses need a zone index nobody types into a TV; loopback is listed separately.
    private static IEnumerable<(string Interface, IPAddress Address)> LocalAddresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.OperationalStatus == OperationalStatus.Up
                          && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(nic => nic.GetIPProperties().UnicastAddresses
                .Select(unicast => (nic.Name, unicast.Address)))
            .Where(entry => !entry.Address.IsIPv6LinkLocal
                            && !entry.Address.IsIPv6SiteLocal
                            && !IPAddress.IsLoopback(entry.Address)
                            && !(entry.Address.AddressFamily == AddressFamily.InterNetwork
                                 && entry.Address.GetAddressBytes() is [169, 254, ..]));

    private static string Format(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{address}]" : address.ToString();
}
