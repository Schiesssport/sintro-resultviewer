namespace Sintro.ResultViewer.Domain;

/// <summary>A keyset-paged slice; <c>NextCursor</c> is null on the last page. Deliberately no total: counting costs a second scan per request and paging never needs it.</summary>
public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor);
