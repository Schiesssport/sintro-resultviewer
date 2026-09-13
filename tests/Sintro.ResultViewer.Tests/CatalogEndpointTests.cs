using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sintro.ResultViewer.Api.V2;
using Sintro.ResultViewer.Domain;

namespace Sintro.ResultViewer.Tests;

/// <summary>
/// Like ProgramEndpointTests: invariants only, no counts or names from one club's export.
/// </summary>
[Collection(ApiCollection.Name)]
public class CatalogEndpointTests(ApiFixture fixture)
{
    private HttpClient Client() => fixture.CreateAuthorizedClient();

    [RequiresDatabaseFact]
    public async Task everyLineIsListedInOrder()
    {
        var lanes = (await Client().GetFromJsonAsync<List<LaneStatus>>("/api/v2/live", TestJson.Options))!;

        // How many lines an installation has is site-specific; that they are all listed,
        // in order, so a display can show the whole firing line, is not.
        Assert.NotEmpty(lanes);
        Assert.Equal(lanes.Select(lane => lane.Number).OrderBy(number => number),
                     lanes.Select(lane => lane.Number));
        Assert.Equal(lanes.Count, lanes.Select(lane => lane.Number).Distinct().Count());
    }

    [RequiresDatabaseFact]
    public async Task aLineWithAPassOnItReportsThatPassAsActive()
    {
        var lanes = (await Client().GetFromJsonAsync<List<LaneStatus>>("/api/v2/live", TestJson.Options))!;

        Assert.All(lanes.Where(lane => lane.CurrentProgram is not null),
            lane => Assert.Equal(ProgramState.Active, lane.CurrentProgram!.State));
    }

    [RequiresDatabaseFact]
    public async Task idleLinesCarryAnExplicitNullRatherThanAMissingKey()
    {
        // The docs promise currentProgram: null. Omitting the key instead would force every
        // client to tell "absent" from "empty" and would contradict the published schema.
        var raw = await Client().GetStringAsync("/api/v2/live");

        using var document = JsonDocument.Parse(raw);
        foreach (var lane in document.RootElement.EnumerateArray())
        {
            Assert.True(lane.TryGetProperty("currentProgram", out _),
                $"line {lane.GetProperty("number")} is missing currentProgram");
        }
    }

    [RequiresDatabaseFact]
    public async Task anonymousPassesCarryAnExplicitNullShooter()
    {
        var raw = await Client().GetStringAsync(
            $"/api/v2/programs?{ApiFixture.WholeRange}&withoutResult=true&limit=200");

        Assert.Contains("\"shooter\":null", raw.Replace(" ", ""));
    }

    [RequiresDatabaseFact]
    public async Task everyShooterHasANormalisedLicence()
    {
        var page = (await Client().GetFromJsonAsync<CursorPage<Shooter>>(
            "/api/v2/shooters?limit=500", TestJson.Options))!;

        Assert.All(page.Items, shooter =>
        {
            Assert.Matches(@"^\d{6,}$", shooter.License);   // padded to six, never capped
            Assert.False(string.IsNullOrWhiteSpace(shooter.LastName));
        });
    }

    [RequiresDatabaseFact]
    public async Task theAllZeroRfidPlaceholderIsReportedAsAbsent()
    {
        var page = (await Client().GetFromJsonAsync<CursorPage<Shooter>>(
            "/api/v2/shooters?limit=500", TestJson.Options))!;

        // RFID is deprecated and installations share one all-zero placeholder across many
        // shooters. It must never surface as if it were a real card id.
        Assert.All(page.Items, shooter =>
            Assert.False(shooter.Rfid is not null && shooter.Rfid.All(character => character == '0')));
    }

    [RequiresDatabaseFact]
    public async Task shooterSearchMatchesNameAndLicence()
    {
        var licence = await fixture.AnyLicenceAsync();
        if (licence is null) return;

        var byLicence = (await Client().GetFromJsonAsync<CursorPage<Shooter>>(
            $"/api/v2/shooters?q={licence}", TestJson.Options))!;
        Assert.NotEmpty(byLicence.Items);

        var surname = byLicence.Items[0].LastName;
        var byName = (await Client().GetFromJsonAsync<CursorPage<Shooter>>(
            $"/api/v2/shooters?q={Uri.EscapeDataString(surname)}", TestJson.Options))!;

        Assert.Contains(byName.Items, shooter => shooter.LastName == surname);
    }

    [RequiresDatabaseFact]
    public async Task aLicenceLookupReturnsTheShooterAndTheirPasses()
    {
        var licence = await fixture.AnyLicenceAsync();
        if (licence is null) return;

        var detail = (await Client().GetFromJsonAsync<ShooterDetail>(
            $"/api/v2/shooters/{licence}", TestJson.Options))!;

        Assert.Equal(licence, detail.License);
        Assert.NotEmpty(detail.Shooters);
        Assert.All(detail.Shooters, shooter => Assert.Equal(licence, shooter.License));
        Assert.NotNull(detail.Programs);
    }

