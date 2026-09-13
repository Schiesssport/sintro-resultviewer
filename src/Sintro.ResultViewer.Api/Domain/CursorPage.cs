namespace Sintro.ResultViewer.Domain;

/// <summary>A keyset-paged slice. <c>NextCursor</c> is the position after the last item (null only for an empty page) so a sync client can always store it; <c>HasMore</c> says whether a further page exists right now.</summary>
public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor, bool HasMore);
