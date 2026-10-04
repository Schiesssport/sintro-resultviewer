using Sintro.ResultViewer.Domain;

namespace Sintro.ResultViewer.Api.V2;

/// <summary>An unpaged collection, shaped like a page so every list answers with <c>items</c>.</summary>
public sealed record Collection<T>(IReadOnlyList<T> Items);

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