    [RequiresDatabaseFact]
    public async Task aShootersPassesArePagedLikeEveryOtherCollection()
    {
        // They used to be silently capped, so a prolific shooter's older passes simply vanished
        // with nothing in the response to say so.
        var licence = await fixture.AnyLicenceAsync();
        if (licence is null) return;

        var everything = (await Client().GetFromJsonAsync<ShooterDetail>(
            $"/api/v2/shooters/{licence}?limit=5000", TestJson.Options))!;
        if (everything.Programs.Items.Count < 2) return;

        var walked = new List<int>();
        string? cursor = null;
        do
        {
            var suffix = cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}";
            var page = (await Client().GetFromJsonAsync<ShooterDetail>(
                $"/api/v2/shooters/{licence}?limit=1{suffix}", TestJson.Options))!;

            walked.AddRange(page.Programs.Items.Select(program => program.Id));
            cursor = page.Programs.NextCursor;
        }
        while (cursor is not null && walked.Count < 500);

        Assert.Equal(everything.Programs.Items.Select(program => program.Id), walked);
    }

    [RequiresDatabaseFact]
    public async Task aMisspelledFilterIsRejectedRatherThanIgnored()
    {
        // Ignoring it returned *everything* — the opposite of what the caller asked for, and
        // invisible to software importing the result.
        foreach (var query in new[] { "state=finishd", "state=", "order=ascending" })
        {
            if (query == "state=") continue;   // empty means "no filter", which is fine

            var response = await Client().GetAsync($"/api/v2/programs?{query}");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<ApiError>(TestJson.Options);
            Assert.False(string.IsNullOrWhiteSpace(body!.Error));
            Assert.Contains("Expected", body.Detail);
        }
    }

    [RequiresDatabaseFact]
    public async Task anEmptyFilterValueStillMeansNoFilter()
    {
        var response = await Client().GetAsync($"/api/v2/programs?state=&order=&{ApiFixture.WholeRange}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task everyValidStateAndOrderIsAccepted()
    {
        foreach (var value in new[] { "active", "finished", "abandoned", "ACTIVE", " finished " })
        {
            var response = await Client().GetAsync(
                $"/api/v2/programs?state={Uri.EscapeDataString(value)}&{ApiFixture.WholeRange}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        foreach (var value in new[] { "asc", "desc", "ASC" })
        {
            var response = await Client().GetAsync(
                $"/api/v2/programs?order={value}&{ApiFixture.WholeRange}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [RequiresDatabaseFact]
    public async Task aLicenceLookupIgnoresLeadingZeros()
    {
        var licence = await fixture.AnyLicenceAsync();
        if (licence is null || !licence.StartsWith('0')) return;

        var padded = (await Client().GetFromJsonAsync<ShooterDetail>(
            $"/api/v2/shooters/{licence}", TestJson.Options))!;
        var unpadded = (await Client().GetFromJsonAsync<ShooterDetail>(
            $"/api/v2/shooters/{licence.TrimStart('0')}", TestJson.Options))!;

        Assert.Equal(padded.Shooters[0].ShooterId, unpadded.Shooters[0].ShooterId);
    }

    [RequiresDatabaseFact]
    public async Task anUnknownLicenceIs404()
    {
        var response = await Client().GetAsync("/api/v2/shooters/999999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task clubSearchNarrowsTheRegister()
    {
        var name = await fixture.AnyClubNameAsync();
        if (string.IsNullOrWhiteSpace(name)) return;

        var term = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).First();

        var search = (await Client().GetFromJsonAsync<CursorPage<Club>>(
            $"/api/v2/clubs?q={Uri.EscapeDataString(term)}", TestJson.Options))!;

        Assert.NotEmpty(search.Items);
        Assert.All(search.Items, club =>
            Assert.Contains(term, club.Name, StringComparison.OrdinalIgnoreCase));
    }

    [RequiresDatabaseFact]
    public async Task clubNamesArriveWithoutTheRegistersStrayWhitespace()
    {
        // The register the device ships carries trailing CR characters in its names; the
        // API must absorb that, not hand it to every client.
        var page = (await Client().GetFromJsonAsync<CursorPage<Club>>(
            "/api/v2/clubs?limit=500", TestJson.Options))!;

        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, club =>
        {
            Assert.Equal(club.Name.Trim(), club.Name);
            Assert.Equal(club.Number.Trim(), club.Number);
        });
    }

    [RequiresDatabaseFact]
    public async Task theProgramCatalogAccountsForEveryPass()
    {
        var catalog = (await Client().GetFromJsonAsync<List<ProgramCatalogEntry>>(
            "/api/v2/program-catalog", TestJson.Options))!;

        var everything = (await Client().GetFromJsonAsync<CursorPage<ShootingProgram>>(
            $"/api/v2/programs?{ApiFixture.WholeRange}&withoutResult=true&limit=5000",
            TestJson.Options))!;

        Assert.Equal(everything.Items.Count, catalog.Sum(entry => entry.ProgramCount));

        // (number, name) is the unit, because operators rename programs: the same number can
        // appear under several names, so the catalogue may hold more entries than numbers.
        Assert.Equal(catalog.Count,
            catalog.Select(entry => (entry.Number, entry.Name)).Distinct().Count());
    }

    [RequiresDatabaseFact]
    public async Task healthReportsReachabilityAndNoPublicExposureByDefault()
    {
        var health = (await Client().GetFromJsonAsync<HealthReport>("/api/v2/health", TestJson.Options))!;

        Assert.True(health.DatabaseReachable);
        Assert.Empty(health.PublicExposure);
    }
}
