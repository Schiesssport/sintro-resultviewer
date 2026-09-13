using Dapper;
using Microsoft.Data.SqlClient;
using Sintro.ResultViewer.Api.V2;
using Sintro.ResultViewer.Domain;

namespace Sintro.ResultViewer.Data;

/// <summary>
/// Every SQL statement against the device database lives in this file, so the mapping rules
/// documented in docs/device-database.md can be verified in one place. Read-only throughout —
/// there is no code path here that writes.
/// </summary>
public sealed class SintroRepository(string connectionString, ISintroClock clock)
{
    /// <summary>
    /// Programs.StartTime is text in German day-first format, so it is converted before it is
    /// filtered or sorted. IsActive combines "sitting on a lane" with "no end-of-program total".
    /// </summary>
    private const string ProgramProjection = """
        WITH prog AS (
            SELECT  pr.ProgramID, pr.Number, pr.Name, pr.StartTime, pr.LaneNr,
                    pr.ContestShooterName, pr.ShooterID,
                    TRY_CONVERT(datetime2, REPLACE(pr.StartTime, '-', ' '), 104) AS StartedAt
            FROM    dbo.Programs pr
        ),
        flags AS (
            SELECT  s.ProgramID,
                    MAX(CASE WHEN s.TotalType = 7 THEN 1 ELSE 0 END) AS HasEndMarker,
                    SUM(CASE WHEN s.ShotNr <> 9999 AND s.ShotType <> 0 THEN 1 ELSE 0 END) AS CountingShots,
                    MAX(CASE WHEN s.TotalType = 7 THEN s.ShotTime END) AS EndShotTime
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
                    ISNULL(f.HasEndMarker, 0)   AS HasEndMarker,
                    ISNULL(f.CountingShots, 0)  AS CountingShots,
                    f.EndShotTime,
                    CASE WHEN o.ProgramID IS NOT NULL AND ISNULL(f.HasEndMarker, 0) = 0
                         THEN 1 ELSE 0 END      AS IsActive
            FROM    prog p
            LEFT JOIN flags  f ON f.ProgramID = p.ProgramID
            LEFT JOIN onlane o ON o.ProgramID = p.ProgramID
        )
        """;

    private const string ProgramWhere = """
        WHERE   (@from        IS NULL OR CONVERT(date, e.StartedAt) >= @from)
          AND   (@to          IS NULL OR CONVERT(date, e.StartedAt) <= @to)
          AND   (@number      IS NULL OR e.Number = @number)
          AND   (@lane        IS NULL OR e.LaneNr = @lane)
          AND   (@name        IS NULL OR e.Name LIKE '%' + @name + '%')
          AND   (@activeOnly  = 0 OR e.IsActive = 1)
          AND   (@finishedOnly = 0 OR e.HasEndMarker = 1)
          AND   (@abandonedOnly = 0 OR (e.HasEndMarker = 0 AND e.IsActive = 0))
          AND   (@withoutResult = 1 OR e.CountingShots > 0)
          AND   (@anyShooter   = 0 OR e.ShooterID IN @shooterIds)
        """;

    private SqlConnection Connect() => new(connectionString);

    // -- Programs -----------------------------------------------------------------

