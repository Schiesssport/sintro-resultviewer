using Microsoft.Data.SqlClient;
using Sintro.ResultViewer.Data.Sintro300;

namespace Sintro.ResultViewer.Tests;

public class ProgramMappingTests
{
    [Fact]
    public async Task theDeviceTextDateIsParsedDayFirst()
    {
        // Mirrors StartedAtSql; the date is invented.
        await using var connection = new SqlConnection(ApiFixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT TRY_CONVERT(datetime2, REPLACE('08.07.2026-20:45:54', '-', ' '), 104)", connection);

        Assert.Equal(new DateTime(2026, 7, 8, 20, 45, 54), await command.ExecuteScalarAsync());
    }

    [Fact]
    public void anUnparseableStartTimeIsReportedAsTheEpochSoBadDataIsVisible()
    {
        var row = new ProgramRow(
            ProgramID: 1, Number: 0, Name: "x", LaneNr: 1, ContestShooterName: null, ShooterID: null,
            StartedAt: null, EndShotId: null, CountingShots: 0, IsActive: 0);
        Assert.Equal(DateTime.UnixEpoch, SintroRepository.StartOf(row));
    }
}
