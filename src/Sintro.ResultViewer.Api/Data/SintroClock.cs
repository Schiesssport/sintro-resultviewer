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
        var unspecified = DateTime.SpecifyKind(naiveLocalTime, DateTimeKind.Unspecified);
        // During the autumn DST overlap this resolves to standard time. The device stores no
        // zone information at all, so no reading can do better than pick one.
        return new DateTimeOffset(unspecified, _zone.GetUtcOffset(unspecified));
    }

    /// <summary>
    /// The host's own timezone is the range's timezone — the device, the database and this
    /// service all run on the same machine — so no configuration is needed in practice. The
    /// setting exists only for a host whose clock is set for somewhere else, and a bad value
    /// falls back rather than taking the service down.
    /// </summary>
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
