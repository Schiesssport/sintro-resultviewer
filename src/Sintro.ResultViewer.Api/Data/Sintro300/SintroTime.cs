using System.Globalization;

namespace Sintro.ResultViewer.Data.Sintro300;

/// <summary>Combines the bare Shots.ShotTime "HH:mm:ss.ff" with the pass start; local wall-clock time without a zone.</summary>
public static class SintroTime
{
    public static DateTime? CombineShotTime(DateTime programStart, string? shotTime)
    {
        if (!TimeSpan.TryParse(shotTime, CultureInfo.InvariantCulture, out var timeOfDay)) return null;

        // A bare time before the start means the pass ran past midnight; a minute of tolerance absorbs clock jitter.
        var combined = programStart.Date + timeOfDay;
        if (combined < programStart.AddMinutes(-1)) combined = combined.AddDays(1);
        return combined;
    }
}
