using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Sintro.ResultViewer.Api.V2;

namespace Sintro.ResultViewer.Security;

/// <summary>What a token is allowed to do. Write implies read.</summary>
public enum ApiScope
{
    None = 0,
    Read = 1,
    Write = 2,
}

/// <summary>
/// Bearer-token check for /api: <c>Authorization: Bearer &lt;token&gt;</c> and nothing else, so
/// there is exactly one documented way in. Accepts any configured read or write token, plus the
/// viewer's per-start session token.
///
/// Tokens are compared as SHA-256 digests in constant time. Hashing first is what makes the
/// comparison genuinely length-independent: FixedTimeEquals returns at once on unequal lengths,
/// which would otherwise reveal how long each configured token is. Every candidate is checked so
/// the work does not depend on which one matched.
/// </summary>
public sealed class TokenAuth(
    RequestDelegate next,
    IOptions<SintroOptions> options,
    SessionToken sessionToken)
{
    private readonly byte[][] _readTokens =
    [
        .. options.Value.ReadTokens.Select(Digest),
        Digest(sessionToken.Value),
    ];

    private readonly byte[][] _writeTokens =
        [.. options.Value.WriteTokens.Select(Digest)];

    public async Task InvokeAsync(HttpContext context)
    {
        if (!RequiresToken(context.Request.Path))
        {
            await next(context);
            return;
        }

        if (ScopeOf(ReadPresentedToken(context)) == ApiScope.None)
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

    /// <summary>
    /// Only /api needs a token, and /health is exempt so monitoring works. The OpenAPI document
    /// is deliberately open: it is schema, contains no shooter data, and the docs page has to be
    /// able to load it — the network gate still guards both.
    /// </summary>
    private static bool RequiresToken(PathString path) =>
        path.StartsWithSegments("/api") && !path.StartsWithSegments(HealthPath);

    private static string? ReadPresentedToken(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return header["Bearer ".Length..].Trim();

        // Browsers cannot set headers on a WebSocket handshake, so the live endpoint — and only
        // an actual upgrade request to it — also accepts the token as a query parameter. A plain
        // GET is held to the header like everywhere else, which keeps tokens out of query strings
        // (and therefore out of logs). IsWebSocketRequest is only meaningful after UseWebSockets
        // has run, which is why that middleware must precede this one.
        if (context.WebSockets.IsWebSocketRequest &&
            context.Request.Path.StartsWithSegments(LivePath) &&
            context.Request.Query.TryGetValue("token", out var query))
            return query[0];

        return null;
    }

    private static readonly PathString LivePath = V2Endpoints.RoutePrefix + "/live";
    private static readonly PathString HealthPath = V2Endpoints.RoutePrefix + "/health";

    private ApiScope ScopeOf(string? presented)
    {
        if (string.IsNullOrEmpty(presented)) return ApiScope.None;

        var candidate = Digest(presented);

        // Both lists are always walked in full: an early return would make "matched the first
        // write token" measurably faster than "matched the last read token".
        var write = MatchesAny(_writeTokens, candidate);
        var read = MatchesAny(_readTokens, candidate);

        if (write) return ApiScope.Write;
        return read ? ApiScope.Read : ApiScope.None;
    }

    private static bool MatchesAny(byte[][] known, byte[] candidate)
    {
        var matched = false;
        foreach (var token in known)
            matched |= CryptographicOperations.FixedTimeEquals(candidate, token);

        return matched;
    }

    private static byte[] Digest(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}

public static class TokenAuthExtensions
{
    public static IApplicationBuilder UseTokenAuth(this IApplicationBuilder app) =>
        app.UseMiddleware<TokenAuth>();
}
