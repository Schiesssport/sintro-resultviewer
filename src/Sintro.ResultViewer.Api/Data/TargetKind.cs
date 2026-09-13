namespace Sintro.ResultViewer.Data;

/// <summary>
/// Maps the device's <c>Targetinformation.TargetType</c> to the Swiss target letter.
///
/// Confirmed against exported data: TargetType 0 correlates exactly with A-prefixed program
/// names and TargetType 1 with B-prefixed ones, with no crossover. The letter plus the ring scale
/// is the "A10" / "B4" / "A100" notation operators already use in program names.
///
/// TargetType 3 is the Sau silhouette ("S").
///
/// This is the only place the mapping lives; extend it here if the device gains another target.
/// </summary>
public static class TargetKind
{
    public const string UnknownLetter = "?";

    public static string? Letter(int? targetType) => targetType switch
    {
        0 => "A",
        1 => "B",
        3 => "S",   // Sau silhouette
        _ => null,
    };

    /// <summary>The compact code shown next to a series' shots, e.g. "A10", "B4", "?10".</summary>
    public static string Code(int? targetType, int? valuation) =>
        $"{Letter(targetType) ?? UnknownLetter}{(valuation?.ToString() ?? UnknownLetter)}";
}
