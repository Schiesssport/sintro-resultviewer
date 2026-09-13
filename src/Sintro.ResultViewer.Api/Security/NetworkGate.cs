using Microsoft.Extensions.Options;

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

    public async Task InvokeAsync(HttpContext context)
    {
        var surface = ClassifySurface(context.Request.Path);
        var allowed = surface == Surface.Api ? _api : _web;

        var source = ClientAddress.Resolve(
            context.Connection.RemoteIpAddress,
            context.Request.Headers["X-Forwarded-For"].ToString(),
            _trustedProxies);

        if (!allowed.Any(range => range.Contains(source)))
        {
            logger.LogWarning("Blocked {Surface} request from {Source} for {Path}",
                surface, source, context.Request.Path);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "forbidden_network",
                detail = $"Source address is not in the allowed {surface} ranges.",
            });
            return;
        }

        await next(context);
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
