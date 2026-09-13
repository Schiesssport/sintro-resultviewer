namespace Sintro.ResultViewer.Data;

/// <summary>
/// Shooters.StartNr is the SSV licence number, not a sequential start number: six digits,
/// zero-padded and non-sequential. Mirrors
/// OpenRangeOffice's normalizeLicense so the two tools agree on identity.
/// </summary>
public static class LicenseNumber
{
    /// <summary>Shorter input is left-padded to this width; longer input is left alone.</summary>
    public const int MinimumDigits = 6;

    /// <summary>
    /// Strips non-digits and pads to at least six digits. Deliberately imposes no upper
    /// bound — the planned 7-9 digit licence format must work without a code change.
    /// </summary>
    public static string Normalize(string? raw)
    {
        var digits = string.Concat((raw ?? string.Empty).Where(char.IsAsciiDigit));
        if (digits.Length == 0) return string.Empty;
        return digits.Length < MinimumDigits ? digits.PadLeft(MinimumDigits, '0') : digits;
    }

    public static bool AreSame(string? left, string? right)
    {
        var normalized = Normalize(left);
        return normalized.Length > 0 && normalized == Normalize(right);
    }
}

/// <summary>
/// RFID is deprecated and was never widely used: installations share a single all-zero
/// placeholder across many shooters. Registration happens by barcode on the licence number.
/// </summary>
public static class RfidCard
{
    public static string? Clean(string? raw)
    {
        var trimmed = raw?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.All(c => c == '0') ? null : trimmed;
    }
}
