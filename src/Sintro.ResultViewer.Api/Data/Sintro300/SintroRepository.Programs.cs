using Dapper;
using Microsoft.Data.SqlClient;
using Sintro.ResultViewer.Domain;

namespace Sintro.ResultViewer.Data.Sintro300;

public sealed partial class SintroRepository
{
    // StartTime is day-first text, so it is converted before any filter or sort. CountingShots skips
    // markers by ShotNr 9999 only: the last real shot carries TotalType 7 as well (see ScoreCalculator).
    private static readonly string ProgramProjection = $"""
        WITH prog AS (
            SELECT  pr.ProgramID, pr.Number, pr.Name, pr.LaneNr,
                    pr.ContestShooterName, pr.ShooterID,
                    {string.Format(StartedAtSql, "pr.StartTime")} AS StartedAt
            FROM    dbo.Programs pr
        ),
        flags AS (
            SELECT  s.ProgramID,
                    MAX(CASE WHEN s.TotalType = 7 THEN s.ShotID END) AS EndShotId,
                    SUM(CASE WHEN s.ShotNr <> 9999 AND s.ShotType <> 0 THEN 1 ELSE 0 END) AS CountingShots
            FROM    dbo.Shots s
            WHERE   s.ProgramID IS NOT NULL
            GROUP BY s.ProgramID
        ),
        onlane AS (
            SELECT DISTINCT ProgramID FROM dbo.Lanes WHERE ProgramID IS NOT NULL
        ),
        enriched AS (
            SELECT  p.ProgramID, p.Number, p.Name, p.LaneNr,
                    p.ContestShooterName, p.ShooterID, p.StartedAt,
                    f.EndShotId,
                    ISNULL(f.CountingShots, 0)  AS CountingShots,
                    CASE WHEN o.ProgramID IS NOT NULL AND f.EndShotId IS NULL
                         THEN 1 ELSE 0 END      AS IsActive
            FROM    prog p
            LEFT JOIN flags  f ON f.ProgramID = p.ProgramID
            LEFT JOIN onlane o ON o.ProgramID = p.ProgramID
        )
        """;

    // A StartTime that does not parse leaves StartedAt NULL and the program out of every date window;
    // it stays reachable by id, where its start is reported as the epoch so the bad data is visible.
    private const string ProgramWhere = """
        WHERE   (@from        IS NULL OR CONVERT(date, e.StartedAt) >= @from)
          AND   (@to          IS NULL OR CONVERT(date, e.StartedAt) <= @to)
          AND   (@anyTargetCode = 0 OR e.Number IN @targetCodes)
          AND   (@anyMatchCode = 0 OR EXISTS (SELECT 1 FROM dbo.Shots s
                                            WHERE s.ProgramID = e.ProgramID AND s.ShotNr <> 9999
                                              AND s.ExternalNumber IN @matchCodes))
          AND   (@lane        IS NULL OR e.LaneNr = @lane)
          AND   (@targetProgram IS NULL OR e.Name LIKE @targetProgram ESCAPE '\')
          AND   (@activeOnly  = 0 OR e.IsActive = 1)
          AND   (@finishedOnly = 0 OR e.EndShotId IS NOT NULL)
          AND   (@abandonedOnly = 0 OR (e.EndShotId IS NULL AND e.IsActive = 0))
          AND   (@withoutResult = 1 OR e.CountingShots > 0)
          AND   (@anyShooter   = 0 OR e.ShooterID IN @shooterIds)
        """;

    public async Task<CursorPage<ShootingProgram>> ListProgramsAsync(ProgramFilter filter, CancellationToken token)
    {
        await using var connection = Connect();

        var licenses = filter.Licenses.Count > 0 ? await LoadLicenseIndexAsync(connection, token) : null;
        var shooterIds = filter.Licenses.SelectMany(license => licenses!.Resolve(license)).Distinct().ToList();
        // A licence matching nobody must return nothing; left to the SQL, @anyShooter = 0 would return everything.
        if (licenses is not null && shooterIds.Count == 0) return new CursorPage<ShootingProgram>([], null, false);

        var parameters = ProgramFilterParameters(filter, shooterIds);
        var direction = filter.Ascending ? "ASC" : "DESC";

        // Finished passes are listed in finishing order: a pass that ends late must still arrive after a
        // syncing client's cursor. Everything else is start order (ProgramID is an identity column).
        var finishedOnly = filter.State == ProgramState.Finished;
        var keyColumn = finishedOnly ? "EndShotId" : "ProgramID";
        Func<ProgramRow, int?> keyOf = finishedOnly ? row => row.EndShotId : row => row.ProgramID;
        var cursorClause = ProgramCursorClause(filter, keyColumn, direction, parameters);

        var (rows, nextCursor, hasMore) = await QueryPageAsync<ProgramRow>(connection, $"""
            {ProgramProjection}
            SELECT  e.*
            FROM    enriched e
            {ProgramWhere}
            {cursorClause}
            ORDER BY e.{keyColumn} {direction}
            """, parameters, filter.Limit, row => Cursor.Encode(keyOf(row), direction, keyColumn), token);

        var programs = await HydrateAsync(connection, rows, licenses, token);
        return new CursorPage<ShootingProgram>(programs, nextCursor, hasMore);
    }

