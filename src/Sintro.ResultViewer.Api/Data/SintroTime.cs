using System.Globalization;

namespace Sintro.ResultViewer.Data;

/// <summary>
/// The device stores dates as text. Programs.StartTime is "dd.MM.yyyy-HH:mm:ss" and
/// Shots.ShotTime is a bare time of day ("HH:mm:ss.ff"), both in local wall-clock time
/// with no zone. Everything the API emits is ISO 8601, so parsing happens exactly here.
/// </summary>
public static class SintroTime
{
    private const string StartTimeFormat = "dd.MM.yyyy-HH:mm:ss";

    public static DateTime? ParseStartTime(string? raw) =>
        DateTime.TryParseExact(raw, StartTimeFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

    /// <summary>
    /// Rebuilds a shot's full timestamp from the program's date plus the stored time of day.
    /// A program started late in the evening can run past midnight, in which case the bare
    /// time appears to precede the start; that rolls over to the next day.
    /// </summary>
    public static DateTime? CombineShotTime(DateTime programStart, string? shotTime)
    {
        if (!TimeSpan.TryParse(shotTime, CultureInfo.InvariantCulture, out var timeOfDay)) return null;

        var combined = programStart.Date + timeOfDay;
        if (combined < programStart.AddMinutes(-1)) combined = combined.AddDays(1);
        return combined;
    }
}
