using Sintro.ResultViewer.Domain;

namespace Sintro.ResultViewer.Data.Sintro300;

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
        IReadOnlyList<ProgramTotal> Totals);

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

        return new ProgramScore(series, sighting, BuildTotals(series));
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
        int shotGroup,
        IEnumerable<ShotRow> rows,
        Dictionary<int, (int? Valuation, int? TargetType)> targets,
        DateTime programStart,
        ISintroClock clock)
    {
        var ordered = rows.OrderBy(row => row.ShotID).ToList();
        targets.TryGetValue(shotGroup, out var target);

        return new ShotSeries(
            TargetKind.Code(target.TargetType, target.Valuation),
            target.Valuation,
            ordered.Sum(row => row.PrimaryResult),
            ordered.Select(row => ToShot(row, programStart, clock)).ToList());
    }

    private static Shot ToShot(ShotRow row, DateTime programStart, ISintroClock clock)
    {
        var at = SintroTime.CombineShotTime(programStart, row.ShotTime);

        return new Shot(
            Number: row.ShotNr,
            MatchCode: row.ExternalNumber == 0 ? null : row.ExternalNumber,
            Value: row.PrimaryResult,
            FineValue: row.SecondaryResult,
            InnerTen: row.Mouche == 1,
            HitSector: row.HitPosition == NoHitSector ? null : row.HitPosition,
            X: row.X,
            Y: row.Y,
            At: at is null ? null : clock.ToOffset(at.Value));
    }

    // One sum per target and ring scale, in the order first shot: a 5er series added to a 10er one is meaningless.
    private static List<ProgramTotal> BuildTotals(List<ShotSeries> series) =>
        series.GroupBy(entry => entry.TargetType)
              .Select(group => new ProgramTotal(
                  group.Key,
                  group.First().Valuation,
                  group.Sum(entry => entry.Subtotal),
                  group.SelectMany(entry => entry.Shots).Select(shot => shot.FineValue).ToList()))
              .ToList();
}