    private static DynamicParameters ProgramFilterParameters(ProgramFilter filter, List<int> shooterIds)
    {
        var parameters = new DynamicParameters();
        parameters.Add("from", filter.From?.ToDateTime(TimeOnly.MinValue).Date);
        parameters.Add("to", filter.To?.ToDateTime(TimeOnly.MinValue).Date);
        parameters.Add("anyTargetCode", filter.TargetCodes.Count > 0 ? 1 : 0);
        parameters.Add("targetCodes", filter.TargetCodes.Count > 0 ? filter.TargetCodes : [0]);
        parameters.Add("anyMatchCode", filter.MatchCodes.Count > 0 ? 1 : 0);
        parameters.Add("matchCodes", filter.MatchCodes.Count > 0 ? filter.MatchCodes : [0]);
        parameters.Add("lane", filter.Lane);
        parameters.Add("targetProgram", ContainsPattern(filter.TargetProgram));
        parameters.Add("activeOnly", filter.State == ProgramState.Active ? 1 : 0);
        parameters.Add("finishedOnly", filter.State == ProgramState.Finished ? 1 : 0);
        parameters.Add("abandonedOnly", filter.State == ProgramState.Abandoned ? 1 : 0);
        parameters.Add("withoutResult", filter.WithoutResult ? 1 : 0);
        parameters.Add("anyShooter", shooterIds.Count > 0 ? 1 : 0);
        parameters.Add("shooterIds", shooterIds.Count > 0 ? shooterIds : new List<int> { 0 });
        return parameters;
    }

    // A cursor replayed under another order or key would return the wrong half of the list, so it is refused.
    private static string ProgramCursorClause(ProgramFilter filter, string keyColumn, string direction, DynamicParameters parameters)
    {
        if (Cursor.Decode(filter.Cursor, 3) is not { } parts) return string.Empty;

        if (parts[1] != direction || parts[2] != keyColumn)
            throw new InvalidCursorException(
                $"This cursor was issued for order={parts[1]?.ToLowerInvariant()}" +
                (parts[2] == "EndShotId" ? " with state=finished" : " without state=finished") +
                "; pass the same order and state to continue from it.");

        parameters.Add("cursorKey", Cursor.DecodeInt(parts[0]));
        return $"AND e.{keyColumn} {(filter.Ascending ? ">" : "<")} @cursorKey";
    }

    public async Task<ShootingProgram?> GetProgramAsync(int id, CancellationToken token)
    {
        await using var connection = Connect();

        var rows = (await connection.QueryAsync<ProgramRow>(new CommandDefinition(
            $"{ProgramProjection} SELECT e.* FROM enriched e WHERE e.ProgramID = @id",
            new { id }, cancellationToken: token))).ToList();

        return (await HydrateAsync(connection, rows, null, token)).FirstOrDefault();
    }

    private async Task<Dictionary<int, ShootingProgram>> LoadProgramsByIdAsync(
        SqlConnection connection, List<int> programIds, CancellationToken token)
    {
        if (programIds.Count == 0) return new();

        var rows = (await connection.QueryAsync<ProgramRow>(new CommandDefinition(
            $"{ProgramProjection} SELECT e.* FROM enriched e WHERE e.ProgramID IN @programIds",
            new { programIds }, cancellationToken: token))).ToList();

        return (await HydrateAsync(connection, rows, null, token)).ToDictionary(program => program.Id);
    }

