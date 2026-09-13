using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Options;
using Sintro.ResultViewer.Api.V2;

namespace Sintro.ResultViewer.Security;

/// <summary>Source-address allowlist, run before authentication so a refused network never gets to guess tokens.</summary>
public sealed class NetworkGate(RequestDelegate next, IOptions<SintroOptions> options, ILogger<NetworkGate> logger)
{
    private enum Surface { Api, Web }

    private const int MaxReportedSources = 1000;

    private readonly IReadOnlyList<IpRange> _api = IpRange.ParseAll(options.Value.Network.Api ?? []);
    private readonly IReadOnlyList<IpRange> _web = IpRange.ParseAll(options.Value.Network.Web ?? []);
    private readonly IReadOnlyList<IpRange> _trustedProxies =
        IpRange.ParseAll(options.Value.TrustedProxies ?? []);

    // A port scan would otherwise scroll the address banner out of the operator's window; bounded so it cannot grow forever.
    private readonly ConcurrentDictionary<string, byte> _reportedSources = new();

    public async Task InvokeAsync(HttpContext context)
    {
        // /openapi is web, not api: it is schema, and the docs page must load it even when the api list is narrower.
        var surface = context.Request.Path.StartsWithSegments("/api") ? Surface.Api : Surface.Web;
        var allowed = surface == Surface.Api ? _api : _web;

        var source = ClientAddress.Resolve(
            context.Connection.RemoteIpAddress,
            context.Request.Headers["X-Forwarded-For"].ToString(),
            _trustedProxies);

        if (source is not null && allowed.Any(range => range.Contains(source)))
        {
            await next(context);
            return;
        }

        LogRejection(surface, source, context.Request.Path);
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new ApiError(
            "forbidden_network",
            source is null
                ? "The forwarded client address could not be read, so the request is refused."
                : $"Source address is not in the allowed {surface} ranges."));
    }

    private void LogRejection(Surface surface, IPAddress? source, PathString path)
    {
        var key = source?.ToString() ?? "unresolvable";

        if (_reportedSources.Count < MaxReportedSources && _reportedSources.TryAdd(key, 0))
            logger.LogWarning("Blocked {Surface} request from {Source} for {Path} (further requests from it are logged at debug level)",
                surface, key, path);
        else
            logger.LogDebug("Blocked {Surface} request from {Source} for {Path}", surface, key, path);
    }
}

public static class NetworkGateExtensions
{
    public static IApplicationBuilder UseNetworkGate(this IApplicationBuilder app) =>
        app.UseMiddleware<NetworkGate>();

    /// <summary>Configured ranges outside private space, per surface. Allowed, but surfaced at startup and in /health.</summary>
    public static IReadOnlyList<string> DescribePublicExposure(SintroOptions options)
    {
        var warnings = new List<string>();

        foreach (var (surface, ranges) in new[] { ("api", options.Network.Api ?? []), ("web", options.Network.Web ?? []) })
        {
            var risky = IpRange.ParseAll(ranges).Where(range => !range.IsPrivate()).ToList();
            if (risky.Count > 0) warnings.Add($"{surface}: {string.Join(", ", risky)}");
        }

        return warnings;
    }
}
