using System.Net;
using Sintro.ResultViewer.Security;

namespace Sintro.ResultViewer.Tests;

public class ClientAddressTests
{
    private static readonly IReadOnlyList<IpRange> NoProxies = [];
    private static IReadOnlyList<IpRange> Proxies(params string[] cidrs) => IpRange.ParseAll(cidrs);

    private static string Resolve(string? peer, string? forwarded, params string[] trusted) =>
        ClientAddress.Resolve(
            peer is null ? null : IPAddress.Parse(peer),
            forwarded,
            trusted.Length == 0 ? NoProxies : Proxies(trusted)).ToString();

    [Fact]
    public void withoutTrustedProxies_theSocketPeerDecides()
    {
        // The default. Anyone able to reach the port could otherwise claim any address.
        Assert.Equal("203.0.113.9", Resolve("203.0.113.9", "10.0.0.5"));
    }

    [Fact]
    public void aHeaderFromAnUntrustedPeerIsIgnored()
    {
        // The peer is not a listed proxy, so its claim about the client carries no weight.
        Assert.Equal("203.0.113.9", Resolve("203.0.113.9", "10.0.0.5", "192.168.1.10/32"));
    }

    [Fact]
    public void aTrustedProxyHandsOverItsClient()
    {
        Assert.Equal("203.0.113.9", Resolve("192.168.1.10", "203.0.113.9", "192.168.1.0/24"));
    }

    [Fact]
    public void aChainIsUnwoundOnlyThroughTrustedHops()
    {
        // client → outer proxy (untrusted) → our proxy. The outer one is where trust stops,
        // so it is the furthest we can believe.
        Assert.Equal("198.51.100.7",
            Resolve("192.168.1.10", "203.0.113.9, 198.51.100.7", "192.168.1.0/24"));
    }

    [Fact]
    public void severalTrustedHopsAreAllUnwound()
    {
        Assert.Equal("203.0.113.9",
            Resolve("192.168.1.10", "203.0.113.9, 192.168.1.20, 192.168.1.30", "192.168.1.0/24"));
    }

    [Fact]
    public void whenEveryHopIsTrustedTheOutermostIsTheBestGuess()
    {
        Assert.Equal("192.168.1.5",
            Resolve("192.168.1.10", "192.168.1.5, 192.168.1.20", "192.168.1.0/24"));
    }

    [Fact]
    public void aForgedHeaderCannotPromoteAnOutsiderToTheLan()
    {
        // The whole point: an internet client hitting a forwarded port claims a private
        // address. Its peer is not a trusted proxy, so the claim is discarded and the gate
        // still sees a public address.
        Assert.Equal("203.0.113.9",
            Resolve("203.0.113.9", "192.168.1.50", "192.168.1.0/24"));
    }

    [Fact]
    public void aGarbledHeaderFallsBackToThePeerRatherThanGuessing()
    {
        Assert.Equal("192.168.1.10", Resolve("192.168.1.10", "not-an-address", "192.168.1.0/24"));
        Assert.Equal("192.168.1.10", Resolve("192.168.1.10", "", "192.168.1.0/24"));
        Assert.Equal("192.168.1.10", Resolve("192.168.1.10", null, "192.168.1.0/24"));
    }

    [Fact]
    public void oneBadEntryDiscreditsTheWholeChain()
    {
        // A chain we cannot fully parse cannot be safely unwound.
        Assert.Equal("192.168.1.10",
            Resolve("192.168.1.10", "203.0.113.9, junk", "192.168.1.0/24"));
    }

    [Fact]
    public void portsAreStripped()
    {
        Assert.Equal("203.0.113.9",
            Resolve("192.168.1.10", "203.0.113.9:51514", "192.168.1.0/24"));
    }

    [Fact]
    public void bracketedIpv6IsUnderstood()
    {
        Assert.Equal("2001:db8::1",
            Resolve("192.168.1.10", "[2001:db8::1]:443", "192.168.1.0/24"));
    }

    [Fact]
    public void bareIpv6WithoutAPortIsNotMistakenForHostColonPort()
    {
        Assert.Equal("2001:db8::1",
            Resolve("192.168.1.10", "2001:db8::1", "192.168.1.0/24"));
    }

    [Fact]
    public void aMissingPeerCountsAsLoopback()
    {
        // No remote address means an in-process or Unix-socket transport, both local.
        Assert.Equal("127.0.0.1", Resolve(null, null));
    }
}
