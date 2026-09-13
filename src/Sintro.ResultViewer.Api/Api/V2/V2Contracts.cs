using Sintro.ResultViewer.Domain;

namespace Sintro.ResultViewer.Api.V2;

// Wire contracts owned by v2. Everything outside Api/V2 is version-agnostic: the device
// layer (Data/), the domain model (Domain/), security, the live hub. A future v3 adds
// Api/V3 with its own routes and its own shapes and nothing shared has to move.
//
// v2 currently serialises the domain records directly, so only the envelope types that are
// genuinely v2 decisions live here. When a version needs a different shape, that version
// gets its own contract records and maps to them — it does not reshape Domain/.

/// <summary>
/// A keyset-paged slice. <paramref name="NextCursor"/> is null on the last page; otherwise pass
/// it back as <c>?cursor=</c>.
///
/// Deliberately carries no total: counting the whole filtered set costs a second full scan on
/// every request, and that cost grows with the table while the count itself is never needed to
/// page. Clients page until <c>nextCursor</c> is null.
/// </summary>
public sealed record CursorPage<T>(
    IReadOnlyList<T> Items,
    string? NextCursor,
    int Limit);

/// <summary>A rejected request. <paramref name="Error"/> is a stable code; Detail is for humans.</summary>
public sealed record ApiError(string Error, string Detail);

/// <summary>All shooters on one licence plus the programs attributed to it.</summary>
public sealed record ShooterDetail(
    string License,
    IReadOnlyList<Shooter> Shooters,
    CursorPage<ShootingProgram> Programs);

/// <summary>
/// <paramref name="PublicExposure"/> lists any configured non-private CIDR ranges so the viewer
/// can show the same red warning the log prints at startup.
/// </summary>
public sealed record HealthReport(
    bool DatabaseReachable,
    DateOnly Today,
    int LiveClients,
    IReadOnlyList<string> PublicExposure);
