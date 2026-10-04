namespace Sintro.ResultViewer.Data;

public sealed record ShooterFilter
{
    public string? Query { get; init; }
    public int? ClubId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public required int Limit { get; init; }
    public string? Cursor { get; init; }
}
