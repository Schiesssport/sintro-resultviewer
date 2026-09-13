using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Options;
using Sintro.ResultViewer.Api.V2;

namespace Sintro.ResultViewer.Security;

public enum Surface { Api, Web }

/// <summary>
/// Source-address allowlist, evaluated before authentication so a rejected network never even
/// gets to guess tokens.
///
/// Port forwarding is destination NAT — it rewrites the destination and leaves the source alone —
/// so traffic arriving from the internet still carries a public source address and is refused
/// here. X-Forwarded-For is only unwound for hops listed in TrustedProxies; see
/// <see cref="ClientAddress"/>. Rejections log the address so an operator can see what is really
/// arriving (some consumer routers source-NAT forwarded traffic, which no code can distinguish
/// from genuinely local traffic).
/// </summary>
public sealed class NetworkGate(RequestDelegate next, IOptions<SintroOptions> options, ILogger<NetworkGate> logger)
{
    private readonly IReadOnlyList<IpRange> _api = IpRange.ParseAll(options.Value.Network.Api ?? []);
    private readonly IReadOnlyList<IpRange> _web = IpRange.ParseAll(options.Value.Network.Web ?? []);
    private readonly IReadOnlyList<IpRange> _trustedProxies =
        IpRange.ParseAll(options.Value.TrustedProxies ?? []);

    /// <summary>
    /// Sources already reported once. A port scan against a forwarded port would otherwise print
    /// thousands of warnings and scroll the address banner — the one line the operator needs —
    /// out of the window. Bounded so a scan cannot grow it without limit.
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> _reportedSources = new();
    private const int MaxReportedSources = 1000;

    public async Task InvokeAsync(HttpContext context)
    {
        var surface = ClassifySurface(context.Request.Path);
        var allowed = surface == Surface.Api ? _api : _web;

        var source = ClientAddress.Resolve(
            context.Connection.RemoteIpAddress,
            context.Request.Headers["X-Forwarded-For"].ToString(),
            _trustedProxies);

        if (source is null || !allowed.Any(range => range.Contains(source)))
        {
            LogRejection(surface, source, context.Request.Path);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new ApiError(
                "forbidden_network",
                source is null
                    ? "The forwarded client address could not be read, so the request is refused."
                    : $"Source address is not in the allowed {surface} ranges."));
            return;
        }

        await next(context);
    }

    private void LogRejection(Surface surface, IPAddress? source, PathString path)
    {
        var key = source?.ToString() ?? "unresolvable";
        var firstTime = _reportedSources.Count < MaxReportedSources && _reportedSources.TryAdd(key, 0);

        if (firstTime)
            logger.LogWarning("Blocked {Surface} request from {Source} for {Path} (further requests from it are logged at debug level)",
                surface, key, path);
        else
            logger.LogDebug("Blocked {Surface} request from {Source} for {Path}", surface, key, path);
    }

    /// <summary>
    /// The OpenAPI document counts as part of the docs (web) surface, not the data (api) surface:
    /// it is schema rather than shooter data, and the docs page must be able to load it even when
    /// the api allowlist is narrower than the web one.
    /// </summary>
    private static Surface ClassifySurface(PathString path) =>
        path.StartsWithSegments("/api") ? Surface.Api : Surface.Web;
}

public static class NetworkGateExtensions
{
    public static IApplicationBuilder UseNetworkGate(this IApplicationBuilder app) =>
        app.UseMiddleware<NetworkGate>();

    /// <summary>
    /// Non-private ranges are legitimate — someone who writes 0.0.0.0/0 knows what they are
    /// doing — but they are surfaced loudly at startup and in the viewer.
    /// </summary>
    public static IReadOnlyList<string> DescribePublicExposure(SintroOptions options)
    {
        var warnings = new List<string>();

        foreach (var (surface, ranges) in new[]
                 {
                     ("api", options.Network.Api ?? []),
                     ("web", options.Network.Web ?? []),
                 })
        {
            var risky = IpRange.ParseAll(ranges).Where(range => !range.IsPrivate()).ToList();
            if (risky.Count > 0)
                warnings.Add($"{surface}: {string.Join(", ", risky.Select(range => range.ToString()))}");
        }

        return warnings;
    }
}
