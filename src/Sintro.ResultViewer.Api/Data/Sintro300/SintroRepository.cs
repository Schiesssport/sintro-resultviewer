using Dapper;
using Microsoft.Data.SqlClient;
using Sintro.ResultViewer.Domain;

namespace Sintro.ResultViewer.Data.Sintro300;

/// <summary>Every SQL statement against the device database lives here; read-only throughout.</summary>
public sealed partial class SintroRepository(string connectionString, ISintroClock clock) : ISintroRepository
{
    private const string StartedAtSql = "TRY_CONVERT(datetime2, REPLACE({0}, '-', ' '), 104)";

    private SqlConnection Connect() => new(connectionString);

    // Epoch when SQL could not parse StartTime, so bad data is visibly wrong rather than missing.
    public static DateTime StartOf(ProgramRow row) => row.StartedAt ?? DateTime.UnixEpoch;

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

    // The shipped club register carries trailing CR characters in its names.
    private static Club ToClub(int id, string? number, string? name) =>
        new(id, number?.Trim() ?? "", name?.Trim() ?? "");
}
