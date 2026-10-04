using Sintro.ResultViewer.Data.Sintro300;

namespace Sintro.ResultViewer.Tests;

public class ProgramMappingTests
{
    [Fact]
    public void anUnparseableStartTimeIsReportedAsTheEpochSoBadDataIsVisible()
    {
        var row = new ProgramRow(
            ProgramID: 1, Number: 0, Name: "x", LaneNr: 1, ContestShooterName: null, ShooterID: null,
            StartedAt: null, EndShotId: null, CountingShots: 0, IsActive: 0);
        Assert.Equal(DateTime.UnixEpoch, SintroRepository.StartOf(row));
    }
}
