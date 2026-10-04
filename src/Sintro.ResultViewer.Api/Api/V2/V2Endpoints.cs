using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Sintro.ResultViewer.Data;
using Sintro.ResultViewer.Domain;
using Sintro.ResultViewer.Live;
using Sintro.ResultViewer.Security;

namespace Sintro.ResultViewer.Api.V2;

/// <summary>v1 is the legacy Grapevine service, so the version is always explicit in the path.</summary>
public static class V2Endpoints
{
    public const string Version = "v2";
    public const string RoutePrefix = "/api/v2";

    // The numeric prefix is what the docs page sorts on: what is happening now, results, reference data, operations.
    public const string TagLive = "1 · Live";
    public const string TagResults = "2 · Resultate";
    public const string TagReference = "3 · Stammdaten";
    public const string TagOperations = "4 · Betrieb";

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
           .WithMetadata(new QueryTokenOnUpgrade())
           .WithTags(TagLive)
           .WithSummary("Lane state now — and the WebSocket for push updates")
           .WithDescription(V2Descriptions.Live);

    private static void MapResults(RouteGroupBuilder api)
    {
        api.MapGet("/programs", ListPrograms)
           .WithTags(TagResults)
           .WithSummary("Programs (Passen), newest first")
           .WithDescription(V2Descriptions.Programs);

        api.MapGet("/programs/{id:int}", GetProgram)
           .WithTags(TagResults)
           .WithSummary("One program with every series and shot")
           .WithDescription(V2Descriptions.Program);

        api.MapGet("/shooters", ListShooters)
           .WithTags(TagResults)
           .WithSummary("Registered shooters")
           .WithDescription(V2Descriptions.Shooters);

        api.MapGet("/shooters/{license}", GetShooter)
           .WithTags(TagResults)
           .WithSummary("Shooters on a licence number, with their programs")
           .WithDescription(V2Descriptions.Shooter);
    }

    private static void MapReference(RouteGroupBuilder api)
    {
        api.MapGet("/clubs", ListClubs)
           .WithTags(TagReference)
           .WithSummary("Swiss club register as held by the device")
           .WithDescription(V2Descriptions.Clubs);

        api.MapGet("/program-catalog", ListCatalog)
           .WithTags(TagReference)
           .WithSummary("Distinct (targetCode, targetProgram) pairs present, with counts")
           .WithDescription(V2Descriptions.Catalog);
    }

    private static void MapOperations(RouteGroupBuilder api) =>
        api.MapGet("/health", Health)
           .AllowAnonymous()
           .WithTags(TagOperations)
           .WithSummary("Database reachability")
           .WithDescription(V2Descriptions.Health);

    private static async Task<IResult> Live(
        HttpContext context,
        ISintroRepository repository,
        LiveHub hub,
        IHostApplicationLifetime lifetime,
        CancellationToken token)
    {
        if (!context.WebSockets.IsWebSocketRequest)
            return TypedResults.Ok(await repository.ListLanesAsync(token));

        // Read before upgrading: after the upgrade there is no HTTP response left to fail with.
        var snapshot = new LanesFrame(await repository.ListLanesAsync(token));

        // RequestAborted fires only after Kestrel's shutdown drain; without ApplicationStopping, Ctrl+C waits on every idle display.
        using var session = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.ApplicationStopping);
        using var socket = await context.WebSockets.AcceptWebSocketAsync();

        await hub.AcceptAsync(socket, snapshot, session.Token);

