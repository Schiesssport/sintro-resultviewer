using Dapper;
using Microsoft.Data.SqlClient;
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
    /// Marker rows are ShotNr 9999 only, as in ScoreCalculator.IsMarker: the last real shot of a
    /// pass carries TotalType 7 as well, so that flag cannot exclude rows.
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
                    ISNULL(f.HasEndMarker, 0)   AS HasEndMarker,
                    ISNULL(f.CountingShots, 0)  AS CountingShots,
                    CASE WHEN o.ProgramID IS NOT NULL AND ISNULL(f.HasEndMarker, 0) = 0
                         THEN 1 ELSE 0 END      AS IsActive
            FROM    prog p
            LEFT JOIN flags  f ON f.ProgramID = p.ProgramID
            LEFT JOIN onlane o ON o.ProgramID = p.ProgramID
        )
        """;

    /// <summary>
    /// A program whose StartTime does not parse has StartedAt NULL, compares UNKNOWN against any
    /// date window and therefore appears in no list. It is still reachable by id, where its
    /// start is reported as the epoch so the bad data is obvious rather than hidden.
    /// </summary>
    private const string ProgramWhere = """
        WHERE   (@from        IS NULL OR CONVERT(date, e.StartedAt) >= @from)
          AND   (@to          IS NULL OR CONVERT(date, e.StartedAt) <= @to)
          AND   (@number      IS NULL OR e.Number = @number)
          AND   (@lane        IS NULL OR e.LaneNr = @lane)
          AND   (@name        IS NULL OR e.Name LIKE @name ESCAPE '\')
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

        LicenseIndex? licenses = null;
        var shooterIds = new List<int>();
        if (filter.License is { Length: > 0 })
        {
            licenses = await LicenseIndex.LoadAsync(connection, token);
            shooterIds = licenses.Resolve(filter.License);

            // A licence filter that matches nobody must return nothing, not everything.
            if (shooterIds.Count == 0) return new CursorPage<ShootingProgram>([], null, filter.Limit);
        }

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

        // ProgramID is the keyset key: it is an identity column and, verified across the whole
        // dataset, perfectly monotonic with StartTime — so ordering by it equals ordering by
        // time while staying stable as rows are inserted and pruned during paging.
        var direction = filter.Ascending ? "ASC" : "DESC";
        var comparison = filter.Ascending ? ">" : "<";

        var cursorClause = string.Empty;
        if (Cursor.Decode(filter.Cursor, 2) is { } parts)
        {
            // The cursor remembers the order it was issued under. Walking asc, then continuing
            // with the default desc, would silently return everything older instead of what is
            // new — the opposite of the sync the client built.
            if (parts[1] != direction)
                throw new InvalidCursorException(
                    $"This cursor was issued for order={parts[1]?.ToLowerInvariant()}; pass the same order to continue from it.");

            parameters.Add("cursorId", Cursor.DecodeInt(parts[0]));
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

        var programs = await HydrateAsync(connection, rows, licenses, token);
        var nextCursor = hasMore && rows.Count > 0 ? Cursor.Encode(rows[^1].ProgramID, direction) : null;

        return new CursorPage<ShootingProgram>(programs, nextCursor, filter.Limit);
    }

    public async Task<ShootingProgram?> GetProgramAsync(int id, CancellationToken token)
    {
        await using var connection = Connect();

        var rows = (await connection.QueryAsync<ProgramRow>(new CommandDefinition(
            $"{ProgramProjection} SELECT e.* FROM enriched e WHERE e.ProgramID = @id",
            new { id }, cancellationToken: token))).ToList();

        return (await HydrateAsync(connection, rows, null, token)).FirstOrDefault();
    }

    /// <summary>
    /// Loads shots, target info and shooters for a page of programs in three set-based queries
    /// rather than per program, then scores each one. <paramref name="licenses"/> is reused when
    /// the caller already loaded it, otherwise read only if any program names a shooter.
    /// </summary>
    private async Task<List<ShootingProgram>> HydrateAsync(
        SqlConnection connection, List<ProgramRow> rows, LicenseIndex? licenses, CancellationToken token)
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

        var shooters = new Dictionary<int, Shooter>();
        if (shooterIds.Count > 0)
        {
            licenses ??= await LicenseIndex.LoadAsync(connection, token);
            shooters = await LoadShootersAsync(connection, shooterIds, licenses, token);
        }

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
        // SQL already tried style 104; the C# parse is the same rule and only ever matters if
        // the two implementations disagree. Epoch after that, so a row with unreadable data is
        // visibly wrong rather than missing (see ProgramWhere for how lists treat it).
        var start = row.StartedAt ?? SintroTime.ParseStartTime(row.StartTime) ?? DateTime.UnixEpoch;

        var score = ScoreCalculator.Calculate(start, clock, shots, targets);

        // The end time comes from the marker with the highest ShotID, not from a SQL MAX over the
        // time text: a pass that runs past midnight has "23:59:.." sort after "00:03:..".
        DateTimeOffset? finishedAt = null;
        if (ScoreCalculator.FindEndShotTime(shots) is { } endTime &&
            SintroTime.CombineShotTime(start, endTime) is DateTime end)
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

            programs = (await HydrateAsync(connection, rows, null, token)).ToDictionary(program => program.Id);
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
        parameters.Add("query", ContainsPattern(query));
        parameters.Add("clubId", clubId);

        const string where = """
            WHERE   (@clubId IS NULL OR sh.ClubID = @clubId)
              AND   (@query  IS NULL
                     OR sh.LastName  LIKE @query ESCAPE '\'
                     OR sh.FirstName LIKE @query ESCAPE '\'
                     OR sh.StartNr   LIKE @query ESCAPE '\')
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
        if (Cursor.Decode(cursor, 3) is { } parts)
        {
            parameters.Add("cLast", parts[0] ?? "");
            parameters.Add("cFirst", parts[1] ?? "");
            parameters.Add("cId", Cursor.DecodeInt(parts[2]));
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

        var licenses = await LicenseIndex.LoadAsync(connection, token);
        var nextCursor = hasMore && rows.Count > 0
            ? Cursor.Encode(rows[^1].LastName, rows[^1].FirstName, rows[^1].ShooterID)
            : null;

        return new CursorPage<Shooter>(
            rows.Select(row => ToShooter(row, licenses)).ToList(), nextCursor, limit);
    }

    /// <summary>
    /// Returns every shooter on the licence. StartNr carries no unique constraint, so a
    /// collision is possible; all matches come back flagged rather than one being guessed.
    /// </summary>
    public async Task<IReadOnlyList<Shooter>> FindShootersByLicenseAsync(string license, CancellationToken token)
    {
        await using var connection = Connect();

        var licenses = await LicenseIndex.LoadAsync(connection, token);
        var ids = licenses.Resolve(license);
        if (ids.Count == 0) return [];

        var rows = await connection.QueryAsync<ShooterRow>(new CommandDefinition("""
            SELECT  sh.ShooterID, sh.FirstName, sh.LastName, sh.RFID, sh.StartNr, sh.ClubID,
                    c.ClubNumber, c.ClubName
            FROM    dbo.Shooters sh
            LEFT JOIN dbo.Club c ON c.ClubID = sh.ClubID
            WHERE   sh.ShooterID IN @ids
            ORDER BY sh.ShooterID
            """, new { ids }, cancellationToken: token));

        return rows.Select(row => ToShooter(row, licenses)).ToList();
    }

    public async Task<CursorPage<Club>> ListClubsAsync(
        string? query, int limit, string? cursor, CancellationToken token)
    {
        await using var connection = Connect();

        var parameters = new DynamicParameters();
        parameters.Add("query", ContainsPattern(query));

        const string where = """
            WHERE   (@query IS NULL
                     OR c.ClubName   LIKE @query ESCAPE '\'
                     OR c.ClubNumber LIKE @query ESCAPE '\')
            """;

        var cursorClause = string.Empty;
        if (Cursor.Decode(cursor, 2) is { } parts)
        {
            parameters.Add("cName", parts[0] ?? "");
            parameters.Add("cId", Cursor.DecodeInt(parts[1]));
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
    /// so one number can appear under several names.
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
    /// A LIKE pattern matching rows that contain the text literally. LIKE treats %, _ and [ as
    /// wildcards, so "A10_" would match "A10-…" and a stray "[" would be a SQL error; each is
    /// escaped so the caller's text means exactly what it says.
    /// </summary>
    internal static string? ContainsPattern(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var escaped = text.Trim()
            .Replace(@"\", @"\\")
            .Replace("%", @"\%")
            .Replace("_", @"\_")
            .Replace("[", @"\[");

        return $"%{escaped}%";
    }

    /// <summary>
    /// Every licence in dbo.Shooters, normalised the way OpenRangeOffice does (digits only,
    /// zero-padded), read once per request. The table is small, and doing the matching in C#
    /// is what keeps the normalisation rule in exactly one place.
    /// </summary>
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

    private static async Task<Dictionary<int, Shooter>> LoadShootersAsync(
        SqlConnection connection, List<int> shooterIds, LicenseIndex licenses, CancellationToken token)
    {
        var rows = await connection.QueryAsync<ShooterRow>(new CommandDefinition("""
            SELECT  sh.ShooterID, sh.FirstName, sh.LastName, sh.RFID, sh.StartNr, sh.ClubID,
                    c.ClubNumber, c.ClubName
            FROM    dbo.Shooters sh
            LEFT JOIN dbo.Club c ON c.ClubID = sh.ClubID
            WHERE   sh.ShooterID IN @shooterIds
            """, new { shooterIds }, cancellationToken: token));

        return rows.ToDictionary(row => row.ShooterID, row => ToShooter(row, licenses));
    }

    private static Shooter ToShooter(ShooterRow row, LicenseIndex licenses)
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
            DuplicateLicense: licenses.IsDuplicate(license));
    }

    // The shipped club register carries trailing CR characters in its names.
    private static Club ToClub(ClubRow row) => ToClub(row.ClubID, row.ClubNumber, row.ClubName);

    private static Club ToClub(int id, string? number, string? name) =>
        new(id, number?.Trim() ?? "", name?.Trim() ?? "");
}
