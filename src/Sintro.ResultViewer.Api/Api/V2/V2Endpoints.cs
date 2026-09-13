using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Sintro.ResultViewer.Data;
using Sintro.ResultViewer.Domain;
using Sintro.ResultViewer.Live;
using Sintro.ResultViewer.Security;

namespace Sintro.ResultViewer.Api.V2;

/// <summary>API v2 routes. v1 is the legacy Grapevine service, so the version is always explicit in the path.</summary>
public static class V2Endpoints
{
    public const string Version = "v2";
    public const string RoutePrefix = "/api/v2";

    // The numeric prefix is what the docs page sorts on: what is happening now, results, reference data, operations.
    public const string TagLive = "1 · Live";
    public const string TagResults = "2 · Resultate";
    public const string TagReference = "3 · Stammdaten";
    public const string TagOperations = "4 · Betrieb";

    private const string CollectionHelp = """
        Collection endpoints are cursor-paged, never offset-paged: the device inserts while you
        read and prunes old rows from the other end, so an offset would skip or repeat records.

        Paging — read `nextCursor` from the response and pass it back as `cursor`; null means you
        reached the end. There is deliberately no total: counting the whole set costs a second
        scan per request and grows with the table, and paging never needs it. A cursor this API
        did not issue, or one issued for the other sort order, is answered with 400
        `invalid_cursor` rather than silently restarting from page one.

        Syncing — request `order=asc` and keep the last `nextCursor` you received. Passing it
        again later returns exactly the records added since, and nothing else. This is the
        supported way to mirror results into event software.

        Errors — every non-2xx answer carries `{"error": "<stable code>", "detail": "<text>"}`.
        """;

    public static void MapV2(this WebApplication app)
    {
        var api = app.MapGroup(RoutePrefix).AddEndpointFilter(RejectInvalidCursor);

        MapLive(api);
        MapResults(api);
        MapReference(api);
        MapOperations(api);
    }

    private static void MapLive(RouteGroupBuilder api) =>
        api.MapGet("/live", Live)
           .WithTags(TagLive)
           .WithSummary("Lane state now — and the WebSocket for push updates")
           .WithDescription("""
                Returns every lane the installation reports, in lane-number order, each with the
                program currently on it. A lane with nobody shooting has currentProgram: null, and
                lanes are always all listed so a display can show the full firing line.

                The same URL upgrades to a WebSocket. On connect it pushes the current lane state
                immediately, then a fresh {"type":"lanes","lanes":[...]} whenever a lane changes
                or a shot is fired. Browsers cannot set headers on a handshake, so the WebSocket
                upgrade — and only the upgrade — accepts the token as ?token=<token>.
                """);

    private static void MapResults(RouteGroupBuilder api)
    {
        api.MapGet("/programs", ListPrograms)
           .WithTags(TagResults)
           .WithSummary("Programs (Passen), newest first")
           .WithDescription($"""
                One program is one shooter shooting one program on one lane at one time — a
                "Passe". This is the main result endpoint.

                Defaults to today only; pass from and/or to for any other window. Programs with no
                counting shots (aborted or cleared runs) are omitted unless withoutResult=true.
                Use /live for what is being shot right now.

                state is one of: active (on a line, still being shot), finished (the device wrote
                an end total), abandoned (neither — started and dropped, or displaced when the
                line was reassigned; these almost always carry no shots). order is asc or desc.
                An unrecognised value for either is answered with 400 rather than ignored, so a
                typo cannot quietly widen what you receive.

                Each series carries a targetCode such as A10 or B4 — the target letter plus the
                ring scale, the same notation used in program names. Shots carry hitSector: 1 is
                twelve o'clock and the numbers run clockwise in 45 degree steps, 0 is a centre
                hit, and null means the device reported no sector.

                {CollectionHelp}
                """);

        api.MapGet("/programs/{id:int}", GetProgram)
           .WithTags(TagResults)
           .WithSummary("One program with every series and shot")
           .WithDescription("""
                Replace {id} in the path with a program id from /programs, for example
                /api/v2/programs/2000.

                total is null when the program's series used different ring scales — adding a 5er
                series to a 10er one is meaningless — and totalUnavailable says why. Use each
                series' subtotal in that case. sighting lists the Probe series, one per stage the
                device recorded them in; they never count towards the total.
                """);

        api.MapGet("/shooters", ListShooters)
           .WithTags(TagResults)
           .WithSummary("Registered shooters")
           .WithDescription($"""
                Registering a shooter is optional, so most programs have none. q matches surname,
                first name or licence number.

                {CollectionHelp}
                """);

        api.MapGet("/shooters/{license}", GetShooter)
           .WithTags(TagResults)
           .WithSummary("Shooters on a licence number, with their programs")
           .WithDescription("""
                Replace {license} in the path with a licence number from /shooters, for example
                /api/v2/shooters/123456. Leading zeros are optional — 12345 and 012345 resolve
                identically.

                Returns every shooter carrying the licence. The device schema places no unique
                constraint on it, so a collision is possible; each result carries
                duplicateLicense rather than one being silently chosen.

                programs is a cursor page like /programs, newest first, and includes passes with
                no shots. Page it the same way, with cursor and limit.
                """);
    }

