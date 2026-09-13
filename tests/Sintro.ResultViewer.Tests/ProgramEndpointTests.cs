using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sintro.ResultViewer.Api.V2;
using Sintro.ResultViewer.Domain;

namespace Sintro.ResultViewer.Tests;

/// <summary>
/// Runs against whatever device export is loaded in the dev database.
///
/// Deliberately asserts invariants rather than counts: exports differ per installation — number
/// of lines, programs shot, shooters registered — so a hard-coded total would only ever be true
/// for one club, and would tell a contributor with their own export that the code is broken when
/// it is not. Reference values are read from the API at run time.
/// </summary>
[Collection(ApiCollection.Name)]
public class ProgramEndpointTests(ApiFixture fixture)
{
    private HttpClient Client() => fixture.CreateAuthorizedClient();

    private async Task<CursorPage<ShootingProgram>> ProgramsAsync(string query)
    {
        var response = await Client().GetAsync($"/api/v2/programs?{query}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CursorPage<ShootingProgram>>(SintroJson.Options))!;
    }

    private Task<CursorPage<ShootingProgram>> AllProgramsAsync(string extra = "") =>
        ProgramsAsync($"{ApiFixture.WholeRange}&withoutResult=true&limit=5000{extra}");

    [RequiresDatabaseFact]
    public async Task theDefaultListOnlyContainsPassesThatHaveAResult()
    {
        var withResult = await ProgramsAsync($"{ApiFixture.WholeRange}&limit=5000");

        Assert.NotEmpty(withResult.Items);
        Assert.All(withResult.Items, program => Assert.True(program.ShotCount > 0));
    }

    [RequiresDatabaseFact]
    public async Task withoutResultAddsThePassesThatWereStartedAndAbandoned()
    {
        var withResult = await ProgramsAsync($"{ApiFixture.WholeRange}&limit=5000");
        var everything = await AllProgramsAsync();

        Assert.True(everything.Items.Count >= withResult.Items.Count);

        // Whatever the extra ones are, they are exactly the ones with nothing shot.
        var extra = everything.Items.Select(program => program.Id)
            .Except(withResult.Items.Select(program => program.Id))
            .ToHashSet();

        Assert.All(everything.Items.Where(program => extra.Contains(program.Id)),
            program => Assert.Equal(0, program.ShotCount));
    }

    [RequiresDatabaseFact]
    public async Task theThreeStateFiltersPartitionTheList()
    {
        // Every pass is in exactly one state, and each filter returns exactly that state.
        // A two-state model failed here: passes with no end total that are no longer on a line
        // are neither active nor finished, and reporting them as finished made an item's own
        // state disagree with ?state=finished.
        var everything = await AllProgramsAsync();

        var byState = new Dictionary<ProgramState, List<int>>();
        foreach (var state in Enum.GetValues<ProgramState>())
        {
            var page = await AllProgramsAsync($"&state={state.ToString().ToLowerInvariant()}");
            Assert.All(page.Items, program => Assert.Equal(state, program.State));
            byState[state] = page.Items.Select(program => program.Id).ToList();
        }

        var partitioned = byState.Values.SelectMany(ids => ids).ToList();

        Assert.Equal(partitioned.Count, partitioned.Distinct().Count());
        Assert.Equal(everything.Items.Count, partitioned.Count);
    }

    [RequiresDatabaseFact]
    public async Task everyPassReportsAStateThatItsOwnFilterAgreesWith()
    {
        var everything = await AllProgramsAsync();

        foreach (var state in everything.Items.Select(program => program.State).Distinct())
        {
            var filtered = await AllProgramsAsync($"&state={state.ToString().ToLowerInvariant()}");
            var expected = everything.Items.Where(program => program.State == state)
                                           .Select(program => program.Id).OrderBy(id => id);

            Assert.Equal(expected, filtered.Items.Select(program => program.Id).OrderBy(id => id));
        }
    }

    [RequiresDatabaseFact]
    public async Task activePassesAreExactlyThoseOnALine()
    {
        var active = await AllProgramsAsync("&state=active");
        var lanes = (await Client().GetFromJsonAsync<List<LaneStatus>>("/api/v2/live", SintroJson.Options))!;

        var onALine = lanes
            .Where(lane => lane.CurrentProgram is not null)
            .Select(lane => lane.CurrentProgram!.Id)
            .OrderBy(id => id);

        Assert.Equal(onALine, active.Items.Select(program => program.Id).OrderBy(id => id));
    }

    [RequiresDatabaseFact]
    public async Task theDefaultWindowIsASingleDay()
    {
        // No from/to means today only, and ReferenceDate pins today to the export's day.
        var page = await ProgramsAsync("withoutResult=true&limit=5000");
        if (page.Items.Count == 0) return;

        var days = page.Items.Select(program => DateOnly.FromDateTime(program.StartedAt.Date)).Distinct();
        Assert.Single(days);
    }

    [RequiresDatabaseFact]
    public async Task aTotalIsTheSumOfItsSeriesSubtotals()
    {
        var page = await ProgramsAsync($"{ApiFixture.WholeRange}&limit=5000");
        var scored = page.Items.Where(program => program.Total is not null).ToList();

        Assert.NotEmpty(scored);
        Assert.All(scored, program =>
        {
            Assert.Equal(program.Series.Sum(series => series.Subtotal), program.Total!.Value);
            Assert.Equal(program.Series.Sum(series => series.ShotCount), program.Total.ShotCount);

            // One valuation throughout, or there would be no total at all.
            Assert.Single(program.Series.Select(series => series.Valuation).Distinct());
        });
    }

    [RequiresDatabaseFact]
    public async Task theFlatShotValuesMatchTheSeriesTheyCameFrom()
    {
        var page = await ProgramsAsync($"{ApiFixture.WholeRange}&limit=5000");

        Assert.All(page.Items, program =>
        {
            var fromSeries = program.Series.SelectMany(series => series.Shots)
                                           .Select(shot => shot.Value).ToList();

            Assert.Equal(fromSeries, program.ShotValues);
            Assert.Equal(string.Join(' ', fromSeries), program.ShotValuesText);
            Assert.Equal(fromSeries.Count, program.ShotCount);
        });
    }

    [RequiresDatabaseFact]
    public async Task sightingShotsAreNeverCountedTowardsATotal()
    {
        var page = await ProgramsAsync($"{ApiFixture.WholeRange}&limit=5000");
        var withSighting = page.Items.Where(program => program.Sighting.Count > 0).ToList();

        Assert.NotEmpty(withSighting);
        Assert.All(withSighting, program =>
        {
            // Counting shots alone make up the result; the sighting series sit beside it.
            Assert.Equal(program.Series.Sum(series => series.ShotCount), program.ShotCount);
            Assert.All(program.Sighting, series => Assert.True(series.ShotCount > 0));
        });
    }

    [RequiresDatabaseFact]
    public async Task markerRowsNeverAppearAsShots()
    {
        var page = await AllProgramsAsync();

        var everyShot = page.Items
            .SelectMany(program => program.Series.Concat(program.Sighting))
            .SelectMany(series => series.Shots);

        Assert.DoesNotContain(9999, everyShot.Select(shot => shot.Number));
    }

    [RequiresDatabaseFact]
    public async Task aPassWithoutATotalAlwaysSaysWhy()
    {
        var page = await ProgramsAsync($"{ApiFixture.WholeRange}&limit=5000");
        var unscored = page.Items.Where(program => program.Total is null).ToList();

        // Every one of these has shots — the default filter guarantees it — so the only honest
        // reasons are a mixed or an unknown ring scale.
        Assert.All(unscored, program => Assert.NotNull(program.TotalUnavailable));

        var mixed = unscored
            .Where(program => program.TotalUnavailable == TotalUnavailableReason.MixedValuation)
            .ToList();

        // Only assert the detail if the export happens to contain such a pass.
        Assert.All(mixed, program =>
            Assert.True(program.Series.Select(series => series.Valuation).Distinct().Count() > 1));
    }

    [RequiresDatabaseFact]
    public async Task everySeriesCarriesAReadableTargetCode()
    {
        var page = await ProgramsAsync($"{ApiFixture.WholeRange}&limit=5000");

        Assert.All(page.Items.SelectMany(program => program.Series), series =>
        {
            Assert.False(string.IsNullOrWhiteSpace(series.TargetCode));

            // Letter plus ring scale, e.g. A10 / B4 / S10; "?" only where the device gave nothing.
            Assert.Matches(@"^[ABS?]\d+$|^[ABS?]\?$", series.TargetCode);
        });
    }

    [RequiresDatabaseFact]
    public async Task hitSectorsStayInTheRangeTheDialCanDraw()
    {
        var page = await ProgramsAsync($"{ApiFixture.WholeRange}&limit=5000");

        var sectors = page.Items
            .SelectMany(program => program.Series)
            .SelectMany(series => series.Shots)
            .Select(shot => shot.HitSector)
            .Where(sector => sector is not null);

        // 0 is a centre hit, 1-8 are the clock sectors; 255 must have become null.
        Assert.All(sectors, sector => Assert.InRange(sector!.Value, 0, 8));
    }

    [RequiresDatabaseFact]
    public async Task aPassWithoutAShooterIsStillIdentifiable()
    {
        var page = await AllProgramsAsync();
        var anonymous = page.Items.Where(program => program.Shooter is null).ToList();

        // Identifying yourself is optional, so this is normal operation, not bad data.
        Assert.All(anonymous, program =>
        {
            Assert.True(program.Lane > 0);
            Assert.NotEqual(default, program.StartedAt);
        });
    }

    [RequiresDatabaseFact]
    public async Task timestampsAreIso8601WithAnOffset()
    {
        var raw = await Client().GetStringAsync(
            $"/api/v2/programs?{ApiFixture.WholeRange}&limit=5&withoutResult=true");

        using var document = JsonDocument.Parse(raw);
        var startedAt = document.RootElement.GetProperty("items")[0]
            .GetProperty("startedAt").GetString()!;

        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}([.\d]*)?[+-]\d{2}:\d{2}$", startedAt);
    }