        return Results.Empty;
    }

    private static async Task<Results<Ok<CursorPage<ShootingProgram>>, BadRequest<ApiError>>> ListPrograms(
        ISintroRepository repository,
        ISintroClock clock,
        IOptions<SintroOptions> options,
        CancellationToken token,
        [FromQuery] string? state = null,
        [FromQuery] string? targetCode = null,
        [FromQuery] string? targetProgram = null,
        [FromQuery] string? matchCode = null,
        [FromQuery] string? license = null,
        [FromQuery] int? lane = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] bool withoutResult = false,
        [FromQuery] string? order = null,
        [FromQuery] string? cursor = null,
        [FromQuery] int? limit = null)
    {
        if (V2Query.ParseState(state, out var parsedState) is { } stateError) return TypedResults.BadRequest(stateError);
        if (V2Query.ParseOrder(order, out var ascending) is { } orderError) return TypedResults.BadRequest(orderError);
        if (V2Query.ParseIntList("targetCode", targetCode, out var targetCodes) is { } targetError) return TypedResults.BadRequest(targetError);
        if (V2Query.ParseIntList("matchCode", matchCode, out var matchCodes) is { } matchError) return TypedResults.BadRequest(matchError);

        // Today only unless a window or a cursor is given: the cursor is the position, and a stored one must not lose yesterday's late passes.
        var explicitWindow = from is not null || to is not null || !string.IsNullOrWhiteSpace(cursor);

        var filter = new ProgramFilter
        {
            State = parsedState,
            TargetCodes = targetCodes,
            TargetProgram = targetProgram,
            MatchCodes = matchCodes,
            Licenses = V2Query.SplitList(license),
            Lane = lane,
            From = explicitWindow ? from : clock.Today,
            To = explicitWindow ? to : clock.Today,
            WithoutResult = withoutResult,
            Ascending = ascending,
            Cursor = cursor,
            Limit = V2Query.ClampLimit(limit, options.Value),
        };

        return TypedResults.Ok(await repository.ListProgramsAsync(filter, token));
    }

    private static async Task<Results<Ok<ShootingProgram>, NotFound<ApiError>>> GetProgram(
        int id, ISintroRepository repository, CancellationToken token)
    {
        var program = await repository.GetProgramAsync(id, token);
        return program is null
            ? TypedResults.NotFound(new ApiError("not_found", $"No program with id {id}."))
            : TypedResults.Ok(program);
    }

    private static async Task<Ok<CursorPage<Shooter>>> ListShooters(
        ISintroRepository repository,
        IOptions<SintroOptions> options,
        CancellationToken token,
        [FromQuery] string? q = null,
        [FromQuery] int? club = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] string? cursor = null,
        [FromQuery] int? limit = null) =>
        TypedResults.Ok(await repository.ListShootersAsync(
            new ShooterFilter
            {
                Query = q,
                ClubId = club,
                From = from,
                To = to,
                Limit = V2Query.ClampLimit(limit, options.Value),
                Cursor = cursor,
            }, token));

    private static async Task<Results<Ok<ShooterDetail>, NotFound<ApiError>, BadRequest<ApiError>>> GetShooter(
        string license,
        ISintroRepository repository,
        IOptions<SintroOptions> options,
        CancellationToken token,
        [FromQuery] string? order = null,
        [FromQuery] string? cursor = null,
        [FromQuery] int? limit = null)
    {
        if (V2Query.ParseOrder(order, out var ascending) is { } orderError) return TypedResults.BadRequest(orderError);

        var shooters = await repository.FindShootersByLicenseAsync(license, token);
        if (shooters.Count == 0)
            return TypedResults.NotFound(new ApiError("not_found", "No shooter carries this licence number."));

        var programs = await repository.ListProgramsAsync(
            new ProgramFilter
            {
                Licenses = [license],
                WithoutResult = true,
                Ascending = ascending,
                Cursor = cursor,
                Limit = V2Query.ClampLimit(limit, options.Value),
            }, token);

        return TypedResults.Ok(new ShooterDetail(
            LicenseNumber.Normalize(license), shooters, programs));
    }

    private static async Task<Ok<CursorPage<Club>>> ListClubs(
        ISintroRepository repository,
        IOptions<SintroOptions> options,
        CancellationToken token,
        [FromQuery] string? q = null,
        [FromQuery] string? cursor = null,
        [FromQuery] int? limit = null) =>
        TypedResults.Ok(await repository.ListClubsAsync(
            q, V2Query.ClampLimit(limit, options.Value), cursor, token));

    private static async Task<Ok<IReadOnlyList<ProgramCatalogEntry>>> ListCatalog(
        ISintroRepository repository, CancellationToken token) =>
        TypedResults.Ok(await repository.ListProgramCatalogAsync(token));

    private static async Task<Results<Ok<HealthReport>, JsonHttpResult<ApiError>>> Health(
        ISintroRepository repository,
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
}
