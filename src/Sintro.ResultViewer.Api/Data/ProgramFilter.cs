using Sintro.ResultViewer.Domain;

namespace Sintro.ResultViewer.Data;

public sealed record ProgramFilter
{
    public ProgramState? State { get; init; }
    public IReadOnlyList<int> TargetCodes { get; init; } = [];
    public string? TargetProgram { get; init; }
    // A pass matches when any real shot carries one of these codes; the device writes it per shot.
    public IReadOnlyList<int> MatchCodes { get; init; } = [];
    public IReadOnlyList<string> Licenses { get; init; } = [];
    public int? Lane { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }

    /// <summary>Include passes with no counting shots (aborted or cleared runs); off by default so lists show results.</summary>
    public bool WithoutResult { get; init; }

    /// <summary>Page size, already clamped by the caller; required so no second default can drift from SintroOptions.</summary>
    public required int Limit { get; init; }

    /// <summary>Opaque keyset cursor from a previous page's <c>nextCursor</c>.</summary>
    public string? Cursor { get; init; }

    /// <summary>Oldest-first, the sync direction; the default is newest-first.</summary>
    public bool Ascending { get; init; }
}
