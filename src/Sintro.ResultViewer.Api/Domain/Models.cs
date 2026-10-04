namespace Sintro.ResultViewer.Domain;

public enum ProgramState
{
    Active,
    Finished,
    Abandoned,
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

/// <summary>Series and shots are in firing order; a position in the array is the only ordinal, the device's group number is not exposed.</summary>
public sealed record ShotSeries(
    string TargetType,
    int? Valuation,
    int Subtotal,
    int? BestFineValue,
    IReadOnlyList<int> FineValues,
    IReadOnlyList<Shot> Shots);

/// <summary>The sum over every counting series of one target and ring scale; a pass that changes scale has several, never one number across scales.</summary>
public sealed record ProgramTotal(string TargetType, int? Valuation, int Value, IReadOnlyList<int> FineValues);

/// <summary>One row of dbo.Programs, a "Stich" in the UI; named ShootingProgram because <c>Program</c> is the entry point, exposed as the <c>program</c> resource.</summary>
public sealed record ShootingProgram(
    int Id,
    int TargetCode,
    string TargetTitle,
    int Lane,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    ProgramState State,
    Shooter? Shooter,
    string? ContestShooterName,
    IReadOnlyList<ProgramTotal> Totals,
    IReadOnlyList<ShotSeries> Series,
    IReadOnlyList<ShotSeries> Sighting);

public sealed record LaneStatus(int Number, ShootingProgram? CurrentProgram);

public sealed record ProgramCatalogEntry(int TargetCode, string TargetTitle, int ProgramCount, DateTimeOffset? LastStartedAt);
