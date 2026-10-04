using Dapper;
using Microsoft.Data.SqlClient;
using Sintro.ResultViewer.Domain;

namespace Sintro.ResultViewer.Data.Sintro300;

public sealed partial class SintroRepository
{
    /// <summary>(Number, Name) pairs present. Not a lookup table: operators rename programs, so one number can appear under several names.</summary>
    public async Task<IReadOnlyList<ProgramCatalogEntry>> ListProgramCatalogAsync(CancellationToken token)
    {
        await using var connection = Connect();

        var rows = await connection.QueryAsync<CatalogRow>(new CommandDefinition($"""
            SELECT   pr.Number,
                     pr.Name,
                     COUNT(*) AS TimesShot,
                     MAX({string.Format(StartedAtSql, "pr.StartTime")}) AS LastStartedAt
            FROM     dbo.Programs pr
            GROUP BY pr.Number, pr.Name
            ORDER BY pr.Number, pr.Name
            """, cancellationToken: token));

        return rows.Select(row => new ProgramCatalogEntry(
            row.Number,
            row.Name,
            row.TimesShot,
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
}