    private static void MapReference(RouteGroupBuilder api)
    {
        api.MapGet("/clubs", ListClubs)
           .WithTags(TagReference)
           .WithSummary("Swiss club register as held by the device")
           .WithDescription($"q matches club name or club number.\n\n{CollectionHelp}");

        api.MapGet("/program-catalog", ListCatalog)
           .WithTags(TagReference)
           .WithSummary("Distinct (number, name) pairs present, with counts")
           .WithDescription("""
                Not a lookup table and not paged. The operator renames programs freely, so one
                number can appear under several names. Filter /programs by number and/or name
                using the pairs listed here. lastStartedAt is when a program of that pair was
                last started.
                """);
    }

    private static void MapOperations(RouteGroupBuilder api) =>
        api.MapGet("/health", Health)
           .WithTags(TagOperations)
           .WithSummary("Database reachability")
           .WithDescription("""
                The only data endpoint that needs no token, so monitoring can reach it. today is
                the date the today-only default resolves to. publicExposure lists any non-private
                network ranges the server is configured to accept. Answers 503 with the same body
                shape as every other error when the database cannot be reached.
                """);

    // -- Handlers -----------------------------------------------------------------

    private static async Task<IResult> Live(
        HttpContext context,
        SintroRepository repository,
        LiveHub hub,
        IHostApplicationLifetime lifetime,
        CancellationToken token)
    {
        if (!context.WebSockets.IsWebSocketRequest)
            return TypedResults.Ok(await repository.ListLanesAsync(token));

        // Read before upgrading: after the upgrade there is no HTTP response left to fail with.
        var snapshot = new { type = "lanes", lanes = await repository.ListLanesAsync(token) };

        // RequestAborted fires only after Kestrel's shutdown drain; without ApplicationStopping, Ctrl+C waits on every idle display.
        using var session = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.ApplicationStopping);
        using var socket = await context.WebSockets.AcceptWebSocketAsync();

        await hub.AcceptAsync(socket, snapshot, session.Token);

