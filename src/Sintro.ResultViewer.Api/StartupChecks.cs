using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Sintro.ResultViewer.Security;

namespace Sintro.ResultViewer;

/// <summary>Refuses to start on a configuration that would fail silently later; warns on public exposure.</summary>
public static class StartupChecks
{
    public static void Run(ILogger logger, SintroOptions settings, SessionToken sessionToken)
    {
        RefuseWeakTokens(settings);
        LogTokenSummary(logger, settings);
        CheckNetworks(settings);
        CheckPageSizes(settings);
        CheckPollInterval(settings);
        WarnOnUnknownTimeZone(logger, settings);
        LogNetworkPolicy(logger, settings);

        logger.LogInformation(
            "Viewer session token minted ({Length} chars, memory only)", sessionToken.Value.Length);
    }

    private static void RefuseWeakTokens(SintroOptions settings)
    {
        var tooShort = settings.AllTokens.Count(token => token.Length < SintroOptions.MinimumTokenLength);
        if (tooShort > 0)
            throw new InvalidOperationException(
                $"{tooShort} configured API token(s) are shorter than " +
                $"{SintroOptions.MinimumTokenLength} characters, so the API refuses to start. " +
                "Replace them, for example with:" +
                Environment.NewLine + Environment.NewLine +
                "  " + GenerateToken());

        if (settings.ReadTokens.Intersect(settings.WriteTokens).Any())
            throw new InvalidOperationException(
                "The same token appears in both Sintro:ApiReadTokens and Sintro:ApiWriteTokens. " +
                "A write token already grants read, so listing it twice only makes the intended " +
                "scope ambiguous. Remove it from the read list.");
    }

    private static void LogTokenSummary(ILogger logger, SintroOptions settings)
    {
        var tokens = settings.AllTokens.ToList();

        // A viewer-only range has nothing external to authenticate; the session token still covers the viewer.
        if (tokens.Count == 0)
            logger.LogInformation(
                "No API token configured — the bundled viewer works, and nothing else can read. " +
                "Add one under Sintro:ApiReadTokens to let event software in, for example: {Example}",
                GenerateToken()[..24] + "…");

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
    }

    private static void CheckNetworks(SintroOptions settings)
    {
        CheckAllowlist("Sintro:Network:Api", settings.Network.Api ?? []);
        CheckAllowlist("Sintro:Network:Web", settings.Network.Web ?? []);
        RefuseUnparseableRanges("Sintro:TrustedProxies", settings.TrustedProxies ?? []);
    }

    private static void CheckPollInterval(SintroOptions settings)
    {
        if (settings.Live.PollMilliseconds < LiveOptions.MinimumPollMilliseconds)
            throw new InvalidOperationException(
                $"Sintro:Live:PollMilliseconds is {settings.Live.PollMilliseconds}; it must be at least " +
                $"{LiveOptions.MinimumPollMilliseconds}.");
    }

    private static void CheckAllowlist(string key, string[] entries)
    {
        if (entries.Length == 0)
            throw new InvalidOperationException(
                $"{key} is empty, so nothing could reach that surface and the API refuses to " +
                "start. List the networks that may connect — a range LAN is usually:" +
                Environment.NewLine + Environment.NewLine +
                "  " + string.Join(", ", NetworkOptions.PrivateSpace) +
                Environment.NewLine + Environment.NewLine +
                "See appsettings.jsonc, which ships with exactly that list.");

        RefuseUnparseableRanges(key, entries);
    }

    // The gate skips entries it cannot parse, so a typo would silently lock a network out rather than widen access.
    private static void RefuseUnparseableRanges(string key, string[] entries)
    {
        var invalid = entries.Where(entry => !IpRange.TryParse(entry, out _)).ToList();
        if (invalid.Count > 0)
            throw new InvalidOperationException(
                $"{key} contains entries that are not valid addresses or CIDR ranges, " +
                $"so the API refuses to start: {string.Join(", ", invalid.Select(entry => $"'{entry}'"))}. " +
                "Expected forms: 192.168.1.0/24, 10.0.0.5, fd00::/8.");
    }

    // Math.Clamp throws when the bounds cross, which would turn every list request into a 500 while /health stayed green.
    private static void CheckPageSizes(SintroOptions settings)
    {
        if (settings.MaxPageSize < 1)
            throw new InvalidOperationException(
                $"Sintro:MaxPageSize is {settings.MaxPageSize}; it must be at least 1.");

        if (settings.DefaultPageSize < 1 || settings.DefaultPageSize > settings.MaxPageSize)
            throw new InvalidOperationException(
                $"Sintro:DefaultPageSize is {settings.DefaultPageSize}; it must be between 1 and " +
                $"Sintro:MaxPageSize ({settings.MaxPageSize}).");
    }

    // The clock falls back to the host zone rather than refusing: a service that will not start is an event without results.
    private static void WarnOnUnknownTimeZone(ILogger logger, SintroOptions settings)
    {
        if (string.IsNullOrWhiteSpace(settings.TimeZone) ||
            TimeZoneInfo.TryFindSystemTimeZoneById(settings.TimeZone, out _))
            return;

        logger.LogWarning(
            "Sintro:TimeZone '{TimeZone}' is not a known time zone; using this machine's zone " +
            "({Local}) instead. Timestamps will carry the wrong offset if the two differ.",
            settings.TimeZone, TimeZoneInfo.Local.Id);
    }

    private static void LogNetworkPolicy(ILogger logger, SintroOptions settings)
    {
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
    }

    public static string GenerateToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(96));
}
