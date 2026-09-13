using Sintro.ResultViewer.Data;
using Sintro.ResultViewer.Domain;

namespace Sintro.ResultViewer.Tests;

public class ScoreCalculatorTests
{
    private static readonly DateTime ProgramStart = new(2026, 7, 8, 20, 45, 54);
    private static readonly TestClock Clock = new();

    private static ShotRow Shot(
        int shotId, int shotNr, int primary, int shotGroup,
        int shotType = 1, int totalType = 0, int mouche = 0, int hitPosition = 3,
        string? shotTime = "20:46:12.55", int secondary = 0) =>
        new(shotId, 1, shotNr, primary, secondary, hitPosition, shotType, shotTime,
            mouche, 39.0, 129.0, totalType, shotGroup);

    private static TargetInfoRow Target(int shotGroup, int valuation, int id = 1, int targetType = 0) =>
        new(id, 1, shotGroup, valuation, targetType);

    private static ScoreCalculator.ProgramScore Calculate(
        IEnumerable<ShotRow> shots, IEnumerable<TargetInfoRow> targets) =>
        ScoreCalculator.Calculate(ProgramStart, Clock, shots, targets);

    [Fact]
    public void uniformValuation_totalsAcrossSeries()
    {
        var shots = new[]
        {
            Shot(1, 1, 8, 1), Shot(2, 2, 9, 1, totalType: 1),
            Shot(3, 3, 10, 2), Shot(4, 4, 7, 2, totalType: 7),
        };
        var targets = new[] { Target(1, 10), Target(2, 10) };

        var score = Calculate(shots, targets);

        Assert.Equal(2, score.Series.Count);
        Assert.Equal(34, score.Total!.Value);
        Assert.Equal(4, score.Total.ShotCount);
        Assert.Equal(10, score.Total.Valuation);
        Assert.Null(score.TotalUnavailable);
        Assert.Equal([8, 9, 10, 7], score.ShotValues);
    }

    [Fact]
    public void mixedValuation_yieldsNoTotalButKeepsSubtotals()
    {
        // The device can switch target and valuation mid-pass, and demo programs ship that
        // do exactly this: adding a 5er series to a 10er one would be meaningless.
        var shots = new[] { Shot(1, 1, 5, 1), Shot(2, 2, 9, 2) };
        var targets = new[] { Target(1, 5), Target(2, 10) };

        var score = Calculate(shots, targets);

        Assert.Null(score.Total);
        Assert.Equal(TotalUnavailableReason.MixedValuation, score.TotalUnavailable);
        Assert.Equal(5, score.Series[0].Subtotal);
        Assert.Equal(9, score.Series[1].Subtotal);
    }

    [Fact]
    public void missingTargetInfo_reportsUnknownValuation()
    {
        var score = Calculate([Shot(1, 1, 8, 1)], []);

        Assert.Null(score.Total);
        Assert.Equal(TotalUnavailableReason.UnknownValuation, score.TotalUnavailable);
        Assert.Null(score.Series[0].Valuation);
    }

    [Fact]
    public void duplicateTargetInfo_takesTheHighestId()
    {
        // (program, series) pairs can carry duplicate rows; the latest write is current.
        var score = Calculate(
            [Shot(1, 1, 9, 1)],
            [Target(1, 5, id: 10), Target(1, 10, id: 42)]);

        Assert.Equal(10, score.Series[0].Valuation);
    }

    [Fact]
    public void markerRows_areNotShots()
    {
        // ShotNr 9999 / TotalType 7 is a synthetic end-of-program record.
        var score = Calculate(
            [Shot(1, 1, 9, 1), Shot(2, 9999, 0, 0, totalType: 7, hitPosition: 255, shotTime: null)],
            [Target(1, 10)]);

        Assert.Equal(1, score.ShotCount);
        Assert.Equal([9], score.ShotValues);
        Assert.Equal(9, score.Total!.Value);
    }