    public async Task<CursorPage<ShootingProgram>> ListProgramsAsync(ProgramFilter filter, CancellationToken token)
    {
        await using var connection = Connect();

        var shooterIds = await ResolveLicenseAsync(connection, filter.License, token);
        // A licence filter that matches nobody must return nothing, not everything.
        if (filter.License is { Length: > 0 } && shooterIds.Count == 0)
            return new CursorPage<ShootingProgram>([], null, filter.Limit);

        var parameters = new DynamicParameters();
        parameters.Add("from", filter.From?.ToDateTime(TimeOnly.MinValue).Date);
        parameters.Add("to", filter.To?.ToDateTime(TimeOnly.MinValue).Date);
        parameters.Add("number", filter.Number);
        parameters.Add("lane", filter.Lane);
        parameters.Add("name", string.IsNullOrWhiteSpace(filter.Name) ? null : filter.Name.Trim());
        parameters.Add("activeOnly", filter.State == ProgramState.Active ? 1 : 0);
        parameters.Add("finishedOnly", filter.State == ProgramState.Finished ? 1 : 0);
        parameters.Add("abandonedOnly", filter.State == ProgramState.Abandoned ? 1 : 0);
        parameters.Add("withoutResult", filter.WithoutResult ? 1 : 0);
        parameters.Add("anyShooter", shooterIds.Count > 0 ? 1 : 0);
        parameters.Add("shooterIds", shooterIds.Count > 0 ? shooterIds : new List<int> { 0 });

        // ProgramID is the keyset key: it is an identity column and, verified across the whole
        // dataset, perfectly monotonic with StartTime — so ordering by it equals ordering by
        // time while staying stable as rows are inserted and pruned during paging.
        var direction = filter.Ascending ? "ASC" : "DESC";
        var comparison = filter.Ascending ? ">" : "<";

        var cursorClause = string.Empty;
        if (Cursor.TryDecodeInt(filter.Cursor, out var cursorId))
        {
            parameters.Add("cursorId", cursorId);
            cursorClause = $"AND e.ProgramID {comparison} @cursorId";
        }

        // One extra row reveals whether a further page exists, without a second query.
        parameters.Add("fetch", filter.Limit + 1);

        var rows = (await connection.QueryAsync<ProgramRow>(new CommandDefinition($"""
            {ProgramProjection}
            SELECT  e.*
            FROM    enriched e
            {ProgramWhere}
            {cursorClause}
            ORDER BY e.ProgramID {direction}
            OFFSET 0 ROWS FETCH NEXT @fetch ROWS ONLY
            """, parameters, cancellationToken: token))).ToList();

        var hasMore = rows.Count > filter.Limit;
        if (hasMore) rows.RemoveAt(rows.Count - 1);

        var programs = await HydrateAsync(connection, rows, token);
        var nextCursor = hasMore && rows.Count > 0 ? Cursor.Encode(rows[^1].ProgramID) : null;

        return new CursorPage<ShootingProgram>(programs, nextCursor, filter.Limit);
    }

    public async Task<ShootingProgram?> GetProgramAsync(int id, CancellationToken token)
    {
        await using var connection = Connect();

        var rows = (await connection.QueryAsync<ProgramRow>(new CommandDefinition(
            $"{ProgramProjection} SELECT e.* FROM enriched e WHERE e.ProgramID = @id",
            new { id }, cancellationToken: token))).ToList();

        return (await HydrateAsync(connection, rows, token)).FirstOrDefault();
    }

    /// <summary>
    /// Loads shots, target info and shooters for a page of programs in three set-based queries
    /// rather than per program, then scores each one.
    /// </summary>
    private async Task<List<ShootingProgram>> HydrateAsync(
        SqlConnection connection, List<ProgramRow> rows, CancellationToken token)
    {
        if (rows.Count == 0) return [];

        var programIds = rows.Select(row => row.ProgramID).ToList();

        var shots = (await connection.QueryAsync<ShotRow>(new CommandDefinition("""
            SELECT  ShotID, ProgramID, ShotNr, PrimaryResult, SecondaryResult, HitPosition,
                    ShotType, ShotTime, Mouche, X, Y, TotalType, ShotGroup
            FROM    dbo.Shots
            WHERE   ProgramID IN @programIds
            ORDER BY ShotID
            """, new { programIds }, cancellationToken: token)))
            .GroupBy(row => row.ProgramID!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());

        var targets = (await connection.QueryAsync<TargetInfoRow>(new CommandDefinition("""
            SELECT  TargeinformationID, ProgramID, ShotGroup, TargetValuation, TargetType
            FROM    dbo.Targetinformation
            WHERE   ProgramID IN @programIds
            """, new { programIds }, cancellationToken: token)))
            .GroupBy(row => row.ProgramID)
            .ToDictionary(group => group.Key, group => group.ToList());

        var shooterIds = rows.Where(row => row.ShooterID.HasValue)
                             .Select(row => row.ShooterID!.Value)
                             .Distinct().ToList();

        var shooters = shooterIds.Count == 0
            ? new Dictionary<int, Shooter>()
            : await LoadShootersAsync(connection, shooterIds, token);

        return rows.Select(row => ToProgram(
                    row,
                    shots.GetValueOrDefault(row.ProgramID, []),
                    targets.GetValueOrDefault(row.ProgramID, []),
                    row.ShooterID is int id ? shooters.GetValueOrDefault(id) : null))
                   .ToList();
    }

