using Microsoft.Extensions.Options;
using Sintro.ResultViewer.Data;

namespace Sintro.ResultViewer.Tests;

public class SintroTimeTests
{
    [Fact]
    public void parseStartTime_readsTheDeviceFormat()
    {
        var parsed = SintroTime.ParseStartTime("08.07.2026-20:45:54");
        Assert.Equal(new DateTime(2026, 7, 8, 20, 45, 54), parsed);
    }

    [Theory]
    [InlineData("2026-07-08T20:45:54")]  // ISO is not what the device writes
    [InlineData("07/08/2026-20:45:54")]  // month-first must not be accepted
    [InlineData("")]
    [InlineData(null)]
    public void parseStartTime_rejectsAnythingElse(string? input) =>
        Assert.Null(SintroTime.ParseStartTime(input));

    [Fact]
    public void parseStartTime_isDayFirstNotMonthFirst()
    {
        // 08.07 is 8 July. Reading it as 7 August would silently shift results by a month.
        var parsed = SintroTime.ParseStartTime("08.07.2026-20:45:54");
        Assert.Equal(7, parsed!.Value.Month);
        Assert.Equal(8, parsed.Value.Day);
    }

    [Fact]
    public void combineShotTime_takesTheDateFromTheProgram()
    {
        var start = new DateTime(2026, 7, 8, 20, 45, 54);
        Assert.Equal(new DateTime(2026, 7, 8, 20, 46, 12, 550),
            SintroTime.CombineShotTime(start, "20:46:12.55"));
    }

    [Fact]
    public void combineShotTime_rollsPastMidnight()
    {
        // ShotTime stores no date, so a shot at 00:05 during a 23:50 program is next day.
        var start = new DateTime(2026, 7, 8, 23, 50, 0);
        Assert.Equal(new DateTime(2026, 7, 9, 0, 5, 0),
            SintroTime.CombineShotTime(start, "00:05:00.00"));
    }

    [Fact]
    public void combineShotTime_toleratesSlightlyEarlierTimes()
    {
        // A shot logged a few seconds before the recorded start is clock jitter,
        // not a rollover to the following day.
        var start = new DateTime(2026, 7, 8, 20, 45, 54);
        Assert.Equal(new DateTime(2026, 7, 8, 20, 45, 50),
            SintroTime.CombineShotTime(start, "20:45:50.00"));
    }

    [Fact]
    public void combineShotTime_returnsNullForUnparseableInput() =>
        Assert.Null(SintroTime.CombineShotTime(DateTime.UnixEpoch, "not a time"));
}

public class SintroClockTests
{
    private static SintroClock Build(string? referenceDate, string timeZone = "Europe/Zurich") =>
        new(Options.Create(new SintroOptions { ReferenceDate = referenceDate, TimeZone = timeZone }),
            TimeProvider.System);

    [Fact]
    public void referenceDate_pinsToday()
    {
        // Without this, the today-only default returns nothing when working from an
        // old backup and the viewer looks broken.
        Assert.Equal(new DateOnly(2026, 7, 8), Build("2026-07-08").Today);
    }

    [Fact]
    public void missingReferenceDate_fallsBackToTheRealDate() =>
        Assert.True(Build(null).Today.Year >= 2024);

    [Fact]
    public void toOffset_appliesSummerTimeForASummerDate()
    {
        var at = Build("2026-07-08").ToOffset(new DateTime(2026, 7, 8, 20, 45, 54));
        Assert.Equal(TimeSpan.FromHours(2), at.Offset);
    }

    [Fact]
    public void toOffset_appliesWinterTimeForAWinterDate()
    {
        var at = Build("2026-01-15").ToOffset(new DateTime(2026, 1, 15, 20, 45, 54));
        Assert.Equal(TimeSpan.FromHours(1), at.Offset);
    }

    [Fact]
    public void unknownTimeZone_doesNotThrow()
    {
        // A typo in configuration must not take the API down on the range PC.
        var clock = Build("2026-07-08", "Not/AZone");
        Assert.Equal(new DateOnly(2026, 7, 8), clock.Today);
    }
}
