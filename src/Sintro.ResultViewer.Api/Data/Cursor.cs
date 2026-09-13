using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace Sintro.ResultViewer.Data;

/// <summary>
/// A cursor was supplied but cannot be honoured. Client error, answered with 400: quietly paging
/// from the start instead would hand a syncing client every historic record again with nothing
/// in the response to say why.
/// </summary>
public sealed class InvalidCursorException(string detail) : Exception(detail);

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

    /// <summary>
    /// The parts a cursor was encoded from, or null when no cursor was passed at all.
    /// Throws <see cref="InvalidCursorException"/> for anything in between.
    /// </summary>
    public static string?[]? Decode(string? cursor, int expectedParts)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return null;

        string?[]? decoded;
        try
        {
            decoded = JsonSerializer.Deserialize<string?[]>(
                Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(cursor)));
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            throw Malformed();
        }

        if (decoded is null || decoded.Length != expectedParts) throw Malformed();
        return decoded;
    }

    public static int DecodeInt(string? part) =>
        int.TryParse(part, out var value) ? value : throw Malformed();

    private static InvalidCursorException Malformed() =>
        new("The cursor is not one this API issued. Pass back a nextCursor exactly as received, or omit it to start from the beginning.");
}
