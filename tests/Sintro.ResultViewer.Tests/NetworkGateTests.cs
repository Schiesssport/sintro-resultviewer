using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Sintro.ResultViewer.Data;

namespace Sintro.ResultViewer.Tests;

/// <summary>Boots the API with an allowlist that excludes the test client's own address, so the refusal path runs for real.</summary>
public sealed class GatedApiFixture : WebApplicationFactory<SintroRepository>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Sintro", "Server=localhost,11433;Database=DBSINTRO300;User Id=sa;Password=Sintro_Dev_2026!;TrustServerCertificate=true;Encrypt=false");
        builder.UseSetting("Sintro:ApiReadTokens:0", ApiFixture.Token);
        builder.UseSetting("Sintro:ReferenceDate", ApiFixture.BackupDate);
        builder.UseSetting("Sintro:Live:PollMilliseconds", "600000");
        // 10.1.2.0/24 contains neither loopback nor any in-process address.
        builder.UseSetting("Sintro:Network:Api:0", "10.1.2.0/24");
        builder.UseSetting("Sintro:Network:Web:0", "10.1.2.0/24");
    }
}

public class NetworkGateTests : IClassFixture<GatedApiFixture>
{
    private readonly GatedApiFixture _fixture;

    public NetworkGateTests(GatedApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task aDisallowedSource_is403()
    {
        var response = await _fixture.CreateClient().GetAsync("/api/v2/live");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task theGateRunsBeforeAuthentication()
    {
        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFixture.Token);

        var response = await client.GetAsync("/api/v2/live");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task theGateAlsoCoversHealthWhichNeedsNoToken()
    {
        Assert.Equal(HttpStatusCode.Forbidden,
            (await _fixture.CreateClient().GetAsync("/api/v2/health")).StatusCode);
    }

    [Fact]
    public async Task theWebSurfaceIsGatedIndependently()
    {
        Assert.Equal(HttpStatusCode.Forbidden,
            (await _fixture.CreateClient().GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task aForgedForwardedHeaderDoesNotBypassTheGate()
    {
        // No trusted proxies configured, so X-Forwarded-For must be ignored entirely.
        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "10.1.2.50");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v2/live")).StatusCode);
    }
}

/// <summary>Same setup, but with the socket peer listed as a trusted proxy.</summary>
public sealed class TrustedProxyApiFixture : WebApplicationFactory<SintroRepository>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Sintro", "Server=localhost,11433;Database=DBSINTRO300;User Id=sa;Password=Sintro_Dev_2026!;TrustServerCertificate=true;Encrypt=false");
        builder.UseSetting("Sintro:ApiReadTokens:0", ApiFixture.Token);
        builder.UseSetting("Sintro:ReferenceDate", ApiFixture.BackupDate);
        builder.UseSetting("Sintro:Live:PollMilliseconds", "600000");
        builder.UseSetting("Sintro:TrustedProxies:0", "127.0.0.0/8");
        builder.UseSetting("Sintro:TrustedProxies:1", "172.20.0.0/16");
        builder.UseSetting("Sintro:Network:Api:0", "10.1.2.0/24");
        builder.UseSetting("Sintro:Network:Web:0", "10.1.2.0/24");
    }
}

public class TrustedProxyTests(TrustedProxyApiFixture fixture) : IClassFixture<TrustedProxyApiFixture>
{
    [Fact]
    public async Task withATrustedProxy_theForwardedAddressIsUsed()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "10.1.2.50");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFixture.Token);

        var response = await client.GetAsync("/api/v2/live");
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task withATrustedProxy_aDisallowedForwardedAddressIsStillRefused()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.7");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v2/live")).StatusCode);
    }

    [Fact]
    public async Task aChainIsUnwoundThroughEveryTrustedHop()
    {
        // client 10.1.2.50 → proxy 172.20.0.1 (trusted) → us.
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "10.1.2.50, 172.20.0.1");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFixture.Token);

        Assert.NotEqual(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v2/live")).StatusCode);
    }

    [Fact]
    public async Task anUnreadableForwardedHopBehindOurProxyIsRefused()
    {
        // nginx appends the real peer to whatever the client sent, so "unknown, <attacker>" is what arrives.
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "unknown");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFixture.Token);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v2/live")).StatusCode);
    }

    [Theory]
    [InlineData("/openapi/v2.json")]
    [InlineData("/styles.css")]
    public async Task theTokenFreeWebSurfaceIsStillGated(string path)
    {
        Assert.Equal(HttpStatusCode.Forbidden,
            (await fixture.CreateClient().GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task unwindingStopsAtTheFirstHopWeDoNotTrust()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "10.1.2.50, 203.0.113.7");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v2/live")).StatusCode);
    }
}
