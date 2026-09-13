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
/// viewer's per-start session token. Comparison is constant-time so a wrong token leaks nothing through
/// timing, and every candidate is checked so the work does not depend on which one matched.
/// </summary>
public sealed class TokenAuth(
    RequestDelegate next,
    IOptions<SintroOptions> options,
    SessionToken sessionToken)
{
    private readonly byte[][] _readTokens =
    [
        .. options.Value.ReadTokens.Select(Encoding.UTF8.GetBytes),
        Encoding.UTF8.GetBytes(sessionToken.Value),
    ];

    private readonly byte[][] _writeTokens =
        [.. options.Value.WriteTokens.Select(Encoding.UTF8.GetBytes)];

    public async Task InvokeAsync(HttpContext context)
    {
        if (!RequiresToken(context.Request.Path))
        {
            await next(context);
            return;
        }

        var scope = ScopeOf(ReadPresentedToken(context));

        if (scope == ApiScope.None)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "unauthorized",
                detail = "Provide an API token as 'Authorization: Bearer <token>'.",
            });
            return;
        }

        // Recorded for the endpoints: nothing writes yet, but when something does it reads the
        // scope from here rather than re-deriving it.
        context.Items[ScopeKey] = scope;

        await next(context);
    }

    public const string ScopeKey = "sintro.scope";

    /// <summary>The scope granted to the current request, or None outside an authenticated one.</summary>
    public static ApiScope ScopeFor(HttpContext context) =>
        context.Items.TryGetValue(ScopeKey, out var value) && value is ApiScope scope
            ? scope
            : ApiScope.None;

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
        // the live endpoint — also accepts the token as a query parameter. Restricting it by path
        // keeps tokens out of query strings (and therefore out of logs) elsewhere.
        if (context.Request.Path.StartsWithSegments(LivePath) &&
            context.Request.Query.TryGetValue("token", out var query))
            return query[0];

        return null;
    }

    private static readonly PathString LivePath = V2Endpoints.RoutePrefix + "/live";
    private static readonly PathString HealthPath = V2Endpoints.RoutePrefix + "/health";

    private ApiScope ScopeOf(string? presented)
    {
        if (string.IsNullOrEmpty(presented)) return ApiScope.None;

        var candidate = Encoding.UTF8.GetBytes(presented);

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
}

public static class TokenAuthExtensions
{
    public static IApplicationBuilder UseTokenAuth(this IApplicationBuilder app) =>
        app.UseMiddleware<TokenAuth>();
}
