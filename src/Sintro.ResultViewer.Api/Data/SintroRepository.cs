using Dapper;
using Microsoft.Data.SqlClient;
using Sintro.ResultViewer.Domain;

namespace Sintro.ResultViewer.Data;

/// <summary>Every SQL statement against the device database lives here; read-only throughout.</summary>
public sealed class SintroRepository(string connectionString, ISintroClock clock)
{
    // StartTime is day-first text, so it is converted before any filter or sort. CountingShots skips
    // markers by ShotNr 9999 only: the last real shot carries TotalType 7 as well (see ScoreCalculator).
    private const string ProgramProjection = """
        WITH prog AS (
            SELECT  pr.ProgramID, pr.Number, pr.Name, pr.StartTime, pr.LaneNr,
                    pr.ContestShooterName, pr.ShooterID,
                    TRY_CONVERT(datetime2, REPLACE(pr.StartTime, '-', ' '), 104) AS StartedAt
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
            SELECT  p.ProgramID, p.Number, p.Name, p.StartTime, p.LaneNr,
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
          AND   (@number      IS NULL OR e.Number = @number)
          AND   (@lane        IS NULL OR e.LaneNr = @lane)
          AND   (@name        IS NULL OR e.Name LIKE @name ESCAPE '\')
          AND   (@activeOnly  = 0 OR e.IsActive = 1)
          AND   (@finishedOnly = 0 OR e.EndShotId IS NOT NULL)
          AND   (@abandonedOnly = 0 OR (e.EndShotId IS NULL AND e.IsActive = 0))
          AND   (@withoutResult = 1 OR e.CountingShots > 0)
          AND   (@anyShooter   = 0 OR e.ShooterID IN @shooterIds)
        """;

    private const string ShooterProjection = """
        SELECT  sh.ShooterID, sh.FirstName, sh.LastName, sh.StartNr, sh.ClubID,
                c.ClubNumber, c.ClubName
        FROM    dbo.Shooters sh
        LEFT JOIN dbo.Club c ON c.ClubID = sh.ClubID
        """;

    private const string LaneQuery = "SELECT Number, ProgramID FROM dbo.Lanes ORDER BY Number";

    private SqlConnection Connect() => new(connectionString);

    // -- Programs -----------------------------------------------------------------