    private ShootingProgram ToProgram(
        ProgramRow row, List<ShotRow> shots, List<TargetInfoRow> targets, Shooter? shooter)
    {
        // A row whose StartTime does not parse still has to surface; fall back to the epoch so
        // the program stays visible and the bad data is obvious rather than silently dropped.
        var start = row.StartedAt ?? SintroTime.ParseStartTime(row.StartTime) ?? DateTime.UnixEpoch;

        var score = shots.Count == 0
            ? ScoreCalculator.Empty
            : ScoreCalculator.Calculate(start, clock, shots, targets);

        var endTime = row.EndShotTime ?? ScoreCalculator.FindEndShotTime(shots);
        DateTimeOffset? finishedAt = null;
        if (endTime is not null && SintroTime.CombineShotTime(start, endTime) is DateTime end)
            finishedAt = clock.ToOffset(end);

        return new ShootingProgram(
            Id: row.ProgramID,
            Number: row.Number,
            Name: row.Name,
            Lane: row.LaneNr,
            StartedAt: clock.ToOffset(start),
            FinishedAt: finishedAt,
            State: row.IsActive == 1 ? ProgramState.Active
                 : row.HasEndMarker == 1 ? ProgramState.Finished
                 : ProgramState.Abandoned,
            Shooter: shooter,
            ContestShooterName: string.IsNullOrWhiteSpace(row.ContestShooterName)
                ? null
                : row.ContestShooterName.Trim(),
            Total: score.Total,
            TotalUnavailable: score.TotalUnavailable,
            ShotCount: score.ShotCount,
            ShotValues: score.ShotValues,
            ShotValuesText: string.Join(' ', score.ShotValues),
            Series: score.Series,
            Sighting: score.Sighting);
    }

    // -- Lanes --------------------------------------------------------------------

    public async Task<IReadOnlyList<LaneStatus>> ListLanesAsync(CancellationToken token)
    {
        await using var connection = Connect();

        var lanes = (await connection.QueryAsync<LaneRow>(new CommandDefinition(
            "SELECT LaneID, Number, ProgramID FROM dbo.Lanes ORDER BY Number",
            cancellationToken: token))).ToList();

        var programIds = lanes.Where(lane => lane.ProgramID.HasValue)
                              .Select(lane => lane.ProgramID!.Value)
                              .Distinct().ToList();

        var programs = new Dictionary<int, ShootingProgram>();
        if (programIds.Count > 0)
        {
            var rows = (await connection.QueryAsync<ProgramRow>(new CommandDefinition(
                $"{ProgramProjection} SELECT e.* FROM enriched e WHERE e.ProgramID IN @programIds",
                new { programIds }, cancellationToken: token))).ToList();

            programs = (await HydrateAsync(connection, rows, token)).ToDictionary(program => program.Id);
        }

        return lanes.Select(lane => new LaneStatus(
            lane.Number,
            lane.ProgramID is int id ? programs.GetValueOrDefault(id) : null)).ToList();
    }

