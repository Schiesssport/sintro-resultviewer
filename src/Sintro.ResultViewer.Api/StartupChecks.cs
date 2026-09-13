using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Sintro.ResultViewer.Security;

namespace Sintro.ResultViewer;

/// <summary>Fail fast on a misconfigured token, and make public exposure impossible to miss.</summary>
public static class StartupChecks
{
    public static void Run(ILogger logger, SintroOptions settings, SessionToken sessionToken)
    {
        var tokens = settings.AllTokens.ToList();

        // No configured token is a legitimate setup, not a misconfiguration: a range that only
        // runs the bundled displays has no external consumer to authenticate. The viewer still
        // authenticates with the per-process session token, and the network gate is the real
        // boundary either way — so requiring a token here would be friction without safety.
        if (tokens.Count == 0)
            logger.LogInformation(
                "No API token configured — the bundled viewer works, and nothing else can read. " +
                "Add one under Sintro:ApiReadTokens to let event software in, for example: {Example}",
                GenerateToken()[..24] + "…");

        var tooShort = tokens.Count(token => token.Length < SintroOptions.MinimumTokenLength);
        if (tooShort > 0)
            throw new InvalidOperationException(
                $"{tooShort} configured API token(s) are shorter than " +
                $"{SintroOptions.MinimumTokenLength} characters, so the API refuses to start. " +
                "Replace them, for example with:" +
                Environment.NewLine + Environment.NewLine +
                "  " + GenerateToken());

        var shared = settings.ReadTokens.Intersect(settings.WriteTokens).ToList();
        if (shared.Count > 0)
            throw new InvalidOperationException(
                "The same token appears in both Sintro:ApiReadTokens and Sintro:ApiWriteTokens. " +
                "A write token already grants read, so listing it twice only makes the intended " +
                "scope ambiguous. Remove it from the read list.");

        if (tokens.Any(token => token.Length < SintroOptions.RecommendedTokenLength))
            logger.LogInformation(
                "Some API tokens are shorter than the recommended {Recommended} characters.",
                SintroOptions.RecommendedTokenLength);

        logger.LogInformation(
            "API tokens configured: {Read} read, {Write} write",
            settings.ReadTokens.Count, settings.WriteTokens.Count);

        if (settings.WriteTokens.Count > 0)
            logger.LogInformation(
                "Write tokens are accepted but no endpoint writes yet — the scope is reserved.");

        // No allowlist means nothing could connect — including the machine it runs on. That is
        // never what someone intended, and silently denying everything is as unhelpful as
        // silently allowing everything. Say what is missing and what to put there.
        //
        // And the gate silently skips entries it cannot parse, so a typo'd CIDR would not widen
        // access — it would silently lock that network out (or shrink the proxy list), which is
        // miserable to debug on a range PC. Refuse to start and name the entry instead.
        foreach (var (key, entries, mustNotBeEmpty) in new[]
                 {
                     ("Sintro:Network:Api", settings.Network.Api ?? [], true),
                     ("Sintro:Network:Web", settings.Network.Web ?? [], true),
                     ("Sintro:TrustedProxies", settings.TrustedProxies ?? [], false),
                 })
        {
            if (mustNotBeEmpty && entries.Length == 0)
                throw new InvalidOperationException(
                    $"{key} is empty, so nothing could reach that surface and the API refuses to " +
                    "start. List the networks that may connect — a range LAN is usually:" +
                    Environment.NewLine + Environment.NewLine +
                    "  " + string.Join(", ", NetworkOptions.PrivateSpace) +
                    Environment.NewLine + Environment.NewLine +
                    "See appsettings.jsonc, which ships with exactly that list.");

            var invalid = entries.Where(entry => !IpRange.TryParse(entry, out _)).ToList();
            if (invalid.Count > 0)
                throw new InvalidOperationException(
                    $"{key} contains entries that are not valid addresses or CIDR ranges, " +
                    $"so the API refuses to start: {string.Join(", ", invalid.Select(entry => $"'{entry}'"))}. " +
                    "Expected forms: 192.168.1.0/24, 10.0.0.5, fd00::/8.");
        }

        // Math.Clamp throws when the bounds cross, so a zero or negative maximum would turn every
        // list request into a 500 while /health stayed green.
        if (settings.MaxPageSize < 1)
            throw new InvalidOperationException(
                $"Sintro:MaxPageSize is {settings.MaxPageSize}; it must be at least 1.");

        if (settings.DefaultPageSize < 1 || settings.DefaultPageSize > settings.MaxPageSize)
            throw new InvalidOperationException(
                $"Sintro:DefaultPageSize is {settings.DefaultPageSize}; it must be between 1 and " +
                $"Sintro:MaxPageSize ({settings.MaxPageSize}).");

        // The clock falls back to the host zone rather than refusing to start, because a wrong
        // offset is a nuisance and a service that will not start is an event without results.
        // But it must not be silent: the setting exists precisely for hosts whose own zone is wrong.
        if (!string.IsNullOrWhiteSpace(settings.TimeZone) &&
            !TimeZoneInfo.TryFindSystemTimeZoneById(settings.TimeZone, out _))
            logger.LogWarning(
                "Sintro:TimeZone '{TimeZone}' is not a known time zone; using this machine's zone " +
                "({Local}) instead. Timestamps will carry the wrong offset if the two differ.",
                settings.TimeZone, TimeZoneInfo.Local.Id);

        if (settings.TrustedProxies is { Length: > 0 })
            logger.LogInformation(
                "Trusting X-Forwarded-For from {Proxies}. Everything else is judged by its socket address.",
                string.Join(", ", settings.TrustedProxies));

        var exposure = NetworkGateExtensions.DescribePublicExposure(settings);
        if (exposure.Count > 0)
            logger.LogWarning(
                "PUBLIC ACCESS ENABLED — non-private ranges may reach this machine: {Exposure}. " +
                "Shooter names are personal data; remove these ranges unless publishing was intended.",
                string.Join(" | ", exposure));

        logger.LogInformation(
            "Viewer session token minted ({Length} chars, memory only)", sessionToken.Value.Length);
    }

