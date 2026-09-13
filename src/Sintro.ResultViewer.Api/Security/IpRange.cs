using System.Net;
using System.Net.Sockets;

namespace Sintro.ResultViewer.Security;

/// <summary>A CIDR block, matched bitwise. Handles IPv4-mapped IPv6 sources (::ffff:10.0.0.1).</summary>
public sealed class IpRange
{
    private readonly byte[] _network;

    private IpRange(byte[] network, AddressFamily family, int prefixLength)
    {
        _network = network;
        Family = family;
        PrefixLength = prefixLength;
    }

    /// <summary>The block's first address, host bits cleared: "10.5.5.5/8" is reported as 10.0.0.0/8.</summary>
    public IPAddress NetworkAddress => new(_network);
    public int PrefixLength { get; }
    private AddressFamily Family { get; }

    public static bool TryParse(string? text, out IpRange range)
    {
        range = null!;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Trim().Split('/', 2);
        if (!IPAddress.TryParse(parts[0], out var address)) return false;

        // IPAddress.TryParse accepts legacy shorthand ("192.168.1" parses as 192.168.0.1),
        // which would turn a typo'd CIDR into a silently different subnet. Full quads only.
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

        if (candidate.IsIPv4MappedToIPv6 && Family == AddressFamily.InterNetwork)
            candidate = candidate.MapToIPv4();

        if (candidate.AddressFamily != Family) return false;

        return Mask(candidate.GetAddressBytes(), PrefixLength).AsSpan().SequenceEqual(_network);
    }

    /// <summary>
    /// Whether this block sits wholly inside RFC1918/loopback/link-local space. Used only to
    /// decide whether to warn the operator, never to allow or deny.
    /// </summary>
    public bool IsPrivate() =>
        PrivateSpace.Value.Any(known =>
            known.Contains(NetworkAddress) && PrefixLength >= known.PrefixLength);

    private static readonly Lazy<IReadOnlyList<IpRange>> PrivateSpace =
        new(() => ParseAll(NetworkOptions.PrivateSpace));

    private static byte[] Mask(byte[] address, int prefixLength)
    {
        var masked = (byte[])address.Clone();
        for (var bit = prefixLength; bit < masked.Length * 8; bit++)
            masked[bit / 8] &= (byte)~(0x80 >> (bit % 8));
        return masked;
    }

    public override string ToString() => $"{NetworkAddress}/{PrefixLength}";
}