    [RequiresDatabaseFact]
    public async Task statesSerializeExactlyAsTheDocsPromiseThem()
    {
        // The documentation names the states as "active", "finished" and "abandoned" —
        // the wire must carry those, not the C# enum spellings.
        var raw = await Client().GetStringAsync(
            $"/api/v2/programs?{ApiFixture.WholeRange}&withoutResult=true&limit=200");

        var states = System.Text.RegularExpressions.Regex
            .Matches(raw, "\"state\":\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .Distinct().ToList();

        Assert.NotEmpty(states);
        Assert.All(states, state =>
            Assert.Contains(state, new[] { "active", "finished", "abandoned" }));
    }

    [RequiresDatabaseFact]
    public async Task theLineFilterNarrowsToThatLine()
    {
        var any = await AllProgramsAsync();
        var line = any.Items.First().Lane;

        var filtered = await AllProgramsAsync($"&lane={line}");

        Assert.NotEmpty(filtered.Items);
        Assert.All(filtered.Items, program => Assert.Equal(line, program.Lane));
    }

    [RequiresDatabaseFact]
    public async Task anUnknownProgramIs404()
    {
        var response = await Client().GetAsync("/api/v2/programs/999999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task theLicenceFilterIgnoresLeadingZerosAndRejectsTheUnknown()
    {
        var licence = await fixture.AnyLicenceAsync();
        if (licence is null) return;

        var padded = await AllProgramsAsync($"&license={licence}");
        var unpadded = await AllProgramsAsync($"&license={licence.TrimStart('0')}");

        Assert.Equal(padded.Items.Count, unpadded.Items.Count);

        // An unmatched licence must narrow to nothing, not silently fall back to everything.
        var unknown = await AllProgramsAsync("&license=999999999");
        Assert.Empty(unknown.Items);
    }
}
