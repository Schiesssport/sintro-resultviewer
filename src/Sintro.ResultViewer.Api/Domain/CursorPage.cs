namespace Sintro.ResultViewer.Domain;

/// <summary>
/// A keyset-paged slice. <paramref name="NextCursor"/> is null on the last page; otherwise pass
/// it back as <c>?cursor=</c>.
///
/// Deliberately carries no total: counting the whole filtered set costs a second full scan on
/// every request, and that cost grows with the table while the count itself is never needed to
/// page. Clients page until <c>nextCursor</c> is null.
///
/// Lives in Domain rather than in an API version because the repository produces it: paging is a
/// property of how the device data is read, not of one wire format.
/// </summary>
public sealed record CursorPage<T>(
    IReadOnlyList<T> Items,
    string? NextCursor,
    int Limit);