    [Fact]
    public void programWithOnlyAMarker_hasNoResult()
    {
        // A pass can consist of nothing but the marker row: started, then abandoned.
        var score = Calculate(
            [Shot(1, 9999, 0, 0, totalType: 7, hitPosition: 255, shotTime: null)],
            []);

        Assert.Empty(score.Series);
        Assert.Equal(0, score.ShotCount);
        Assert.Null(score.Total);
        Assert.Null(score.TotalUnavailable);
    }

    [Fact]
    public void sightingShots_areSeparatedAndExcludedFromTheTotal()
    {
        var shots = new[]
        {
            Shot(1, 1, 2, 0, shotType: 0),
            Shot(2, 2, 3, 0, shotType: 0, totalType: 1),
            Shot(3, 1, 9, 1), Shot(4, 2, 10, 1),
        };
        var targets = new[] { Target(0, 5), Target(1, 10) };

        var score = Calculate(shots, targets);

        Assert.NotNull(score.Sighting);
        Assert.Equal(2, score.Sighting!.ShotCount);
        Assert.Equal(5, score.Sighting.Subtotal);

        // The sighting series must not drag its 5er valuation into the total.
        Assert.Equal(19, score.Total!.Value);
        Assert.Equal(10, score.Total.Valuation);
        Assert.Equal(2, score.ShotCount);
    }

    [Fact]
    public void countingShotsInGroupZero_areNotMistakenForSighting()
    {
        // Counting shots do occur in ShotGroup 0, so ShotType is what identifies a
        // sighting shot — grouping on ShotGroup 0 would silently discard them.
        var score = Calculate([Shot(1, 1, 9, 0, shotType: 1)], [Target(0, 10)]);

        Assert.Null(score.Sighting);
        Assert.Single(score.Series);
        Assert.Equal(9, score.Total!.Value);
    }

    [Fact]
    public void sightingShotsOutsideGroupZero_areStillSighting()
    {
        // The converse case: sighting shots also occur in higher groups.
        var score = Calculate(
            [Shot(1, 1, 3, 4, shotType: 0), Shot(2, 1, 9, 5, shotType: 1)],
            [Target(4, 5), Target(5, 10)]);

        Assert.NotNull(score.Sighting);
        Assert.Equal(4, score.Sighting!.Index);
        Assert.Single(score.Series);
        Assert.Equal(10, score.Total!.Valuation);
    }

    [Fact]
    public void miss_countsAsAShotWorthZero()
    {
        var score = Calculate([Shot(1, 1, 0, 1), Shot(2, 2, 9, 1)], [Target(1, 10)]);

        Assert.Equal(2, score.ShotCount);
        Assert.Equal(9, score.Total!.Value);
        Assert.Equal([0, 9], score.ShotValues);
    }

    [Fact]
    public void moucheAndHitSector_areMapped()
    {
        var shots = new[]
        {
            Shot(1, 1, 10, 1, mouche: 1, hitPosition: 0),
            Shot(2, 2, 8, 1, hitPosition: 255),
            Shot(3, 3, 7, 1, hitPosition: 6),
        };

        var score = Calculate(shots, [Target(1, 10)]);
        var mapped = score.Series[0].Shots;

        Assert.True(mapped[0].Mouche);
        Assert.Equal(0, mapped[0].HitSector);   // 0 is a centre hit, not "unknown"
        Assert.Null(mapped[1].HitSector);       // 255 means the device reported none
        Assert.Equal(6, mapped[2].HitSector);
    }

    [Fact]
    public void shotTimestamps_combineTheProgramDateWithTheStoredTimeOfDay()
    {
        var score = Calculate([Shot(1, 1, 9, 1, shotTime: "20:46:12.55")], [Target(1, 10)]);

        var at = score.Series[0].Shots[0].At;
        Assert.NotNull(at);
        Assert.Equal(new DateTime(2026, 7, 8, 20, 46, 12, 550), at!.Value.DateTime);
        Assert.Equal(TimeSpan.FromHours(2), at.Value.Offset);
    }

