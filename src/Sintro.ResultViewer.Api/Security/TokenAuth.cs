using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Sintro.ResultViewer.Api;

namespace Sintro.ResultViewer.Security;

/// <summary>Bearer-token check for /api: any configured token or the viewer's session token, via <c>Authorization: Bearer</c> only.</summary>
public sealed class TokenAuth(
    RequestDelegate next,
    IOptions<SintroOptions> options,
    SessionToken sessionToken)
{
    // Digests, so FixedTimeEquals never returns early on a length mismatch and reveals a token's length.
    private readonly byte[][] _knownDigests =
        [.. options.Value.AllTokens.Select(Digest), Digest(sessionToken.Value)];

    public async Task InvokeAsync(HttpContext context)
    {
        if (RequiresToken(context) && !IsKnownToken(PresentedToken(context)))
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

    // Routing has run: WebApplication inserts UseRouting ahead of user middleware, so the endpoint is known here; without it the check fails closed.
    // /api is the one convention shared by every version; an endpoint opts out with AllowAnonymous.
    private static bool RequiresToken(HttpContext context) =>
        context.Request.Path.StartsWithSegments("/api") &&
        context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is null;

    private static string? PresentedToken(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return header["Bearer ".Length..].Trim();

        // Browsers cannot set headers on a WebSocket handshake; IsWebSocketRequest is meaningful only after UseWebSockets.
        if (context.WebSockets.IsWebSocketRequest &&
            context.GetEndpoint()?.Metadata.GetMetadata<QueryTokenOnUpgrade>() is not null &&
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
