using Sintro.ResultViewer.Domain;

namespace Sintro.ResultViewer.Data;

public sealed record ProgramFilter
{
    public ProgramState? State { get; init; }
    public int? Number { get; init; }
    public string? Name { get; init; }
    public string? License { get; init; }
    public int? Lane { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }

    /// <summary>
    /// Include programs the device started but that carry no counting shots: aborted or cleared
    /// runs. Excluded by default so result lists show results.
    /// </summary>
    public bool WithoutResult { get; init; }

    /// <summary>Page size, already clamped by the caller. Required so no second default can drift from SintroOptions.</summary>
    public required int Limit { get; init; }

    /// <summary>Opaque keyset cursor from a previous page's <c>nextCursor</c>.</summary>
    public string? Cursor { get; init; }

    /// <summary>
    /// Oldest-first. This is the sync direction: store the last cursor, ask again with the same
    /// cursor later, and receive exactly the programs added since. Default is newest-first.
    /// </summary>
    public bool Ascending { get; init; }
}
