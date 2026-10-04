namespace Sintro.ResultViewer.Data.Sintro300;

/// <summary>Matching in C#, not SQL, keeps the licence normalisation rule in one place.</summary>
internal sealed class LicenseIndex(Dictionary<string, List<int>> shootersByLicense)
{
    public static LicenseIndex From(IEnumerable<(int ShooterID, string? StartNr)> rows) =>
        new(rows
            .Select(row => (row.ShooterID, License: LicenseNumber.Normalize(row.StartNr)))
            .Where(entry => entry.License.Length > 0)
            .GroupBy(entry => entry.License)
            .ToDictionary(group => group.Key, group => group.Select(entry => entry.ShooterID).ToList()));

    public List<int> Resolve(string? license)
    {
        var normalized = LicenseNumber.Normalize(license);
        return normalized.Length == 0 ? [] : shootersByLicense.GetValueOrDefault(normalized, []);
    }

    public bool IsDuplicate(string normalizedLicense) =>
        shootersByLicense.TryGetValue(normalizedLicense, out var ids) && ids.Count > 1;
}
