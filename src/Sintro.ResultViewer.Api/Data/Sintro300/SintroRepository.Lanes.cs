using Dapper;
using Microsoft.Data.SqlClient;
using Sintro.ResultViewer.Domain;

namespace Sintro.ResultViewer.Data.Sintro300;

public sealed partial class SintroRepository
{
    private const string LaneQuery = "SELECT Number, ProgramID FROM dbo.Lanes ORDER BY Number";

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
}