    [Fact]
    public void findEndShotTime_readsTheEndMarkerBeforeMarkersAreDropped()
    {
        var shots = new[]
        {
            Shot(1, 1, 9, 1, shotTime: "20:46:00.00"),
            Shot(2, 9999, 0, 0, totalType: 7, shotTime: "20:47:30.00"),
        };

        Assert.Equal("20:47:30.00", ScoreCalculator.FindEndShotTime(shots));
    }

    [Theory]
    [InlineData(0, 10, "A10")]   // A target, 10er scale
    [InlineData(0, 5, "A5")]
    [InlineData(0, 100, "A100")]
    [InlineData(1, 4, "B4")]     // B target, 4er scale
    [InlineData(1, 100, "B100")]
    public void targetCodeCombinesTheTargetLetterAndTheRingScale(int targetType, int valuation, string expected)
    {
        // These are exactly the codes operators use in program names ("A10-EF6-SF4").
        var score = Calculate([Shot(1, 1, 9, 1)], [Target(1, valuation, targetType: targetType)]);
        Assert.Equal(expected, score.Series[0].TargetCode);
    }

    [Fact]
    public void theSauSilhouetteIsItsOwnTarget()
    {
        // TargetType 3 is the Sau silhouette.
        var score = Calculate([Shot(1, 1, 9, 1)], [Target(1, 10, targetType: 3)]);
        Assert.Equal("S10", score.Series[0].TargetCode);
    }

    [Fact]
    public void anUnknownTargetTypeIsMarkedRatherThanGuessed()
    {
        var score = Calculate([Shot(1, 1, 9, 1)], [Target(1, 10, targetType: 42)]);
        Assert.Equal("?10", score.Series[0].TargetCode);
    }

    [Fact]
    public void aSeriesWithoutTargetInfoStillGetsAReadableCode()
    {
        var score = Calculate([Shot(1, 1, 9, 1)], []);
        Assert.Equal("??", score.Series[0].TargetCode);
    }

    [Fact]
    public void bestFineValueIsTheHighestTenthInTheSeries()
    {
        var shots = new[]
        {
            Shot(1, 1, 9, 1, secondary: 88),
            Shot(2, 2, 10, 1, secondary: 97),
            Shot(3, 3, 8, 1, secondary: 79),
        };

        var score = Calculate(shots, [Target(1, 10)]);
        Assert.Equal(97, score.Series[0].BestFineValue);
    }

    [Fact]
    public void missesDoNotWinBestFineValue()
    {
        // A miss reports a fine value of 0, which must not be treated as a score.
        var score = Calculate(
            [Shot(1, 1, 0, 1, secondary: 0), Shot(2, 2, 7, 1, secondary: 61)],
            [Target(1, 10)]);

        Assert.Equal(61, score.Series[0].BestFineValue);
    }

    [Fact]
    public void aSeriesOfNothingButMissesHasNoBestFineValue()
    {
        var score = Calculate([Shot(1, 1, 0, 1, secondary: 0)], [Target(1, 10)]);
        Assert.Null(score.Series[0].BestFineValue);
    }

    [Fact]
    public void seriesAreOrderedByGroupAndShotsByShotId()
    {
        // Rows arrive in arbitrary order; the device's ShotID is the firing order.
        var shots = new[]
        {
            Shot(4, 2, 7, 2), Shot(1, 1, 8, 1), Shot(3, 1, 6, 2), Shot(2, 2, 9, 1),
        };

        var score = Calculate(shots, [Target(1, 10), Target(2, 10)]);

        Assert.Equal([1, 2], score.Series.Select(series => series.Index));
        Assert.Equal([8, 9, 6, 7], score.ShotValues);
    }
}
