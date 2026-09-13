using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace Sintro.ResultViewer.Data;

/// <summary>A supplied cursor cannot be honoured; answered with 400 rather than silently paging from the start.</summary>
public sealed class InvalidCursorException(string detail) : Exception(detail);

/// <summary>Opaque keyset cursor: stable while the device inserts and prunes during paging, and the basis of incremental sync.</summary>
public static class Cursor
{
    public static string Encode(params object?[] parts) =>
        WebEncoders.Base64UrlEncode(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(parts.Select(part => part?.ToString()))));

    /// <summary>The encoded parts, or null when no cursor was passed; throws <see cref="InvalidCursorException"/> for anything else.</summary>
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
