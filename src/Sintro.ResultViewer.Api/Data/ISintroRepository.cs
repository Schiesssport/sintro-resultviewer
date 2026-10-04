using Sintro.ResultViewer.Domain;

namespace Sintro.ResultViewer.Data;

/// <summary>Read-only. Implementations live in a folder named after the device schema (Sintro300).</summary>
public interface ISintroRepository
{
    Task<CursorPage<ShootingProgram>> ListProgramsAsync(ProgramFilter filter, CancellationToken token);
    Task<ShootingProgram?> GetProgramAsync(int id, CancellationToken token);
    Task<IReadOnlyList<LaneStatus>> ListLanesAsync(CancellationToken token);
    Task<string> ReadLiveFingerprintAsync(CancellationToken token);
    Task<CursorPage<Shooter>> ListShootersAsync(ShooterFilter filter, CancellationToken token);
    Task<IReadOnlyList<Shooter>> FindShootersByLicenseAsync(string license, CancellationToken token);
    Task<CursorPage<Club>> ListClubsAsync(string? query, int limit, string? cursor, CancellationToken token);
    Task<IReadOnlyList<ProgramCatalogEntry>> ListProgramCatalogAsync(CancellationToken token);
    Task<bool> CanReachDatabaseAsync(CancellationToken token);
}