    /// <summary>Loads shots, target info and shooters for a page of programs in set-based queries, then scores each one.</summary>
    private async Task<List<ShootingProgram>> HydrateAsync(
        SqlConnection connection, List<ProgramRow> rows, LicenseIndex? licenses, CancellationToken token)
    {
        if (rows.Count == 0) return [];

        var programIds = rows.Select(row => row.ProgramID).ToList();
        var shots = await LoadShotsAsync(connection, programIds, token);
        var targets = await LoadTargetInfoAsync(connection, programIds, token);
        var shooters = await LoadProgramShootersAsync(connection, rows, licenses, token);

        return rows.Select(row => ToProgram(
                    row,
                    shots.GetValueOrDefault(row.ProgramID, []),
                    targets.GetValueOrDefault(row.ProgramID, []),
                    row.ShooterID is int id ? shooters.GetValueOrDefault(id) : null))
                   .ToList();
    }

    private static async Task<Dictionary<int, List<ShotRow>>> LoadShotsAsync(
        SqlConnection connection, List<int> programIds, CancellationToken token)
    {
        var rows = await connection.QueryAsync<ShotRow>(new CommandDefinition("""
            SELECT  ShotID, ProgramID, ShotNr, PrimaryResult, SecondaryResult, HitPosition,
                    ShotType, ShotTime, Mouche, X, Y, TotalType, ShotGroup, ExternalNumber
            FROM    dbo.Shots
            WHERE   ProgramID IN @programIds
            ORDER BY ShotID
            """, new { programIds }, cancellationToken: token));

        return rows.GroupBy(row => row.ProgramID)
                   .ToDictionary(group => group.Key, group => group.ToList());
    }

    private static async Task<Dictionary<int, List<TargetInfoRow>>> LoadTargetInfoAsync(
        SqlConnection connection, List<int> programIds, CancellationToken token)
    {
        var rows = await connection.QueryAsync<TargetInfoRow>(new CommandDefinition("""
            SELECT  TargeinformationID, ProgramID, ShotGroup, TargetValuation, TargetType
            FROM    dbo.Targetinformation
            WHERE   ProgramID IN @programIds
            """, new { programIds }, cancellationToken: token));

        return rows.GroupBy(row => row.ProgramID)
                   .ToDictionary(group => group.Key, group => group.ToList());
    }

    private static async Task<Dictionary<int, Shooter>> LoadProgramShootersAsync(
        SqlConnection connection, List<ProgramRow> rows, LicenseIndex? licenses, CancellationToken token)
    {
        var shooterIds = rows.Where(row => row.ShooterID.HasValue)
                             .Select(row => row.ShooterID!.Value)
                             .Distinct().ToList();
        if (shooterIds.Count == 0) return new();

        licenses ??= await LoadLicenseIndexAsync(connection, token);
        var shooters = await LoadShootersByIdAsync(connection, shooterIds, licenses, token);
        return shooters.ToDictionary(shooter => shooter.ShooterId);
    }

    private ShootingProgram ToProgram(
        ProgramRow row, List<ShotRow> shots, List<TargetInfoRow> targets, Shooter? shooter)
    {
        var start = StartOf(row);
        var score = ScoreCalculator.Calculate(start, clock, shots, targets);

        return new ShootingProgram(
            Id: row.ProgramID,
            TargetCode: row.Number,
            TargetProgram: row.Name,
            Lane: row.LaneNr,
            StartedAt: clock.ToOffset(start),
            FinishedAt: FinishedAt(start, shots),
            State: StateOf(row),
            Shooter: shooter,
            ContestShooterName: string.IsNullOrWhiteSpace(row.ContestShooterName)
                ? null
                : row.ContestShooterName.Trim(),
            Total: score.Total,
            TotalUnavailable: score.TotalUnavailable,
            ShotCount: score.ShotCount,
            ShotValues: score.ShotValues,
            Series: score.Series,
            Sighting: score.Sighting);
    }

    // From the marker with the highest ShotID, not a MAX over the time text: past midnight "23:59" sorts after "00:03".
    private DateTimeOffset? FinishedAt(DateTime start, List<ShotRow> shots) =>
        ScoreCalculator.FindEndShotTime(shots) is { } endTime &&
        SintroTime.CombineShotTime(start, endTime) is { } end
            ? clock.ToOffset(end)
            : null;

    private static ProgramState StateOf(ProgramRow row) =>
        row.IsActive == 1 ? ProgramState.Active
        : row.EndShotId is not null ? ProgramState.Finished
        : ProgramState.Abandoned;
}
