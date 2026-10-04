namespace Sintro.ResultViewer.Domain;

public enum ProgramState
{
    Active,
    Finished,
    Abandoned,
}

/// <summary>Why <see cref="ShootingProgram.Total"/> is null, so a missing total is debuggable.</summary>
public enum TotalUnavailableReason
{
    MixedValuation,
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
    int? MatchCode,
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
    string TargetType,
    int ShotCount,
    int Subtotal,
    int? BestFineValue,
    IReadOnlyList<Shot> Shots);

public sealed record ProgramTotal(int Value, int Valuation);

/// <summary>One row of dbo.Programs, a "Stich" in the UI; named ShootingProgram because <c>Program</c> is the entry point, exposed as the <c>program</c> resource.</summary>
public sealed record ShootingProgram(
    int Id,
    int TargetCode,
    string TargetProgram,
    int Lane,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    ProgramState State,
    Shooter? Shooter,
    string? ContestShooterName,
    ProgramTotal? Total,
    TotalUnavailableReason? TotalUnavailable,
    int ShotCount,
    IReadOnlyList<int> ShotValues,
    IReadOnlyList<ShotSeries> Series,
    IReadOnlyList<ShotSeries> Sighting);

public sealed record LaneStatus(int Number, ShootingProgram? CurrentProgram);

public sealed record ProgramCatalogEntry(int TargetCode, string TargetProgram, int ProgramCount, DateTimeOffset? LastStartedAt);
