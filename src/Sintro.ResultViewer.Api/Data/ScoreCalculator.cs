using Sintro.ResultViewer.Domain;

namespace Sintro.ResultViewer.Data;

/// <summary>
/// Turns raw device rows into scored series. Pure — no database, no clock of its own — because
/// this is where every scoring subtlety of the schema lives and it must be testable in isolation.
///
/// Measured facts this encodes:
///   - ShotNr 9999 rows are synthetic end-of-program markers, not shots. TotalType 7 alone
///     does NOT mark one: the last real shot of a pass carries it too (645 such shots against
///     438 markers in one export), so filtering on it would drop every final shot.
///   - Sighting shots are identified by ShotType 0, NOT by ShotGroup 0: counting shots do occur
///     in ShotGroup 0, and sighting shots do occur in higher groups.
///   - Ring scale comes from Targetinformation per (ProgramID, ShotGroup) and can change between
///     series inside one program, so a grand total is only meaningful when it is uniform.
///   - (ProgramID, ShotGroup) can carry several Targetinformation rows, which occasionally
///     disagree; the highest TargeinformationID is the current one.
/// </summary>
public static class ScoreCalculator
{
    public const int MarkerShotNumber = 9999;
    public const int EndOfProgramTotalType = 7;
    public const int SightingShotType = 0;

    /// <summary>HitPosition uses this for "no sector reported"; 0 legitimately means a centre hit.</summary>
    private const int NoHitSector = 255;

    public static bool IsMarker(ShotRow row) => row.ShotNr == MarkerShotNumber;

    public static bool IsSighting(ShotRow row) => row.ShotType == SightingShotType;

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

    /// <summary>The end-of-program marker carries the finishing time, so it is read before markers are dropped.</summary>
    public static string? FindEndShotTime(IEnumerable<ShotRow> shots) =>
        shots.Where(row => row.TotalType == EndOfProgramTotalType)
             .OrderByDescending(row => row.ShotID)
             .Select(row => row.ShotTime)
             .FirstOrDefault();

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
            target.TargetType,
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

        return (new ProgramTotal(
            series.Sum(entry => entry.Subtotal),
            series.Sum(entry => entry.ShotCount),
            valuations[0]!.Value), null);
    }
}