    /// <summary>Cheap change-detection payload for the live feed — deliberately not the full lane view.</summary>
    public async Task<string> ReadLiveFingerprintAsync(CancellationToken token)
    {
        await using var connection = Connect();

        var lanes = await connection.QueryAsync<LaneRow>(new CommandDefinition(
            "SELECT LaneID, Number, ProgramID FROM dbo.Lanes ORDER BY Number", cancellationToken: token));
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
        parameters.Add("query", string.IsNullOrWhiteSpace(query) ? null : query.Trim());
        parameters.Add("clubId", clubId);

        const string where = """
            WHERE   (@clubId IS NULL OR sh.ClubID = @clubId)
              AND   (@query  IS NULL
                     OR sh.LastName  LIKE '%' + @query + '%'
                     OR sh.FirstName LIKE '%' + @query + '%'
                     OR sh.StartNr   LIKE '%' + @query + '%')
            """;

        // Shooters read alphabetically, so the keyset is the full sort tuple, tie-broken by the
        // primary key. Names are not unique — two shooters really can share one.
        //
        // ISNULL on both sides of the comparison and in ORDER BY: the device declares these
        // columns NOT NULL, so this is unreachable today, but the cursor already coalesces a
        // missing part to "" and the SQL did not. A NULL would then compare as UNKNOWN, quietly
        // skipping rows instead of failing — the worst way to be wrong. It is a vendor schema we
        // do not control, and there is no index here for the wrapper to defeat.
        var cursorClause = string.Empty;
        if (Cursor.TryDecode(cursor, 3, out var parts) && int.TryParse(parts[2], out var cursorShooterId))
        {
            parameters.Add("cLast", parts[0] ?? "");
            parameters.Add("cFirst", parts[1] ?? "");
            parameters.Add("cId", cursorShooterId);
            cursorClause = """
                AND (ISNULL(sh.LastName, '') > @cLast
                     OR (ISNULL(sh.LastName, '') = @cLast AND ISNULL(sh.FirstName, '') > @cFirst)
                     OR (ISNULL(sh.LastName, '') = @cLast AND ISNULL(sh.FirstName, '') = @cFirst
                         AND sh.ShooterID > @cId))
                """;
        }

        parameters.Add("fetch", limit + 1);

        var rows = (await connection.QueryAsync<ShooterRow>(new CommandDefinition($"""
            SELECT  sh.ShooterID, sh.FirstName, sh.LastName, sh.RFID, sh.StartNr, sh.ClubID,
                    c.ClubNumber, c.ClubName
            FROM    dbo.Shooters sh
            LEFT JOIN dbo.Club c ON c.ClubID = sh.ClubID
            {where}
            {cursorClause}
            ORDER BY ISNULL(sh.LastName, ''), ISNULL(sh.FirstName, ''), sh.ShooterID
            OFFSET 0 ROWS FETCH NEXT @fetch ROWS ONLY
            """, parameters, cancellationToken: token))).ToList();

        var hasMore = rows.Count > limit;
        if (hasMore) rows.RemoveAt(rows.Count - 1);

        var duplicates = await LoadDuplicateLicensesAsync(connection, token);
        var nextCursor = hasMore && rows.Count > 0
            ? Cursor.Encode(rows[^1].LastName, rows[^1].FirstName, rows[^1].ShooterID)
            : null;

        return new CursorPage<Shooter>(
            rows.Select(row => ToShooter(row, duplicates)).ToList(), nextCursor, limit);
    }

    /// <summary>
    /// Returns every shooter on the licence. StartNr carries no unique constraint, so a
    /// collision is possible; all matches come back flagged rather than one being guessed.
    /// </summary>
    public async Task<IReadOnlyList<Shooter>> FindShootersByLicenseAsync(string license, CancellationToken token)
    {
        await using var connection = Connect();

        var ids = await ResolveLicenseAsync(connection, license, token);
        if (ids.Count == 0) return [];

        var rows = await connection.QueryAsync<ShooterRow>(new CommandDefinition("""
            SELECT  sh.ShooterID, sh.FirstName, sh.LastName, sh.RFID, sh.StartNr, sh.ClubID,
                    c.ClubNumber, c.ClubName
            FROM    dbo.Shooters sh
            LEFT JOIN dbo.Club c ON c.ClubID = sh.ClubID
            WHERE   sh.ShooterID IN @ids
            ORDER BY sh.ShooterID
            """, new { ids }, cancellationToken: token));

        var duplicates = await LoadDuplicateLicensesAsync(connection, token);
        return rows.Select(row => ToShooter(row, duplicates)).ToList();
    }

    public async Task<CursorPage<Club>> ListClubsAsync(
        string? query, int limit, string? cursor, CancellationToken token)
    {
        await using var connection = Connect();

        var parameters = new DynamicParameters();
        parameters.Add("query", string.IsNullOrWhiteSpace(query) ? null : query.Trim());

        const string where = """
            WHERE   (@query IS NULL
                     OR c.ClubName   LIKE '%' + @query + '%'
                     OR c.ClubNumber LIKE '%' + @query + '%')
            """;

        var cursorClause = string.Empty;
        if (Cursor.TryDecode(cursor, 2, out var parts) && int.TryParse(parts[1], out var cursorClubId))
        {
            parameters.Add("cName", parts[0] ?? "");
            parameters.Add("cId", cursorClubId);
            cursorClause =
                "AND (ISNULL(c.ClubName, '') > @cName " +
                "     OR (ISNULL(c.ClubName, '') = @cName AND c.ClubID > @cId))";
        }

        parameters.Add("fetch", limit + 1);

        var rows = (await connection.QueryAsync<ClubRow>(new CommandDefinition($"""
            SELECT c.ClubID, c.ClubNumber, c.ClubName
            FROM   dbo.Club c
            {where}
            {cursorClause}
            ORDER BY ISNULL(c.ClubName, ''), c.ClubID
            OFFSET 0 ROWS FETCH NEXT @fetch ROWS ONLY
            """, parameters, cancellationToken: token))).ToList();

        var hasMore = rows.Count > limit;
        if (hasMore) rows.RemoveAt(rows.Count - 1);

        var nextCursor = hasMore && rows.Count > 0
            ? Cursor.Encode(rows[^1].ClubName, rows[^1].ClubID)
            : null;

        return new CursorPage<Club>(
            rows.Select(ToClub).ToList(),
            nextCursor, limit);
    }

