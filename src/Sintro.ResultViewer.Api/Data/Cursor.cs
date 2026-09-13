using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace Sintro.ResultViewer.Data;

/// <summary>
/// Opaque keyset cursor. Clients must treat the value as a blob and only ever echo it back.
///
/// Keyset rather than offset paging because offsets skip or repeat rows when the underlying set
/// shifts — and this set shifts constantly: the device inserts while a client pages, and prunes
/// old programs from the other end. A cursor is also what makes incremental sync work: ask for
/// ascending order and pass the last cursor you stored to get exactly what is new.
/// </summary>
public static class Cursor
{
    public static string Encode(params object?[] parts) =>
        WebEncoders.Base64UrlEncode(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(parts.Select(part => part?.ToString()))));

    public static bool TryDecode(string? cursor, int expectedParts, out string?[] parts)
    {
        parts = [];
        if (string.IsNullOrWhiteSpace(cursor)) return false;

        try
        {
            var decoded = JsonSerializer.Deserialize<string?[]>(
                Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(cursor)));

            if (decoded is null || decoded.Length != expectedParts) return false;

            parts = decoded;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            // A malformed cursor is client error, not a server fault: page from the start.
            return false;
        }
    }

    public static bool TryDecodeInt(string? cursor, out int value)
    {
        value = 0;
        return TryDecode(cursor, 1, out var parts) && int.TryParse(parts[0], out value);
    }
}
