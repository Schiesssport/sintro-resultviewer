namespace Sintro.ResultViewer.Data;

/// <summary>Maps Targetinformation.TargetType to the Swiss target letter (0=A, 1=B, 3=S for the Sau silhouette); the only place this mapping lives.</summary>
public static class TargetKind
{
    private const string UnknownLetter = "?";

    private static string? Letter(int? targetType) => targetType switch
    {
        0 => "A",
        1 => "B",
        3 => "S",
        _ => null,
    };

    /// <summary>The compact code shown next to a series' shots, e.g. "A10", "B4", "?10".</summary>
    public static string Code(int? targetType, int? valuation) =>
        $"{Letter(targetType) ?? UnknownLetter}{(valuation?.ToString() ?? UnknownLetter)}";
}
