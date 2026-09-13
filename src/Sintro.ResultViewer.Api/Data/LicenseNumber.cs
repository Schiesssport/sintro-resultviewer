namespace Sintro.ResultViewer.Data;

/// <summary>Shooters.StartNr is the SSV licence number; normalised like OpenRangeOffice's normalizeLicense so both tools agree on identity.</summary>
public static class LicenseNumber
{
    private const int MinimumDigits = 6;

    // Digits only, left-padded to six; no upper bound because the planned 7-9 digit format must work without a code change.
    public static string Normalize(string? raw)
    {
        var digits = string.Concat((raw ?? string.Empty).Where(char.IsAsciiDigit));
        if (digits.Length == 0) return string.Empty;
        return digits.Length < MinimumDigits ? digits.PadLeft(MinimumDigits, '0') : digits;
    }
}
