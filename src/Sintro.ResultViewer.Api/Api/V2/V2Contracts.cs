using Sintro.ResultViewer.Domain;

namespace Sintro.ResultViewer.Api.V2;

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