        return Results.Empty;
    }

    private static async Task<Results<Ok<CursorPage<ShootingProgram>>, BadRequest<ApiError>>> ListPrograms(
        SintroRepository repository,
        ISintroClock clock,
        IOptions<SintroOptions> options,
        CancellationToken token,
        [FromQuery] string? state = null,
        [FromQuery] int? number = null,
        [FromQuery] string? name = null,
        [FromQuery] string? license = null,
        [FromQuery] int? lane = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] bool withoutResult = false,
        [FromQuery] string? order = null,
        [FromQuery] string? cursor = null,
        [FromQuery] int? limit = null)
    {
        if (ParseState(state, out var parsedState) is { } stateError) return TypedResults.BadRequest(stateError);
        if (ParseOrder(order, out var ascending) is { } orderError) return TypedResults.BadRequest(orderError);

        // Today only unless a window is given; a pass that runs past midnight is filed under the day it started.
        var explicitWindow = from is not null || to is not null;

        var filter = new ProgramFilter
        {
            State = parsedState,
            Number = number,
            Name = name,
            License = license,
            Lane = lane,
            From = explicitWindow ? from : clock.Today,
            To = explicitWindow ? to : clock.Today,
            WithoutResult = withoutResult,
            Ascending = ascending,
            Cursor = cursor,
            Limit = ClampLimit(limit, options.Value),
        };

        return TypedResults.Ok(await repository.ListProgramsAsync(filter, token));
    }

    private static async Task<Results<Ok<ShootingProgram>, NotFound<ApiError>>> GetProgram(
        int id, SintroRepository repository, CancellationToken token)
    {
        var program = await repository.GetProgramAsync(id, token);
        return program is null
            ? TypedResults.NotFound(new ApiError("not_found", $"No program with id {id}."))
            : TypedResults.Ok(program);
    }

    private static async Task<Ok<CursorPage<Shooter>>> ListShooters(
        SintroRepository repository,
        IOptions<SintroOptions> options,
        CancellationToken token,
        [FromQuery] string? q = null,
        [FromQuery] int? club = null,
        [FromQuery] string? cursor = null,
        [FromQuery] int? limit = null) =>
        TypedResults.Ok(await repository.ListShootersAsync(
            q, club, ClampLimit(limit, options.Value), cursor, token));

    private static async Task<Results<Ok<ShooterDetail>, NotFound<ApiError>, BadRequest<ApiError>>> GetShooter(
        string license,
        SintroRepository repository,
        IOptions<SintroOptions> options,
        CancellationToken token,
        [FromQuery] string? order = null,
        [FromQuery] string? cursor = null,
        [FromQuery] int? limit = null)
    {
        if (ParseOrder(order, out var ascending) is { } orderError) return TypedResults.BadRequest(orderError);

        var shooters = await repository.FindShootersByLicenseAsync(license, token);
        if (shooters.Count == 0)
            return TypedResults.NotFound(new ApiError("not_found", "No shooter carries this licence number."));

        var programs = await repository.ListProgramsAsync(
            new ProgramFilter
            {
                License = license,
                WithoutResult = true,
                Ascending = ascending,
                Cursor = cursor,
                Limit = ClampLimit(limit, options.Value),
            }, token);

        return TypedResults.Ok(new ShooterDetail(
            LicenseNumber.Normalize(license), shooters, programs));
    }

    private static async Task<Ok<CursorPage<Club>>> ListClubs(
        SintroRepository repository,
        IOptions<SintroOptions> options,
        CancellationToken token,
        [FromQuery] string? q = null,
        [FromQuery] string? cursor = null,
        [FromQuery] int? limit = null) =>
        TypedResults.Ok(await repository.ListClubsAsync(
            q, ClampLimit(limit, options.Value), cursor, token));

    private static async Task<Ok<IReadOnlyList<ProgramCatalogEntry>>> ListCatalog(
        SintroRepository repository, CancellationToken token) =>
        TypedResults.Ok(await repository.ListProgramCatalogAsync(token));

    private static async Task<Results<Ok<HealthReport>, JsonHttpResult<ApiError>>> Health(
        SintroRepository repository,
        ISintroClock clock,
        LiveHub hub,
        IOptions<SintroOptions> options,
        CancellationToken token)
    {
        var reachable = await repository.CanReachDatabaseAsync(token);
        var report = new HealthReport(
            reachable, clock.Today, hub.ClientCount,
            NetworkGateExtensions.DescribePublicExposure(options.Value));

        return reachable
            ? TypedResults.Ok(report)
            : TypedResults.Json(
                new ApiError("database_unreachable", "Cannot reach the Sintro database."),
                statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    // -- Helpers ------------------------------------------------------------------

    /// <summary>Cursors are decoded in the repository, where their shape is known, so the 400 for a bad one is produced here for every collection at once.</summary>
    private static async ValueTask<object?> RejectInvalidCursor(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (InvalidCursorException ex)
        {
            return TypedResults.BadRequest(new ApiError("invalid_cursor", ex.Message));
        }
    }

    private static int ClampLimit(int? limit, SintroOptions settings) =>
        Math.Clamp(limit ?? settings.DefaultPageSize, 1, settings.MaxPageSize);

    // Both return the 400 body for an unrecognised value and null when accepted: a misspelled filter is rejected,
    // never ignored, because ?state=finishd returning everything is the opposite of what was asked for.
    private static ApiError? ParseState(string? state, out ProgramState? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(state)) return null;

        parsed = state.Trim().ToLowerInvariant() switch
        {
            "active" => ProgramState.Active,
            "finished" => ProgramState.Finished,
            "abandoned" => ProgramState.Abandoned,
            _ => null,
        };

        return parsed is null
            ? new ApiError("invalid_state", $"Unknown state '{state}'. Expected one of: active, finished, abandoned.")
            : null;
    }

    private static ApiError? ParseOrder(string? order, out bool ascending)
    {
        ascending = false;
        if (string.IsNullOrWhiteSpace(order)) return null;

        switch (order.Trim().ToLowerInvariant())
        {
            case "asc": ascending = true; return null;
            case "desc": return null;
            default: return new ApiError("invalid_order", $"Unknown order '{order}'. Expected 'asc' or 'desc'.");
        }
    }
}
