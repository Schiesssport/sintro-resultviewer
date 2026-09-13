using Sintro.ResultViewer.Domain;

namespace Sintro.ResultViewer.Api.V2;

// Wire envelopes owned by v2. Data/ and Domain/ are version-agnostic; v2 serialises the domain records directly.

/// <summary>Every non-2xx body, middlewares included: a stable code a client can switch on plus human-readable detail.</summary>
public sealed record ApiError(string Error, string Detail);

/// <summary>All shooters on one licence plus the programs attributed to it.</summary>
public sealed record ShooterDetail(
    string License,
    IReadOnlyList<Shooter> Shooters,
    CursorPage<ShootingProgram> Programs);

/// <summary><c>PublicExposure</c> lists any configured non-private CIDR ranges so the viewer can show the startup warning.</summary>
public sealed record HealthReport(
    bool DatabaseReachable,
    DateOnly Today,
    int LiveClients,
    IReadOnlyList<string> PublicExposure);
