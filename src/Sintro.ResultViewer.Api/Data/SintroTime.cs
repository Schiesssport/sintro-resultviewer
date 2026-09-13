using System.Globalization;

namespace Sintro.ResultViewer.Data;

/// <summary>Parses the device's text timestamps: Programs.StartTime "dd.MM.yyyy-HH:mm:ss" and the bare Shots.ShotTime "HH:mm:ss.ff", both local wall-clock time without a zone.</summary>
public static class SintroTime
{
    private const string StartTimeFormat = "dd.MM.yyyy-HH:mm:ss";

    public static DateTime? ParseStartTime(string? raw) =>
        DateTime.TryParseExact(raw, StartTimeFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

    public static DateTime? CombineShotTime(DateTime programStart, string? shotTime)
    {
        if (!TimeSpan.TryParse(shotTime, CultureInfo.InvariantCulture, out var timeOfDay)) return null;

        // A bare time before the start means the pass ran past midnight; a minute of tolerance absorbs clock jitter.
        var combined = programStart.Date + timeOfDay;
        if (combined < programStart.AddMinutes(-1)) combined = combined.AddDays(1);
        return combined;
    }
}
