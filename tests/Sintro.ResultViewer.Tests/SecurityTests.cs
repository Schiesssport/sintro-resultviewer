using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Sintro.ResultViewer.Tests;

[Collection(ApiCollection.Name)]
public class SecurityTests(ApiFixture fixture)
{
    [Fact]
    public async Task requestWithoutAToken_is401()
    {
        var response = await fixture.CreateClient().GetAsync("/api/v2/live");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task requestWithTheWrongToken_is401()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-token-value");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v2/live")).StatusCode);
    }

    [Fact]
    public async Task theBearerHeaderIsTheOnlyWayIn()
    {
        var bearer = fixture.CreateClient();
        bearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFixture.Token);
        Assert.NotEqual(HttpStatusCode.Unauthorized, (await bearer.GetAsync("/api/v2/live")).StatusCode);

        // There used to be an undocumented X-Api-Token path. One documented way in is easier to
        // reason about than two, and an undocumented one survives by accident.
        var custom = fixture.CreateClient();
        custom.DefaultRequestHeaders.Add("X-Api-Token", ApiFixture.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await custom.GetAsync("/api/v2/live")).StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task healthNeedsNoToken_soMonitoringCanReachIt()
    {
        var response = await fixture.CreateClient().GetAsync("/api/v2/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task theViewerIsServedWithItsTokenSubstituted()
    {
        var html = await fixture.CreateClient().GetStringAsync("/");

        Assert.Contains("window.SINTRO_TOKEN", html);
        // The placeholder must have been replaced, or the viewer cannot call the API.
        Assert.DoesNotContain("{{SESSION_TOKEN}}", html);
    }

    [Fact]
    public async Task theRawTemplateIsNeverServedAsAStaticFile()
    {
        // /index.html must go through the renderer too, not the static file handler.
        var html = await fixture.CreateClient().GetStringAsync("/index.html");
        Assert.DoesNotContain("{{SESSION_TOKEN}}", html);
    }

    [Theory]
    [InlineData("/fullscreen")]
    [InlineData("/fullscreen/live")]
    [InlineData("/fullscreen/results")]
    [InlineData("/fullscreen/leaderboard")]
    [InlineData("/fullscreen/live+results")]
    [InlineData("/fullscreen/anything-else")]
    public async Task everyFullscreenVariantServesTheViewer(string path)
    {
        // These are client-side routes; the server must hand out the same page for all of
        // them, including ones it has never heard of, or a bookmarked TV shows a 404.
        var response = await fixture.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("{{SESSION_TOKEN}}", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/fullscreen/live")]
    [InlineData("/fullscreen/live+results")]
    public async Task fullscreenPagesReferenceAssetsAbsolutely(string path)
    {
        // Regression guard: a relative src="app.js" on /fullscreen/live resolves to
        // /fullscreen/app.js, which the SPA catch-all answers with HTML — and the browser
        // then fails to parse HTML as a module, breaking every fullscreen view.
        var html = await fixture.CreateClient().GetStringAsync(path);

        Assert.Contains("src=\"/app.js\"", html);
        Assert.Contains("href=\"/styles.css\"", html);
        Assert.DoesNotContain("src=\"app.js\"", html);
        Assert.DoesNotContain("href=\"styles.css\"", html);
    }

    [Fact]
    public async Task theViewerTokenDiffersFromTheConfiguredToken()
    {
        var html = await fixture.CreateClient().GetStringAsync("/");
        Assert.DoesNotContain(ApiFixture.Token, html);
    }

    [Fact]
    public async Task theViewerSessionTokenWorksAgainstTheApi()
    {
        var html = await fixture.CreateClient().GetStringAsync("/");
        var token = System.Text.RegularExpressions.Regex
            .Match(html, @"window\.SINTRO_TOKEN\s*=\s*""([^""]+)""").Groups[1].Value;

        Assert.NotEmpty(token);

        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.NotEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v2/live")).StatusCode);
    }

    [Fact]
    public async Task staticAssetsAreServedWithoutAToken()
    {
        // The web surface is guarded by the network gate, not by the API token.
        Assert.Equal(HttpStatusCode.OK, (await fixture.CreateClient().GetAsync("/styles.css")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.CreateClient().GetAsync("/app.js")).StatusCode);
    }

    [Fact]
    public async Task theOpenApiDocumentNeedsNoToken()
    {
        // It is schema, not shooter data, and the docs page has to load it. The network
        // gate still guards it.
        var response = await fixture.CreateClient().GetAsync("/openapi/v2.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task theOpenApiDocumentExposesNoShooterData()
    {
        // The spec is served without a token, so a real name or licence slipped into an
        // endpoint description would be published to anyone who can reach the web surface.
        // Checked against the loaded export rather than a hard-coded name, so it keeps
        // working — and keeps protecting — whatever data a contributor has.
        var spec = await fixture.CreateClient().GetStringAsync("/openapi/v2.json");
        Assert.Contains("/api/v2/programs", spec);

        var shooters = await fixture.CreateAuthorizedClient()
            .GetFromJsonAsync<Api.V2.CursorPage<Domain.Shooter>>(
                "/api/v2/shooters?limit=50", TestJson.Options);

        foreach (var shooter in shooters?.Items ?? [])
        {
            Assert.DoesNotContain(shooter.LastName, spec, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(shooter.License, spec, StringComparison.Ordinal);
        }
    }

    [RequiresDatabaseFact]
    public async Task theLiveEndpointServesLaneStateOverPlainHttp()
    {
        // Same URL: plain GET returns the current snapshot, an upgrade gets the push feed.
        var response = await fixture.CreateAuthorizedClient().GetAsync("/api/v2/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

public class StartupCheckTests
{
    /// <summary>
    /// Runs the startup checks with a workable allowlist unless the test set one, so a test
    /// about tokens fails for token reasons.
    /// </summary>
    private static void Check(SintroOptions options)
    {
        options.Network.Api ??= NetworkOptions.PrivateSpace;
        options.Network.Web ??= NetworkOptions.PrivateSpace;

        StartupChecks.Run(
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            options,
            new Security.SessionToken());
    }

    [Theory]
    [InlineData("Api")]
    [InlineData("Web")]
    public void anEmptyAllowlist_refusesToStart(string surface)
    {
        // Denying everything silently is as unhelpful as allowing everything silently: nobody,
        // including the machine it runs on, could reach it, and nothing would say why.
        var options = new SintroOptions
        {
            Network = surface == "Api"
                ? new NetworkOptions { Api = [], Web = NetworkOptions.PrivateSpace }
                : new NetworkOptions { Api = NetworkOptions.PrivateSpace, Web = [] },
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            StartupChecks.Run(
                Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
                options,
                new Security.SessionToken()));

        Assert.Contains($"Sintro:Network:{surface}", error.Message);
        // The message must show what to put there, not just that something is missing.
        Assert.Contains("192.168.0.0/16", error.Message);
    }

    [Fact]
    public void noTokenAtAll_isAValidViewerOnlyInstall()
    {
        // A range running only the bundled displays has nothing external to authenticate. The
        // viewer still uses the per-process session token, so this is not an open API.
        Check(new SintroOptions());
    }

    [Theory]
    [InlineData("too-short")]
    [InlineData("123456789012345")]   // 15 chars — one below the minimum
    public void aTokenBelowTheMinimum_refusesToStart(string token)
    {
        // A token that is configured must still be a real one; a short one is a mistake, not
        // a choice to run without.
        var error = Assert.Throws<InvalidOperationException>(() =>
            Check(new SintroOptions { ApiReadTokens = [token] }));

        Assert.Contains("refuses to start", error.Message);
    }

    [Fact]
    public void aShortWriteTokenIsCaughtToo()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Check(new SintroOptions { ApiWriteTokens = ["short"] }));
    }

    [Fact]
    public void aTokenAtTheMinimum_isAccepted() =>
        Check(new SintroOptions { ApiReadTokens = [new string('a', SintroOptions.MinimumTokenLength)] });

    [Fact]
    public void aWriteTokenAloneIsEnough_becauseWriteImpliesRead() =>
        Check(new SintroOptions { ApiWriteTokens = [new string('b', SintroOptions.MinimumTokenLength)] });

    [Fact]
    public void theSameTokenInBothScopes_refusesToStart()
    {
        // Listing it twice only makes the intended scope ambiguous.
        var token = new string('c', SintroOptions.MinimumTokenLength);

        var error = Assert.Throws<InvalidOperationException>(() =>
            Check(new SintroOptions { ApiReadTokens = [token], ApiWriteTokens = [token] }));

        Assert.Contains("both", error.Message);
    }

    [Theory]
    [InlineData("192.168.1.0/33")]
    [InlineData("not-a-range")]
    [InlineData("192.168.1/24")]
    public void anUnparseableNetworkRange_refusesToStart(string range)
    {
        // The gate skips entries it cannot parse, so a typo would silently lock a network
        // out rather than widen access — still wrong, and miserable to debug.
        var options = new SintroOptions
        {
            ApiReadTokens = [new string('a', SintroOptions.MinimumTokenLength)],
            Network = new NetworkOptions { Api = [range] },
        };

        var error = Assert.Throws<InvalidOperationException>(() => Check(options));
        Assert.Contains(range, error.Message);
        Assert.Contains("Sintro:Network:Api", error.Message);
    }

    [Fact]
    public void anUnparseableTrustedProxy_refusesToStart()
    {
        var options = new SintroOptions
        {
            ApiReadTokens = [new string('a', SintroOptions.MinimumTokenLength)],
            TrustedProxies = ["proxy.local"],
        };

        var error = Assert.Throws<InvalidOperationException>(() => Check(options));
        Assert.Contains("Sintro:TrustedProxies", error.Message);
    }

    [Fact]
    public void validNetworkRangesAreAccepted() =>
        Check(new SintroOptions
        {
            ApiReadTokens = [new string('a', SintroOptions.MinimumTokenLength)],
            Network = new NetworkOptions { Api = ["192.168.1.0/24", "10.0.0.5"], Web = ["fd00::/8"] },
            TrustedProxies = ["127.0.0.1"],
        });

    [Fact]
    public void generatedTokens_areLongAndUnique()
    {
        var first = StartupChecks.GenerateToken();
        var second = StartupChecks.GenerateToken();

        Assert.True(first.Length >= SintroOptions.RecommendedTokenLength);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void sessionTokens_differPerInstance() =>
        Assert.NotEqual(new Security.SessionToken().Value, new Security.SessionToken().Value);
}