    /// <summary>
    /// Says where the displays should be pointed, once the server has actually bound.
    ///
    /// A wildcard address ("http://[::]:5000") tells the operator nothing they can type into a TV
    /// browser, and the machine's own address is exactly what they do not know — it is a range PC
    /// on someone else's DHCP. So the interfaces are resolved to real addresses here.
    /// </summary>
    public static void LogReachableAddresses(ILogger logger, IEnumerable<string> serverAddresses)
    {
        var lines = new List<(string Label, string Url)>();
        var unresolved = new List<string>();

        foreach (var address in serverAddresses)
        {
            if (!Uri.TryCreate(address.Replace("+", "0.0.0.0").Replace("*", "0.0.0.0"),
                    UriKind.Absolute, out var uri))
            {
                unresolved.Add(address);
                continue;
            }

            // A specific address was configured: it is already the answer, wildcards aside.
            if (!IsWildcard(uri.Host))
            {
                lines.Add(("configured", address));
                continue;
            }

            lines.Add(("this machine", $"http://localhost:{uri.Port}"));
            lines.AddRange(LocalAddresses()
                .Select(entry => (entry.Interface, $"http://{Format(entry.Address)}:{uri.Port}")));
        }

        foreach (var address in unresolved) logger.LogInformation("Listening on {Address}", address);
        if (lines.Count == 0) return;

        // The address is the one thing the operator came to this window for, and they are reading
        // it off a screen to type into a TV across the hall. So it gets a frame of its own rather
        // than a place in the log, where it would scroll away behind the first connected display.
        var column = lines.Max(line => line.Url.Length) + 4;
        var rule = new string('#', Math.Max(column + lines.Max(line => line.Label.Length) + 10, 58));

        var banner = new System.Text.StringBuilder()
            .AppendLine()
            .AppendLine(rule)
            .AppendLine("#")
            .AppendLine("#  Sintro Resultviewer is running. Open a display at:")
            .AppendLine("#");
        foreach (var (label, url) in lines)
            banner.AppendLine($"#      {url.PadRight(column)}{label}");
        banner
            .AppendLine("#")
            .AppendLine("#  Press Ctrl+C to stop.")
            .AppendLine("#")
            .AppendLine(rule);

        if (lines.Count == 1)
            banner.AppendLine()
                .AppendLine("No network interface is up besides this machine's own, so no display " +
                            "can connect yet.");

        logger.LogVerbatim(banner.ToString());
    }

    static bool IsWildcard(string host) =>
        host is "0.0.0.0" or "::" or "[::]" || IPAddress.TryParse(host.Trim('[', ']'), out var ip)
            && (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any));

    /// <summary>Addresses of every interface that is up, loopback and link-local aside.</summary>
    static IEnumerable<(string Interface, IPAddress Address)> LocalAddresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.OperationalStatus == OperationalStatus.Up
                          && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(nic => nic.GetIPProperties().UnicastAddresses
                .Select(unicast => (nic.Name, unicast.Address)))
            // Link-local addresses only work with a zone index that no one is going to type into a
            // TV, and IPv6 privacy/temporary addresses would just add noise. An installation is
            // reached by its IPv4 address, or by its routable IPv6 one.
            .Where(entry => !entry.Address.IsIPv6LinkLocal
                            && !entry.Address.IsIPv6SiteLocal
                            && !IPAddress.IsLoopback(entry.Address)
                            && !(entry.Address.AddressFamily == AddressFamily.InterNetwork
                                 && entry.Address.GetAddressBytes() is [169, 254, ..]));

    static string Format(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{address}]" : address.ToString();

    public static string GenerateToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(96));
}
