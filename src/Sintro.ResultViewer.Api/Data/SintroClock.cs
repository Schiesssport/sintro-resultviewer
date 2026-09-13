using System.Globalization;
using Microsoft.Extensions.Options;

namespace Sintro.ResultViewer.Data;

public interface ISintroClock
{
    /// <summary>The date the today-only default resolves to.</summary>
    DateOnly Today { get; }

    /// <summary>Attaches the range's UTC offset to a naive device timestamp.</summary>
    DateTimeOffset ToOffset(DateTime naiveLocalTime);
}

public sealed class SintroClock : ISintroClock
{
    private readonly TimeZoneInfo _zone;
    private readonly DateOnly? _referenceDate;
    private readonly TimeProvider _time;

    public SintroClock(IOptions<SintroOptions> options, TimeProvider time)
    {
        var settings = options.Value;
        _time = time;
        _zone = ResolveZone(settings.TimeZone);
        _referenceDate = DateOnly.TryParse(settings.ReferenceDate, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    public DateOnly Today =>
        _referenceDate ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _zone).DateTime);

    public DateTimeOffset ToOffset(DateTime naiveLocalTime)
    {
        // The autumn DST overlap resolves to standard time; the device stores no zone, so no reading can do better.
        var unspecified = DateTime.SpecifyKind(naiveLocalTime, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, _zone.GetUtcOffset(unspecified));
    }

    // The host zone is the range's zone (device, database and service share one machine); a bad id falls back rather than failing startup.
    private static TimeZoneInfo ResolveZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Local;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }
}
