using System.Net;
using System.Net.Http.Json;
using Sintro.ResultViewer.Data;
using Sintro.ResultViewer.Api.V2;
using Sintro.ResultViewer.Domain;

namespace Sintro.ResultViewer.Tests;

/// <summary>Walking every page visits each record exactly once, and ascending order plus a stored cursor is a correct incremental sync.</summary>
[Collection(ApiCollection.Name)]
public class CursorPagingTests(ApiFixture fixture)
{
    private HttpClient Client() => fixture.CreateAuthorizedClient();

    private async Task<CursorPage<T>> PageAsync<T>(string pathWithQuery) =>
        (await Client().GetFromJsonAsync<CursorPage<T>>(pathWithQuery, SintroJson.Options))!;

    private Task<CursorPage<ShootingProgram>> ProgramsAsync(string query) =>
        PageAsync<ShootingProgram>($"/api/v2/programs?{query}");

    private async Task<List<T>> WalkAsync<T>(string pathWithQuery, int stopAfter)
    {
        var all = new List<T>();
        string? cursor = null;

        do
        {
            var suffix = cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}";
            var page = await PageAsync<T>($"{pathWithQuery}{suffix}");
            all.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null && all.Count < stopAfter);

        return all;
    }

    [RequiresDatabaseFact]
    public async Task walkingEveryPage_visitsEachProgramExactlyOnce()
    {
        var single = await ProgramsAsync($"{ApiFixture.WholeRange}&withoutResult=true&limit=2000");
        var walked = await WalkAsync<ShootingProgram>(
            $"/api/v2/programs?{ApiFixture.WholeRange}&withoutResult=true&limit=37", stopAfter: 5000);

        Assert.Equal(single.Items.Count, walked.Count);
        Assert.Equal(
            single.Items.Select(program => program.Id),
            walked.Select(program => program.Id));
        Assert.Equal(walked.Count, walked.Select(program => program.Id).Distinct().Count());
    }

    [RequiresDatabaseFact]
    public async Task theLastPageReportsNoNextCursor()
    {
        var page = await ProgramsAsync($"{ApiFixture.WholeRange}&withoutResult=true&limit=5000");

        Assert.Null(page.NextCursor);
        Assert.NotEmpty(page.Items);
    }

    [RequiresDatabaseFact]
    public async Task afullPageThatExactlyEmptiesTheSetStillTerminates()
    {
        // A limit equal to the remaining rows must not hand back a cursor to an empty page.
        var all = await ProgramsAsync($"{ApiFixture.WholeRange}&withoutResult=true&limit=5000");

        var exact = await ProgramsAsync(
            $"{ApiFixture.WholeRange}&withoutResult=true&limit={all.Items.Count}");

        Assert.Equal(all.Items.Count, exact.Items.Count);
        Assert.Null(exact.NextCursor);
    }

    [RequiresDatabaseFact]
    public async Task defaultOrderIsNewestFirst()
    {
        var page = await ProgramsAsync($"{ApiFixture.WholeRange}&withoutResult=true&limit=50");
        var ids = page.Items.Select(program => program.Id).ToList();

        Assert.Equal(ids.OrderByDescending(id => id), ids);
    }

    [RequiresDatabaseFact]
    public async Task ascendingOrderIsOldestFirst()
    {
        var page = await ProgramsAsync($"{ApiFixture.WholeRange}&withoutResult=true&order=asc&limit=50");
        var ids = page.Items.Select(program => program.Id).ToList();

        Assert.Equal(ids.OrderBy(id => id), ids);
    }

    [RequiresDatabaseFact]
    public async Task ascendingCursorIsAnIncrementalSync()
    {
        var first = await ProgramsAsync($"{ApiFixture.WholeRange}&withoutResult=true&order=asc&limit=20");
        Assert.NotNull(first.NextCursor);

        var second = await ProgramsAsync(
            $"{ApiFixture.WholeRange}&withoutResult=true&order=asc&limit=20&cursor={Uri.EscapeDataString(first.NextCursor!)}");

        var firstIds = first.Items.Select(program => program.Id).ToHashSet();
        Assert.All(second.Items, program => Assert.DoesNotContain(program.Id, firstIds));
        Assert.All(second.Items, program => Assert.True(program.Id > first.Items[^1].Id));
    }

    [RequiresDatabaseFact]
    public async Task aGarbageCursorIs400RatherThanARestartFromPageOne()
    {
        var response = await Client().GetAsync(
            $"/api/v2/programs?{ApiFixture.WholeRange}&withoutResult=true&limit=5&cursor=not-a-cursor");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiError>(SintroJson.Options);
        Assert.Equal("invalid_cursor", body!.Error);
    }

    [RequiresDatabaseFact]
    public async Task aCursorIssuedForOneOrderIsRefusedForTheOther()
    {
        // Continuing an ascending walk with the default descending order would return everything older.
        var first = await ProgramsAsync($"{ApiFixture.WholeRange}&withoutResult=true&order=asc&limit=5");
        Assert.NotNull(first.NextCursor);

        var response = await Client().GetAsync(
            $"/api/v2/programs?{ApiFixture.WholeRange}&withoutResult=true&limit=5&cursor={Uri.EscapeDataString(first.NextCursor!)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiError>(SintroJson.Options);
        Assert.Equal("invalid_cursor", body!.Error);
        Assert.Contains("order=asc", body.Detail);
    }

    [Theory]
    [InlineData("/api/v2/shooters?cursor=not-a-cursor")]
    [InlineData("/api/v2/clubs?cursor=not-a-cursor")]
    public async Task everyCollectionRejectsAGarbageCursor(string path)
    {
        var response = await Client().GetAsync(path);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [RequiresDatabaseFact]
    public async Task shootersPageAlphabeticallyWithoutRepeats()
    {
        var all = await WalkAsync<Shooter>("/api/v2/shooters?limit=25", stopAfter: 1000);

        Assert.NotEmpty(all);
        Assert.Equal(all.Count, all.Select(shooter => shooter.ShooterId).Distinct().Count());

        // Compared against the unpaged order, not a .NET comparer: none reproduces SQL Server's accent-aware collation.
        var unpaged = await PageAsync<Shooter>("/api/v2/shooters?limit=500");

        Assert.Equal(
            unpaged.Items.Select(shooter => shooter.ShooterId),
            all.Select(shooter => shooter.ShooterId));
    }

    [RequiresDatabaseFact]
    public async Task clubsPageThroughTheWholeRegister()
    {
        var all = await WalkAsync<Club>("/api/v2/clubs?limit=500", stopAfter: 5000);

        Assert.NotEmpty(all);
        Assert.Equal(all.Count, all.Select(club => club.Id).Distinct().Count());

        var unpaged = await PageAsync<Club>("/api/v2/clubs?limit=5000");
        Assert.Equal(unpaged.Items.Select(club => club.Id), all.Select(club => club.Id));
    }
}

public class CursorTests
{
    [Fact]
    public void aCursorRoundTrips()
    {
        var parts = Cursor.Decode(Cursor.Encode(2000, "ASC"), 2);

        Assert.NotNull(parts);
        Assert.Equal(2000, Cursor.DecodeInt(parts![0]));
        Assert.Equal("ASC", parts[1]);
    }

    [Fact]
    public void aCompoundCursorRoundTrips()
    {
        var parts = Cursor.Decode(Cursor.Encode("Muster", "Hans", 300), 3);
        Assert.Equal(["Muster", "Hans", "300"], parts!.Select(part => part ?? "").ToArray());
    }

    [Fact]
    public void nullPartsSurviveTheRoundTrip()
    {
        var parts = Cursor.Decode(Cursor.Encode(null, 7), 2);
        Assert.Null(parts![0]);
        Assert.Equal("7", parts[1]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void noCursorDecodesToNull(string? cursor) =>
        Assert.Null(Cursor.Decode(cursor, 1));

    [Theory]
    [InlineData("not-base64!!")]
    [InlineData("YWJj")]  // valid base64, not the expected JSON
    public void malformedInputIsRejectedWithAClientError(string cursor) =>
        Assert.Throws<InvalidCursorException>(() => Cursor.Decode(cursor, 1));

    [Fact]
    public void aCursorWithTheWrongShapeIsRejected() =>
        Assert.Throws<InvalidCursorException>(() => Cursor.Decode(Cursor.Encode(1, 2), 3));

    [Fact]
    public void aNonNumericIdPartIsRejected() =>
        Assert.Throws<InvalidCursorException>(() => Cursor.DecodeInt("abc"));

    [Fact]
    public void theEncodingIsUrlSafe()
    {
        var cursor = Cursor.Encode("Müller-Ähnlich / Test+Wert", 12345);

        Assert.DoesNotContain('+', cursor);
        Assert.DoesNotContain('/', cursor);
        Assert.DoesNotContain('=', cursor);
    }
}
