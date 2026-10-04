using Dapper;
using Microsoft.Data.SqlClient;
using Sintro.ResultViewer.Domain;

namespace Sintro.ResultViewer.Data.Sintro300;

public sealed partial class SintroRepository
{
    private const string ShooterProjection = """
        SELECT  sh.ShooterID, sh.FirstName, sh.LastName, sh.StartNr, sh.ClubID,
                c.ClubNumber, c.ClubName
        FROM    dbo.Shooters sh
        LEFT JOIN dbo.Club c ON c.ClubID = sh.ClubID
        """;

    // With a window, only shooters who have a pass starting inside it; the StartTime conversion mirrors ProgramProjection.
    public async Task<CursorPage<Shooter>> ListShootersAsync(ShooterFilter filter, CancellationToken token)
    {
        await using var connection = Connect();

        var parameters = new DynamicParameters();
        parameters.Add("query", ContainsPattern(filter.Query));
        parameters.Add("clubId", filter.ClubId);
        parameters.Add("windowed", filter.From is not null || filter.To is not null ? 1 : 0);
        parameters.Add("from", filter.From?.ToDateTime(TimeOnly.MinValue).Date);
        parameters.Add("to", filter.To?.ToDateTime(TimeOnly.MinValue).Date);
        var cursorClause = ShooterCursorClause(filter.Cursor, parameters);

        var (rows, nextCursor, hasMore) = await QueryPageAsync<ShooterRow>(connection, $"""
            {ShooterProjection}
            WHERE   (@clubId IS NULL OR sh.ClubID = @clubId)
              AND   (@windowed = 0 OR EXISTS (
                        SELECT 1 FROM dbo.Programs p
                        WHERE p.ShooterID = sh.ShooterID
                          AND (@from IS NULL OR CONVERT(date, {string.Format(StartedAtSql, "p.StartTime")}) >= @from)
                          AND (@to   IS NULL OR CONVERT(date, {string.Format(StartedAtSql, "p.StartTime")}) <= @to)))
              AND   (@query  IS NULL
                     OR sh.LastName  LIKE @query ESCAPE '\'
                     OR sh.FirstName LIKE @query ESCAPE '\'
                     OR sh.StartNr   LIKE @query ESCAPE '\')
            {cursorClause}
            ORDER BY ISNULL(sh.LastName, ''), ISNULL(sh.FirstName, ''), sh.ShooterID
            """, parameters, filter.Limit, row => Cursor.Encode(row.LastName, row.FirstName, row.ShooterID), token);

        var licenses = await LoadLicenseIndexAsync(connection, token);
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

        var licenses = await LoadLicenseIndexAsync(connection, token);
        var shooterIds = licenses.Resolve(license);
        return shooterIds.Count == 0 ? [] : await LoadShootersByIdAsync(connection, shooterIds, licenses, token);
    }

    private static async Task<LicenseIndex> LoadLicenseIndexAsync(SqlConnection connection, CancellationToken token) =>
        LicenseIndex.From(await connection.QueryAsync<(int ShooterID, string? StartNr)>(new CommandDefinition(
            "SELECT ShooterID, StartNr FROM dbo.Shooters", cancellationToken: token)));

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
}
