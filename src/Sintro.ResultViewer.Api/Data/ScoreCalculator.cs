using Sintro.ResultViewer.Domain;

namespace Sintro.ResultViewer.Data;

/// <summary>Turns raw device rows into scored series. Pure, so every scoring rule is testable in isolation; the measured facts behind the rules are in docs/device-database.md.</summary>
public static class ScoreCalculator
{
    private const int MarkerShotNumber = 9999;
    private const int EndOfProgramTotalType = 7;
    private const int SightingShotType = 0;

    /// <summary>HitPosition uses this for "no sector reported"; 0 legitimately means a centre hit.</summary>
    private const int NoHitSector = 255;

    // ShotNr 9999 alone marks the synthetic end row: the last real shot of a pass carries TotalType 7 as well.
    private static bool IsMarker(ShotRow row) => row.ShotNr == MarkerShotNumber;

    // ShotType, never ShotGroup 0: counting shots occur in group 0 and sighting shots in higher groups.
    private static bool IsSighting(ShotRow row) => row.ShotType == SightingShotType;

    public sealed record ProgramScore(
        IReadOnlyList<ShotSeries> Series,
        IReadOnlyList<ShotSeries> Sighting,
        ProgramTotal? Total,
        TotalUnavailableReason? TotalUnavailable,
        IReadOnlyList<int> ShotValues,
        int ShotCount);

    public static ProgramScore Calculate(
        DateTime programStart,
        ISintroClock clock,
        IEnumerable<ShotRow> shots,
        IEnumerable<TargetInfoRow> targetInfo)
    {
        var targets = ResolveTargetInfo(targetInfo);

        var realShots = shots.Where(row => !IsMarker(row)).OrderBy(row => row.ShotID).ToList();
        var countingShots = realShots.Where(row => !IsSighting(row)).ToList();

        var series = GroupIntoSeries(countingShots, targets, programStart, clock);
        var sighting = GroupIntoSeries(realShots.Where(IsSighting), targets, programStart, clock);

        var (total, unavailable) = BuildTotal(series);

        return new ProgramScore(
            series,
            sighting,
            total,
            unavailable,
            countingShots.Select(row => row.PrimaryResult).ToList(),
            countingShots.Count);
    }

    public static string? FindEndShotTime(IEnumerable<ShotRow> shots) =>
        shots.Where(row => row.TotalType == EndOfProgramTotalType)
             .OrderByDescending(row => row.ShotID)
             .Select(row => row.ShotTime)
             .FirstOrDefault();

    // Duplicate rows per (program, group) exist and can disagree; the highest TargeinformationID is current.
    private static Dictionary<int, (int? Valuation, int? TargetType)> ResolveTargetInfo(
        IEnumerable<TargetInfoRow> rows) =>
        rows.GroupBy(row => row.ShotGroup)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var current = group.OrderByDescending(row => row.TargeinformationID).First();
                    return ((int?)current.TargetValuation, (int?)current.TargetType);
                });

    private static List<ShotSeries> GroupIntoSeries(
        IEnumerable<ShotRow> rows,
        Dictionary<int, (int? Valuation, int? TargetType)> targets,
        DateTime programStart,
        ISintroClock clock) =>
        rows.GroupBy(row => row.ShotGroup)
            .OrderBy(group => group.Key)
            .Select(group => BuildSeries(group.Key, group, targets, programStart, clock))
            .ToList();

    private static ShotSeries BuildSeries(
        int index,
        IEnumerable<ShotRow> rows,
        Dictionary<int, (int? Valuation, int? TargetType)> targets,
        DateTime programStart,
        ISintroClock clock)
    {
        var ordered = rows.OrderBy(row => row.ShotID).ToList();
        targets.TryGetValue(index, out var target);

        // Misses report a fine value of 0, which would otherwise win "best".
        var scoring = ordered.Where(row => row.PrimaryResult > 0).ToList();

        return new ShotSeries(
            index,
            target.Valuation,
            TargetKind.Code(target.TargetType, target.Valuation),
            ordered.Count,
            ordered.Sum(row => row.PrimaryResult),
            scoring.Count == 0 ? null : scoring.Max(row => row.SecondaryResult),
            ordered.Select(row => ToShot(row, programStart, clock)).ToList());
    }

    private static Shot ToShot(ShotRow row, DateTime programStart, ISintroClock clock)
    {
        var at = SintroTime.CombineShotTime(programStart, row.ShotTime);

        return new Shot(
            Number: row.ShotNr,
            Value: row.PrimaryResult,
            FineValue: row.SecondaryResult,
            Mouche: row.Mouche == 1,
            HitSector: row.HitPosition == NoHitSector ? null : row.HitPosition,
            X: row.X,
            Y: row.Y,
            At: at is null ? null : clock.ToOffset(at.Value));
    }

    private static (ProgramTotal?, TotalUnavailableReason?) BuildTotal(List<ShotSeries> series)
    {
        if (series.Count == 0) return (null, null);

        var valuations = series.Select(entry => entry.Valuation).Distinct().ToList();

        if (valuations.Any(valuation => valuation is null))
            return (null, TotalUnavailableReason.UnknownValuation);

        if (valuations.Count > 1)
            return (null, TotalUnavailableReason.MixedValuation);

        return (new ProgramTotal(series.Sum(entry => entry.Subtotal), valuations[0]!.Value), null);
    }
}
