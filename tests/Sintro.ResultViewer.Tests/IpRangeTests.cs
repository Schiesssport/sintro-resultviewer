using System.Net;
using Sintro.ResultViewer;
using Sintro.ResultViewer.Security;

namespace Sintro.ResultViewer.Tests;

public class IpRangeTests
{
    private static IpRange Parse(string text)
    {
        Assert.True(IpRange.TryParse(text, out var range), $"expected '{text}' to parse");
        return range;
    }

    [Theory]
    [InlineData("10.0.0.0/8", "10.4.5.6", true)]
    [InlineData("10.0.0.0/8", "11.4.5.6", false)]
    [InlineData("172.16.0.0/12", "172.17.0.2", true)]   // Docker's default bridge
    [InlineData("172.16.0.0/12", "172.32.0.1", false)]
    [InlineData("192.168.0.0/16", "192.168.1.50", true)]
    [InlineData("127.0.0.0/8", "127.0.0.1", true)]
    [InlineData("192.168.1.10/32", "192.168.1.10", true)]
    [InlineData("192.168.1.10/32", "192.168.1.11", false)]
    public void contains_matchesOnPrefixBits(string cidr, string address, bool expected) =>
        Assert.Equal(expected, Parse(cidr).Contains(IPAddress.Parse(address)));

    [Fact]
    public void wildcard_matchesEverything()
    {
        Assert.True(Parse("0.0.0.0/0").Contains(IPAddress.Parse("203.0.113.9")));
    }

    [Fact]
    public void ipv6Wildcard_matchesEverything()
    {
        var range = Parse("::/0");
        Assert.True(range.Contains(IPAddress.Parse("2001:db8::1")));
    }

    [Fact]
    public void ipv4MappedIpv6_matchesAnIpv4Range()
    {
        // Kestrel reports dual-stack clients as ::ffff:10.0.0.5.
        Assert.True(Parse("10.0.0.0/8").Contains(IPAddress.Parse("::ffff:10.0.0.5")));
    }

    [Fact]
    public void familiesDoNotCrossMatch()
    {
        Assert.False(Parse("10.0.0.0/8").Contains(IPAddress.Parse("2001:db8::1")));
        Assert.False(Parse("fc00::/7").Contains(IPAddress.Parse("10.0.0.1")));
    }

    [Theory]
    [InlineData("10.5.5.5/8", "10.0.0.0/8")]
    [InlineData("192.168.1.77/24", "192.168.1.0/24")]
    [InlineData("fd12:3456::1/7", "fc00::/7")]
    [InlineData("192.168.1.10", "192.168.1.10/32")]
    public void hostBitsAreClearedOnParse(string text, string expected)
    {
        // Otherwise IsPrivate would judge the host address rather than the block.
        Assert.Equal(expected, Parse(text).ToString());
        Assert.True(Parse(text).IsPrivate());
    }

    [Theory]
    [InlineData("fc00::/7", "fd00::1", true)]
    [InlineData("fc00::/7", "fe00::1", false)]
    [InlineData("2001:db8::/32", "2001:db8:ffff::1", true)]
    [InlineData("2001:db8::/32", "2001:db9::1", false)]
    public void ipv6PrefixesThatEndMidByteAreMatchedBitwise(string cidr, string address, bool expected) =>
        Assert.Equal(expected, Parse(cidr).Contains(IPAddress.Parse(address)));

    [Fact]
    public void bareAddress_isTreatedAsASingleHost() =>
        Assert.Equal(32, Parse("192.168.1.10").PrefixLength);

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("10.0.0.0/33")]
    [InlineData("10.0.0.0/-1")]
    [InlineData("::1/129")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("192.168.1/24")]   // IPAddress.TryParse would read this as 192.168.0.1
    [InlineData("10/8")]
    public void invalidInput_doesNotParse(string? text) =>
        Assert.False(IpRange.TryParse(text, out _));

    [Fact]
    public void nullAddress_neverMatches() =>
        Assert.False(Parse("0.0.0.0/0").Contains(null));

    [Theory]
    [InlineData("10.0.0.0/8", true)]
    [InlineData("192.168.4.0/24", true)]
    [InlineData("127.0.0.1/32", true)]
    [InlineData("fc00::/7", true)]
    [InlineData("0.0.0.0/0", false)]
    [InlineData("::/0", false)]
    [InlineData("203.0.113.0/24", false)]
    [InlineData("10.0.0.0/4", false)]     // wider than RFC1918, so it reaches public space
    public void isPrivate_identifiesRangesWorthWarningAbout(string cidr, bool expected) =>
        Assert.Equal(expected, Parse(cidr).IsPrivate());

    [Fact]
    public void thePrivateRangesProduceNoExposureWarning() =>
        Assert.Empty(NetworkGateExtensions.DescribePublicExposure(new SintroOptions
        {
            Network = new NetworkOptions
            {
                Api = NetworkOptions.PrivateSpace,
                Web = NetworkOptions.PrivateSpace,
            },
        }));

    [Fact]
    public void wildcardConfiguration_warnsPerSurface()
    {
        var options = new SintroOptions
        {
            Network = new NetworkOptions { Api = ["0.0.0.0/0"], Web = ["192.168.0.0/16"] },
        };

        var warnings = NetworkGateExtensions.DescribePublicExposure(options);

        Assert.Single(warnings);
        Assert.Contains("api", warnings[0]);
        Assert.Contains("0.0.0.0/0", warnings[0]);
    }

    [Fact]
    public void theDefinitionOfPrivateSpaceAllParses() =>
        Assert.Equal(
            NetworkOptions.PrivateSpace.Length,
            IpRange.ParseAll(NetworkOptions.PrivateSpace).Count);
}
