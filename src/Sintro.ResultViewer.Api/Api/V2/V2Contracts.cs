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
/// Every non-2xx answer this API gives, from the middlewares as well as the endpoints.
/// <paramref name="Error"/> is a stable code a client can switch on; Detail is for humans.
/// One shape throughout, so a client needs exactly one error parser.
/// </summary>
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
