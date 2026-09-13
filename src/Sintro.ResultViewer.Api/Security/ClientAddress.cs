using System.Net;

namespace Sintro.ResultViewer.Security;

/// <summary>
/// Works out who is really calling when a reverse proxy sits in front. Pure, so the awkward
/// cases are testable without a server.
/// </summary>
public static class ClientAddress
{
    /// <summary>
    /// Resolves the client address from the socket peer and any <c>X-Forwarded-For</c> chain.
    ///
    /// A simple "trust the header" switch is not safe: anyone able to reach the port could then
    /// claim a private source address and walk through the network gate. So only hops that are
    /// themselves trusted proxies are unwound — the chain is read from the nearest hop outwards
    /// and the first address that is not a known proxy is the client. With nothing configured the
    /// header is ignored entirely and the socket peer stands.
    /// </summary>
    public static IPAddress Resolve(
        IPAddress? peer,
        string? forwardedFor,
        IReadOnlyList<IpRange> trustedProxies)
    {
        // No remote address means the request did not arrive over a network socket — an
        // in-process or Unix-socket transport, both local by construction.
        var socketPeer = peer ?? IPAddress.Loopback;

        if (trustedProxies.Count == 0) return socketPeer;
        if (!IsTrusted(socketPeer, trustedProxies)) return socketPeer;

        // Left to right: the original client first, then each proxy it passed through.
        var chain = (forwardedFor ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Parse)
            .ToList();

        if (chain.Count == 0 || chain.Any(address => address is null)) return socketPeer;

        for (var index = chain.Count - 1; index >= 0; index--)
        {
            if (!IsTrusted(chain[index]!, trustedProxies)) return chain[index]!;
        }

        // Every hop is a proxy we trust; the outermost is the closest thing to a client.
        return chain[0]!;
    }

    private static bool IsTrusted(IPAddress address, IReadOnlyList<IpRange> trustedProxies) =>
        trustedProxies.Any(range => range.Contains(address));

    private static IPAddress? Parse(string value)
    {
        // A port may be appended, and IPv6 arrives bracketed: [2001:db8::1]:443
        var text = value.Trim();

        if (text.StartsWith('['))
        {
            var close = text.IndexOf(']');
            if (close > 0) text = text[1..close];
        }
        else if (text.Count(character => character == ':') == 1)
        {
            text = text[..text.IndexOf(':')];
        }

        return IPAddress.TryParse(text, out var parsed) ? parsed : null;
    }
}
