using System.Net;
using Sintro.ResultViewer.Security;

namespace Sintro.ResultViewer.Tests;

public class ClientAddressTests
{
    private static readonly IReadOnlyList<IpRange> NoProxies = [];
    private static IReadOnlyList<IpRange> Proxies(params string[] cidrs) => IpRange.ParseAll(cidrs);

    private static string? Resolve(string? peer, string? forwarded, params string[] trusted) =>
        ClientAddress.Resolve(
            peer is null ? null : IPAddress.Parse(peer),
            forwarded,
            trusted.Length == 0 ? NoProxies : Proxies(trusted))?.ToString();

    [Fact]
    public void withoutTrustedProxies_theSocketPeerDecides()
    {
        Assert.Equal("203.0.113.9", Resolve("203.0.113.9", "10.0.0.5"));
    }

    [Fact]
    public void aHeaderFromAnUntrustedPeerIsIgnored()
    {
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
        // client → outer proxy (untrusted) → our proxy; trust stops at the outer one.
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
        Assert.Equal("203.0.113.9",
            Resolve("203.0.113.9", "192.168.1.50", "192.168.1.0/24"));
    }

    [Fact]
    public void anEmptyHeaderFromATrustedProxyMeansTheProxyIsTheClient()
    {
        Assert.Equal("192.168.1.10", Resolve("192.168.1.10", "", "192.168.1.0/24"));
        Assert.Equal("192.168.1.10", Resolve("192.168.1.10", null, "192.168.1.0/24"));
    }

    [Fact]
    public void aGarbledHopBehindATrustedProxyCannotBeNamed_soNobodyIsLetIn()
    {
        // Falling back to the proxy's own (allowed) address would admit anyone who sends "X-Forwarded-For: unknown" through it.
        Assert.Null(Resolve("192.168.1.10", "not-an-address", "192.168.1.0/24"));
        Assert.Null(Resolve("192.168.1.10", "203.0.113.9, junk", "192.168.1.0/24"));
    }

    [Fact]
    public void aGarbledHopBeyondAnUntrustedOneIsNeverReached()
    {
        Assert.Equal("198.51.100.7",
            Resolve("192.168.1.10", "junk, 198.51.100.7", "192.168.1.0/24"));
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
        Assert.Equal("127.0.0.1", Resolve(null, null));
    }
}