    public async Task<CursorPage<ShootingProgram>> ListProgramsAsync(ProgramFilter filter, CancellationToken token)
    {
        await using var connection = Connect();

        var licenses = filter.License is { Length: > 0 } ? await LicenseIndex.LoadAsync(connection, token) : null;
        var shooterIds = licenses?.Resolve(filter.License) ?? [];
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
        parameters.Add("number", filter.Number);
        parameters.Add("lane", filter.Lane);
        parameters.Add("name", ContainsPattern(filter.Name));
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
                    ShotType, ShotTime, Mouche, X, Y, TotalType, ShotGroup
            FROM    dbo.Shots
            WHERE   ProgramID IN @programIds
            ORDER BY ShotID
            """, new { programIds }, cancellationToken: token));

        return rows.GroupBy(row => row.ProgramID!.Value)
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

        licenses ??= await LicenseIndex.LoadAsync(connection, token);
        var shooters = await LoadShootersByIdAsync(connection, shooterIds, licenses, token);
        return shooters.ToDictionary(shooter => shooter.ShooterId);
    }

    private ShootingProgram ToProgram(
        ProgramRow row, List<ShotRow> shots, List<TargetInfoRow> targets, Shooter? shooter)
    {
        // Epoch when neither SQL nor C# could parse StartTime, so bad data is visibly wrong rather than missing.
        var start = row.StartedAt ?? SintroTime.ParseStartTime(row.StartTime) ?? DateTime.UnixEpoch;
        var score = ScoreCalculator.Calculate(start, clock, shots, targets);

        return new ShootingProgram(
            Id: row.ProgramID,
            Number: row.Number,
            Name: row.Name,
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

    // -- Lanes --------------------------------------------------------------------

    public async Task<IReadOnlyList<LaneStatus>> ListLanesAsync(CancellationToken token)
    {
        await using var connection = Connect();

        var lanes = (await connection.QueryAsync<LaneRow>(new CommandDefinition(LaneQuery, cancellationToken: token))).ToList();
        var programIds = lanes.Where(lane => lane.ProgramID.HasValue)
                              .Select(lane => lane.ProgramID!.Value)
                              .Distinct().ToList();
        var programs = await LoadProgramsByIdAsync(connection, programIds, token);

        return lanes.Select(lane => new LaneStatus(
            lane.Number,
            lane.ProgramID is int id ? programs.GetValueOrDefault(id) : null)).ToList();
    }

    /// <summary>Cheap change-detection payload for the live feed, deliberately not the full lane view.</summary>
    public async Task<string> ReadLiveFingerprintAsync(CancellationToken token)
    {
        await using var connection = Connect();

        var lanes = await connection.QueryAsync<LaneRow>(new CommandDefinition(LaneQuery, cancellationToken: token));
        var maxShot = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT ISNULL(MAX(ShotID), 0) FROM dbo.Shots", cancellationToken: token));

        return string.Join(',', lanes.Select(lane => $"{lane.Number}:{lane.ProgramID}")) + $"|{maxShot}";
    }

    // -- Shooters & clubs ---------------------------------------------------------

    public async Task<CursorPage<Shooter>> ListShootersAsync(
        string? query, int? clubId, int limit, string? cursor, CancellationToken token)
    {
        await using var connection = Connect();

        var parameters = new DynamicParameters();
        parameters.Add("query", ContainsPattern(query));
        parameters.Add("clubId", clubId);
        var cursorClause = ShooterCursorClause(cursor, parameters);

        var (rows, nextCursor, hasMore) = await QueryPageAsync<ShooterRow>(connection, $"""
            {ShooterProjection}
            WHERE   (@clubId IS NULL OR sh.ClubID = @clubId)
              AND   (@query  IS NULL
                     OR sh.LastName  LIKE @query ESCAPE '\'
                     OR sh.FirstName LIKE @query ESCAPE '\'
                     OR sh.StartNr   LIKE @query ESCAPE '\')
            {cursorClause}
            ORDER BY ISNULL(sh.LastName, ''), ISNULL(sh.FirstName, ''), sh.ShooterID
            """, parameters, limit, row => Cursor.Encode(row.LastName, row.FirstName, row.ShooterID), token);

        var licenses = await LicenseIndex.LoadAsync(connection, token);
        return new CursorPage<Shooter>(rows.Select(row => ToShooter(row, licenses)).ToList(), nextCursor, hasMore);
    }

    // Names are not unique, so the keyset is the full sort tuple tie-broken by the primary key. ISNULL mirrors the
    // cursor's "" for a missing part: a NULL would compare UNKNOWN and skip rows silently.
    private static string ShooterCursorClause(string? cursor, DynamicParameters parameters)
    {
        if (Cursor.Decode(cursor, 3) is not { } parts) return string.Empty;

        parameters.Add("cursorLastName", parts[0] ?? "");
        parameters.Add("cursorFirstName", parts[1] ?? "");
        parameters.Add("cursorShooterId", Cursor.DecodeInt(parts[2]));
        return """
            AND (ISNULL(sh.LastName, '') > @cursorLastName
                 OR (ISNULL(sh.LastName, '') = @cursorLastName AND ISNULL(sh.FirstName, '') > @cursorFirstName)
                 OR (ISNULL(sh.LastName, '') = @cursorLastName AND ISNULL(sh.FirstName, '') = @cursorFirstName
                     AND sh.ShooterID > @cursorShooterId))
            """;
    }

    /// <summary>Every shooter on the licence: StartNr has no unique constraint, so all matches come back flagged rather than one guessed.</summary>
    public async Task<IReadOnlyList<Shooter>> FindShootersByLicenseAsync(string license, CancellationToken token)
    {
        await using var connection = Connect();

        var licenses = await LicenseIndex.LoadAsync(connection, token);
        var shooterIds = licenses.Resolve(license);
        return shooterIds.Count == 0 ? [] : await LoadShootersByIdAsync(connection, shooterIds, licenses, token);
    }

    private static async Task<List<Shooter>> LoadShootersByIdAsync(
        SqlConnection connection, List<int> shooterIds, LicenseIndex licenses, CancellationToken token)
    {
        var rows = await connection.QueryAsync<ShooterRow>(new CommandDefinition(
            $"{ShooterProjection} WHERE sh.ShooterID IN @shooterIds ORDER BY sh.ShooterID",
            new { shooterIds }, cancellationToken: token));

        return rows.Select(row => ToShooter(row, licenses)).ToList();
    }

    public async Task<CursorPage<Club>> ListClubsAsync(
        string? query, int limit, string? cursor, CancellationToken token)
    {
        await using var connection = Connect();

        var parameters = new DynamicParameters();
        parameters.Add("query", ContainsPattern(query));
        var cursorClause = ClubCursorClause(cursor, parameters);

        var (rows, nextCursor, hasMore) = await QueryPageAsync<ClubRow>(connection, $"""
            SELECT c.ClubID, c.ClubNumber, c.ClubName
            FROM   dbo.Club c
            WHERE  (@query IS NULL
                    OR c.ClubName   LIKE @query ESCAPE '\'
                    OR c.ClubNumber LIKE @query ESCAPE '\')
            {cursorClause}
            ORDER BY ISNULL(c.ClubName, ''), c.ClubID
            """, parameters, limit, row => Cursor.Encode(row.ClubName, row.ClubID), token);

        return new CursorPage<Club>(rows.Select(row => ToClub(row.ClubID, row.ClubNumber, row.ClubName)).ToList(), nextCursor, hasMore);
    }

    private static string ClubCursorClause(string? cursor, DynamicParameters parameters)
    {
        if (Cursor.Decode(cursor, 2) is not { } parts) return string.Empty;

        parameters.Add("cursorClubName", parts[0] ?? "");
        parameters.Add("cursorClubId", Cursor.DecodeInt(parts[1]));
        return """
            AND (ISNULL(c.ClubName, '') > @cursorClubName
                 OR (ISNULL(c.ClubName, '') = @cursorClubName AND c.ClubID > @cursorClubId))
            """;
    }

    /// <summary>(Number, Name) pairs present. Not a lookup table: operators rename programs, so one number can appear under several names.</summary>
    public async Task<IReadOnlyList<ProgramCatalogEntry>> ListProgramCatalogAsync(CancellationToken token)
    {
        await using var connection = Connect();

        var rows = await connection.QueryAsync<CatalogRow>(new CommandDefinition("""
            SELECT   pr.Number,
                     pr.Name,
                     COUNT(*) AS ProgramCount,
                     MAX(TRY_CONVERT(datetime2, REPLACE(pr.StartTime, '-', ' '), 104)) AS LastStartedAt
            FROM     dbo.Programs pr
            GROUP BY pr.Number, pr.Name
            ORDER BY pr.Number, pr.Name
            """, cancellationToken: token));

        return rows.Select(row => new ProgramCatalogEntry(
            row.Number,
            row.Name,
            row.ProgramCount,
            row.LastStartedAt is null ? null : clock.ToOffset(row.LastStartedAt.Value))).ToList();
    }

    public async Task<bool> CanReachDatabaseAsync(CancellationToken token)
    {
        try
        {
            await using var connection = Connect();
            return await connection.ExecuteScalarAsync<int>(
                new CommandDefinition("SELECT 1", cancellationToken: token)) == 1;
        }
        // SqlClient reports pool exhaustion and connect timeouts as InvalidOperationException; health must answer 503, not crash.
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            return false;
        }
    }

    // -- Helpers ------------------------------------------------------------------

    /// <summary>Runs an ordered query fetching one row beyond <paramref name="limit"/>: the look-ahead row says whether a next page exists and is then dropped.</summary>
    private static async Task<(List<TRow> Rows, string? NextCursor, bool HasMore)> QueryPageAsync<TRow>(
        SqlConnection connection, string orderedSql, DynamicParameters parameters, int limit,
        Func<TRow, string> encodeCursor, CancellationToken token)
    {
        parameters.Add("fetch", limit + 1);
        var rows = (await connection.QueryAsync<TRow>(new CommandDefinition(
            $"{orderedSql} OFFSET 0 ROWS FETCH NEXT @fetch ROWS ONLY", parameters, cancellationToken: token))).ToList();

        var hasMore = rows.Count > limit;
        if (hasMore) rows.RemoveAt(rows.Count - 1);

        return (rows, rows.Count > 0 ? encodeCursor(rows[^1]) : null, hasMore);
    }

    // LIKE treats %, _ and [ as wildcards; escaping them makes the caller's text match literally.
    private static string? ContainsPattern(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var escaped = text.Trim()
            .Replace(@"\", @"\\")
            .Replace("%", @"\%")
            .Replace("_", @"\_")
            .Replace("[", @"\[");

        return $"%{escaped}%";
    }

    /// <summary>Every licence in dbo.Shooters, normalised; matching in C# keeps the normalisation rule in one place.</summary>
    private sealed class LicenseIndex(Dictionary<string, List<int>> shootersByLicense)
    {
        public static async Task<LicenseIndex> LoadAsync(SqlConnection connection, CancellationToken token)
        {
            var rows = await connection.QueryAsync<(int ShooterID, string? StartNr)>(new CommandDefinition(
                "SELECT ShooterID, StartNr FROM dbo.Shooters", cancellationToken: token));

            var index = rows
                .Select(row => (row.ShooterID, License: LicenseNumber.Normalize(row.StartNr)))
                .Where(entry => entry.License.Length > 0)
                .GroupBy(entry => entry.License)
                .ToDictionary(group => group.Key, group => group.Select(entry => entry.ShooterID).ToList());

            return new LicenseIndex(index);
        }

        public List<int> Resolve(string? license)
        {
            var normalized = LicenseNumber.Normalize(license);
            return normalized.Length == 0 ? [] : shootersByLicense.GetValueOrDefault(normalized, []);
        }

        public bool IsDuplicate(string normalizedLicense) =>
            shootersByLicense.TryGetValue(normalizedLicense, out var ids) && ids.Count > 1;
    }

    private static Shooter ToShooter(ShooterRow row, LicenseIndex licenses)
    {
        var license = LicenseNumber.Normalize(row.StartNr);

        return new Shooter(
            License: license,
            FirstName: row.FirstName?.Trim() ?? "",
            LastName: row.LastName?.Trim() ?? "",
            ShooterId: row.ShooterID,
            Club: row.ClubID is int clubId
                ? ToClub(clubId, row.ClubNumber, row.ClubName)
                : null,
            DuplicateLicense: licenses.IsDuplicate(license));
    }

    // The shipped club register carries trailing CR characters in its names.
    private static Club ToClub(int id, string? number, string? name) =>
        new(id, number?.Trim() ?? "", name?.Trim() ?? "");
}
