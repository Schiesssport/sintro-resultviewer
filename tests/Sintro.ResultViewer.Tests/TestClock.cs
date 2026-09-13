using Sintro.ResultViewer.Data;

namespace Sintro.ResultViewer.Tests;

/// <summary>Fixed +02:00 (Swiss summer time) so expected ISO strings are literal.</summary>
public sealed class TestClock(DateOnly? today = null) : ISintroClock
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(2);

    public DateOnly Today { get; } = today ?? new DateOnly(2026, 7, 8);

    public DateTimeOffset ToOffset(DateTime naiveLocalTime) =>
        new(DateTime.SpecifyKind(naiveLocalTime, DateTimeKind.Unspecified), Offset);
}
