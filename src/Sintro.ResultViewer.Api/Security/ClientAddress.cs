using System.Net;

namespace Sintro.ResultViewer.Security;

/// <summary>Resolves the real client behind X-Forwarded-For, unwinding only hops that are trusted proxies.</summary>
public static class ClientAddress
{
    // Null means a trusted proxy forwarded a hop that does not parse: the caller must refuse, or a public proxy admits anyone.
    public static IPAddress? Resolve(IPAddress? peer, string? forwardedFor, IReadOnlyList<IpRange> trustedProxies)
    {
        // No remote address means an in-process or Unix-socket transport, local by construction.
        var socketPeer = peer ?? IPAddress.Loopback;
        if (!IsTrusted(socketPeer, trustedProxies)) return socketPeer;

        var chain = (forwardedFor ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Parse)
            .ToList();
        if (chain.Count == 0) return socketPeer;

        // Nearest hop first: a hop is believed only because the trusted hop after it reported it.
        for (var index = chain.Count - 1; index >= 0; index--)
        {
            var hop = chain[index];
            if (hop is null || !IsTrusted(hop, trustedProxies)) return hop;
        }

        return chain[0];
    }

    private static bool IsTrusted(IPAddress address, IReadOnlyList<IpRange> trustedProxies) =>
        trustedProxies.Any(range => range.Contains(address));

    // A port may be appended, and IPv6 then arrives bracketed: [2001:db8::1]:443
    private static IPAddress? Parse(string text)
    {
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
