using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Sintro.ResultViewer.Api.V2;

namespace Sintro.ResultViewer.Security;

/// <summary>Bearer-token check for /api: any configured token or the viewer's session token, via <c>Authorization: Bearer</c> only.</summary>
public sealed class TokenAuth(
    RequestDelegate next,
    IOptions<SintroOptions> options,
    SessionToken sessionToken)
{
    private static readonly PathString LivePath = V2Endpoints.RoutePrefix + "/live";
    private static readonly PathString HealthPath = V2Endpoints.RoutePrefix + "/health";

    // Digests, so FixedTimeEquals never returns early on a length mismatch and reveals a token's length.
    private readonly byte[][] _knownDigests =
        [.. options.Value.AllTokens.Select(Digest), Digest(sessionToken.Value)];

    public async Task InvokeAsync(HttpContext context)
    {
        if (RequiresToken(context.Request.Path) && !IsKnownToken(PresentedToken(context)))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer";
            await context.Response.WriteAsJsonAsync(new ApiError(
                "unauthorized",
                "Provide an API token as 'Authorization: Bearer <token>'."));
            return;
        }

        await next(context);
    }

    // /health stays open for monitoring; the network gate still guards it.
    private static bool RequiresToken(PathString path) =>
        path.StartsWithSegments("/api") && !path.StartsWithSegments(HealthPath);

    private static string? PresentedToken(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return header["Bearer ".Length..].Trim();

        // Browsers cannot set headers on a WebSocket handshake; ?token= is accepted on a genuine upgrade only,
        // and IsWebSocketRequest is meaningful only after UseWebSockets has run.
        if (context.WebSockets.IsWebSocketRequest &&
            context.Request.Path.StartsWithSegments(LivePath) &&
            context.Request.Query.TryGetValue("token", out var query))
            return query[0];

        return null;
    }

    // Every digest is compared, so the work does not depend on which one matched.
    private bool IsKnownToken(string? presented)
    {
        if (string.IsNullOrEmpty(presented)) return false;

        var candidate = Digest(presented);
        var matched = false;
        foreach (var known in _knownDigests)
            matched |= CryptographicOperations.FixedTimeEquals(candidate, known);

        return matched;
    }

    private static byte[] Digest(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}

public static class TokenAuthExtensions
{
    public static IApplicationBuilder UseTokenAuth(this IApplicationBuilder app) =>
        app.UseMiddleware<TokenAuth>();
}
