using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sintro.ResultViewer.Api.V2;
using Sintro.ResultViewer.Domain;

namespace Sintro.ResultViewer.Tests;

/// <summary>Runs against whatever export is loaded and asserts invariants, never counts: exports differ per installation, so reference values are read from the API at run time.</summary>
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

    [Fact]
    public async Task theDefaultListOnlyContainsPassesThatHaveAResult()
    {
        var withResult = await ProgramsAsync($"{ApiFixture.WholeRange}&limit=5000");

        Assert.NotEmpty(withResult.Items);
        Assert.All(withResult.Items, program => Assert.True(program.Series.Sum(series => series.ShotCount) > 0));
    }

    [Fact]
    public async Task withoutResultAddsThePassesThatWereStartedAndAbandoned()
    {
        var withResult = await ProgramsAsync($"{ApiFixture.WholeRange}&limit=5000");
        var everything = await AllProgramsAsync();

        Assert.True(everything.Items.Count >= withResult.Items.Count);

        var extra = everything.Items.Select(program => program.Id)
            .Except(withResult.Items.Select(program => program.Id))
            .ToHashSet();

        Assert.All(everything.Items.Where(program => extra.Contains(program.Id)),
            program => Assert.Empty(program.Series));
    }

    [Fact]
    public async Task theThreeStateFiltersPartitionTheList()
    {
        // The third state exists because a pass off the line with no end total is neither active nor finished.
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

    [Fact]
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

    [Fact]
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

    [Fact]
    public async Task theDefaultWindowIsASingleDay()
    {
        // No from/to means today only, and ReferenceDate pins today to the export's day.
        var page = await ProgramsAsync("withoutResult=true&limit=5000");
        if (page.Items.Count == 0) return;

        var days = page.Items.Select(program => DateOnly.FromDateTime(program.StartedAt.Date)).Distinct();
        Assert.Single(days);
    }

    [Fact]
    public async Task eachTotalIsTheSumOfTheSeriesOnItsScale()
    {
        var page = await ProgramsAsync($"{ApiFixture.WholeRange}&limit=5000");

        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, program =>
        {
            Assert.Equal(program.Series.Select(series => series.TargetType).Distinct(), program.Totals.Select(total => total.TargetType));
            Assert.All(program.Totals, total =>
            {
                var onScale = program.Series.Where(series => series.TargetType == total.TargetType).ToList();
                Assert.Equal(onScale.Sum(series => series.Subtotal), total.Value);
                Assert.Equal(onScale.SelectMany(series => series.Shots).Select(shot => shot.FineValue), total.FineValues);
                Assert.Equal(onScale[0].Valuation, total.Valuation);
            });
        });
    }

    [Fact]
    public async Task aProgramOnOneScaleHasExactlyOneTotal()
    {
        var page = await ProgramsAsync($"{ApiFixture.WholeRange}&limit=5000");
        var single = page.Items.Where(program => program.Series.Select(series => series.TargetType).Distinct().Count() == 1).ToList();

        Assert.NotEmpty(single);
        Assert.All(single, program => Assert.Single(program.Totals));
    }

    [Fact]
    public async Task sightingShotsAreNeverCountedTowardsATotal()
    {
        var page = await ProgramsAsync($"{ApiFixture.WholeRange}&limit=5000");
        var withSighting = page.Items.Where(program => program.Sighting.Count > 0).ToList();

        Assert.NotEmpty(withSighting);
        Assert.All(withSighting, program =>
        {
            Assert.All(program.Totals, total =>
                Assert.Equal(program.Series.Where(series => series.TargetType == total.TargetType).Sum(series => series.ShotCount), total.FineValues.Count));
            Assert.All(program.Sighting, series => Assert.True(series.ShotCount > 0));
        });
    }

    [Fact]
    public async Task markerRowsNeverAppearAsShots()
    {
        var page = await AllProgramsAsync();

        var everyShot = page.Items
            .SelectMany(program => program.Series.Concat(program.Sighting))
            .SelectMany(series => series.Shots);

        Assert.DoesNotContain(9999, everyShot.Select(shot => shot.Number));
    }

    [Fact]
    public async Task everySeriesCarriesAReadableTargetType()
    {
        var page = await ProgramsAsync($"{ApiFixture.WholeRange}&limit=5000");

        Assert.All(page.Items.SelectMany(program => program.Series), series =>
        {
            Assert.False(string.IsNullOrWhiteSpace(series.TargetType));

            // Letter plus ring scale, e.g. A10 / B4 / S10; "?" only where the device gave nothing.
            Assert.Matches(@"^[ABS?]\d+$|^[ABS?]\?$", series.TargetType);
        });
    }

    [Fact]
    public async Task aMatchCodeIsNeverZeroAndDoesNotChangeWithinAPass()
    {
        var page = await ProgramsAsync($"{ApiFixture.WholeRange}&limit=5000");

        Assert.All(page.Items, program =>
        {
            var codes = program.Series.Concat(program.Sighting)
                .SelectMany(series => series.Shots)
                .Select(shot => shot.MatchCode)
                .Distinct()
                .ToList();

            Assert.DoesNotContain(0, codes);
            Assert.True(codes.Count <= 1, $"program {program.Id} carries several match codes");
        });
    }

    [Fact]
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

    [Fact]
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

    [Fact]
    public async Task timestampsAreIso8601WithAnOffset()
    {
        var raw = await Client().GetStringAsync(
            $"/api/v2/programs?{ApiFixture.WholeRange}&limit=5&withoutResult=true");

        using var document = JsonDocument.Parse(raw);
        var startedAt = document.RootElement.GetProperty("items")[0]
            .GetProperty("startedAt").GetString()!;

        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}([.\d]*)?[+-]\d{2}:\d{2}$", startedAt);
    }

    [Fact]
    public async Task statesSerializeExactlyAsTheDocsPromiseThem()
    {
        // The wire carries "active", "finished" and "abandoned", not the C# enum spellings.
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

    [Fact]
    public async Task theLineFilterNarrowsToThatLine()
    {
        var any = await AllProgramsAsync();
        var line = any.Items.First().Lane;

        var filtered = await AllProgramsAsync($"&lane={line}");

        Assert.NotEmpty(filtered.Items);
        Assert.All(filtered.Items, program => Assert.Equal(line, program.Lane));
    }

    [Fact]
    public async Task anUnknownProgramIs404()
    {
        var response = await Client().GetAsync("/api/v2/programs/999999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task listFiltersUnionTheirValuesAndRefuseNonNumbers()
    {
        var all = await AllProgramsAsync();
        var codes = all.Items.Select(program => program.TargetCode).Distinct().Take(2).ToList();
        if (codes.Count < 2) return;

        var first = await AllProgramsAsync($"&targetCode={codes[0]}");
        var second = await AllProgramsAsync($"&targetCode={codes[1]}");
        var both = await AllProgramsAsync($"&targetCode={codes[0]},{codes[1]}");

        Assert.Equal(first.Items.Count + second.Items.Count, both.Items.Count);
        Assert.All(both.Items, program => Assert.Contains(program.TargetCode, codes));

        var response = await Client().GetAsync($"/api/v2/programs?{ApiFixture.WholeRange}&targetCode=41,abc");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task theMatchCodeFilterKeepsOnlyPassesShotUnderThatCode()
    {
        var all = await AllProgramsAsync();
        var code = all.Items.SelectMany(program => program.Series).SelectMany(series => series.Shots)
            .Select(shot => shot.MatchCode).FirstOrDefault(value => value is not null);
        if (code is null) return;

        var filtered = await AllProgramsAsync($"&matchCode={code}");

        Assert.NotEmpty(filtered.Items);
        Assert.All(filtered.Items, program =>
            Assert.Contains(program.Series.Concat(program.Sighting).SelectMany(series => series.Shots), shot => shot.MatchCode == code));
    }

    [Fact]
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
