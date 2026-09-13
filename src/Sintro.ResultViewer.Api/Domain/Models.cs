namespace Sintro.ResultViewer.Domain;

public enum ProgramState
{
    /// <summary>Loaded on a line and not yet ended.</summary>
    Active,

    /// <summary>The device wrote an end-of-program total (TotalType 7).</summary>
    Finished,

    /// <summary>No end total and no longer on a line: started and dropped, or displaced when the line was reassigned.</summary>
    Abandoned,
}

/// <summary>Why <see cref="ShootingProgram.Total"/> is null, so a missing total is debuggable.</summary>
public enum TotalUnavailableReason
{
    /// <summary>Series use different ring scales; adding them would be meaningless.</summary>
    MixedValuation,

    /// <summary>At least one series has no Targetinformation row, so its scale is unknown.</summary>
    UnknownValuation,
}

public sealed record Club(int Id, string Number, string Name);

public sealed record Shooter(
    string License,
    string FirstName,
    string LastName,
    int ShooterId,
    Club? Club,
    bool DuplicateLicense);

public sealed record Shot(
    int Number,
    int Value,
    int FineValue,
    bool Mouche,
    // Clock sector 1-8 of the hit, 0 for a centre hit, null when the device reported none.
    int? HitSector,
    double X,
    double Y,
    DateTimeOffset? At);

public sealed record ShotSeries(
    int Index,
    int? Valuation,
    // Target letter plus ring scale, e.g. "A10" or "B4", the notation used in program names.
    string TargetCode,
    int ShotCount,
    int Subtotal,
    // Highest fine value (SecondaryResult, tenths) among the hits; misses report 0 and are excluded so they cannot win it.
    int? BestFineValue,
    IReadOnlyList<Shot> Shots);

public sealed record ProgramTotal(int Value, int Valuation);

/// <summary>One row of dbo.Programs, a "Passe"; named ShootingProgram because <c>Program</c> is the entry point, exposed as the <c>program</c> resource.</summary>
public sealed record ShootingProgram(
    int Id,
    int Number,
    string Name,
    int Lane,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    ProgramState State,
    Shooter? Shooter,
    string? ContestShooterName,
    ProgramTotal? Total,
    TotalUnavailableReason? TotalUnavailable,
    int ShotCount,
    // Counting-shot ring values in firing order.
    IReadOnlyList<int> ShotValues,
    IReadOnlyList<ShotSeries> Series,
    // Sighting shots (Probe), one series per ShotGroup and never counted towards Total; kept per group because ring scales can differ between groups.
    IReadOnlyList<ShotSeries> Sighting);

public sealed record LaneStatus(int Number, ShootingProgram? CurrentProgram);

public sealed record ProgramCatalogEntry(int Number, string Name, int ProgramCount, DateTimeOffset? LastStartedAt);