    /// <summary>
    /// (Number, Name) pairs actually present. Not a lookup table: the operator renames programs,
    /// so number 31 appears as both "A10-Probe" and "Obligatorisches Programm".
    /// </summary>
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
        // InvalidOperationException covers pool exhaustion and connect timeouts, which
        // SqlClient throws instead of SqlException; health must answer 503, not crash.
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            return false;
        }
    }

    // -- Helpers ------------------------------------------------------------------

    /// <summary>
    /// Resolves a licence to shooter ids in C# rather than SQL: normalisation (digit-stripping
    /// and zero-padding) has to match OpenRangeOffice exactly, and the table is small.
    /// </summary>
    private static async Task<List<int>> ResolveLicenseAsync(
        SqlConnection connection, string? license, CancellationToken token)
    {
        var normalized = LicenseNumber.Normalize(license);
        if (normalized.Length == 0) return [];

        var rows = await connection.QueryAsync<(int ShooterID, string? StartNr)>(new CommandDefinition(
            "SELECT ShooterID, StartNr FROM dbo.Shooters", cancellationToken: token));

        return rows.Where(row => LicenseNumber.Normalize(row.StartNr) == normalized)
                   .Select(row => row.ShooterID)
                   .ToList();
    }

    private static async Task<HashSet<string>> LoadDuplicateLicensesAsync(
        SqlConnection connection, CancellationToken token)
    {
        var rows = await connection.QueryAsync<string?>(new CommandDefinition(
            "SELECT StartNr FROM dbo.Shooters", cancellationToken: token));

        return rows.Select(LicenseNumber.Normalize)
                   .Where(license => license.Length > 0)
                   .GroupBy(license => license)
                   .Where(group => group.Count() > 1)
                   .Select(group => group.Key)
                   .ToHashSet();
    }

    private static async Task<Dictionary<int, Shooter>> LoadShootersAsync(
        SqlConnection connection, List<int> shooterIds, CancellationToken token)
    {
        var rows = await connection.QueryAsync<ShooterRow>(new CommandDefinition("""
            SELECT  sh.ShooterID, sh.FirstName, sh.LastName, sh.RFID, sh.StartNr, sh.ClubID,
                    c.ClubNumber, c.ClubName
            FROM    dbo.Shooters sh
            LEFT JOIN dbo.Club c ON c.ClubID = sh.ClubID
            WHERE   sh.ShooterID IN @shooterIds
            """, new { shooterIds }, cancellationToken: token));

        var duplicates = await LoadDuplicateLicensesAsync(connection, token);
        return rows.ToDictionary(row => row.ShooterID, row => ToShooter(row, duplicates));
    }

    private static Shooter ToShooter(ShooterRow row, HashSet<string> duplicateLicenses)
    {
        var license = LicenseNumber.Normalize(row.StartNr);

        return new Shooter(
            License: license,
            FirstName: row.FirstName?.Trim() ?? "",
            LastName: row.LastName?.Trim() ?? "",
            ShooterId: row.ShooterID,
            Rfid: RfidCard.Clean(row.RFID),
            Club: row.ClubID is int clubId
                ? ToClub(clubId, row.ClubNumber, row.ClubName)
                : null,
            DuplicateLicense: duplicateLicenses.Contains(license));
    }

    // The shipped club register carries trailing CR characters in its names.
    private static Club ToClub(ClubRow row) => ToClub(row.ClubID, row.ClubNumber, row.ClubName);

    private static Club ToClub(int id, string? number, string? name) =>
        new(id, number?.Trim() ?? "", name?.Trim() ?? "");
}
