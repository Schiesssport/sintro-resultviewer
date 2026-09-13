using System.Net;
using System.Net.Sockets;

namespace Sintro.ResultViewer.Security;

/// <summary>A CIDR block, matched bitwise; IPv4-mapped IPv6 sources (::ffff:10.0.0.1) match IPv4 blocks.</summary>
public sealed class IpRange
{
    private static readonly Lazy<IReadOnlyList<IpRange>> PrivateSpace =
        new(() => ParseAll(NetworkOptions.PrivateSpace));

    private readonly byte[] _network;
    private readonly AddressFamily _family;

    private IpRange(byte[] network, AddressFamily family, int prefixLength)
    {
        _network = network;
        _family = family;
        PrefixLength = prefixLength;
    }

    public IPAddress NetworkAddress => new(_network);
    public int PrefixLength { get; }

    public static bool TryParse(string? text, out IpRange range)
    {
        range = null!;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Trim().Split('/', 2);
        if (!IPAddress.TryParse(parts[0], out var address)) return false;

        // Full quads only: IPAddress.TryParse reads "192.168.1" as 192.168.0.1, turning a typo into a different subnet.
        if (address.AddressFamily == AddressFamily.InterNetwork &&
            parts[0].Count(character => character == '.') != 3)
            return false;

        var maximum = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        var prefix = maximum;
        if (parts.Length == 2 && (!int.TryParse(parts[1], out prefix) || prefix < 0 || prefix > maximum))
            return false;

        range = new IpRange(Mask(address.GetAddressBytes(), prefix), address.AddressFamily, prefix);
        return true;
    }

    public static IReadOnlyList<IpRange> ParseAll(IEnumerable<string> texts) =>
        texts.Select(text => TryParse(text, out var range) ? range : null)
             .OfType<IpRange>()
             .ToList();

    public bool Contains(IPAddress? candidate)
    {
        if (candidate is null) return false;

        if (candidate.IsIPv4MappedToIPv6 && _family == AddressFamily.InterNetwork)
            candidate = candidate.MapToIPv4();

        if (candidate.AddressFamily != _family) return false;

        return Mask(candidate.GetAddressBytes(), PrefixLength).AsSpan().SequenceEqual(_network);
    }

    /// <summary>Wholly inside loopback, RFC1918 or link-local space. Decides whether to warn, never whether to allow.</summary>
    public bool IsPrivate() =>
        PrivateSpace.Value.Any(known =>
            known.Contains(NetworkAddress) && PrefixLength >= known.PrefixLength);

    private static byte[] Mask(byte[] address, int prefixLength)
    {
        var masked = (byte[])address.Clone();
        for (var bit = prefixLength; bit < masked.Length * 8; bit++)
            masked[bit / 8] &= (byte)~(0x80 >> (bit % 8));
        return masked;
    }

    public override string ToString() => $"{NetworkAddress}/{PrefixLength}";
}
