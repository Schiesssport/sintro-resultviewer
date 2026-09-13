namespace Sintro.ResultViewer.Domain;

public enum ProgramState
{
    /// <summary>Loaded on a line and not yet ended.</summary>
    Active,

    /// <summary>The device wrote an end-of-program total (TotalType 7).</summary>
    Finished,

    /// <summary>
    /// Neither: no end total was ever written, and the pass is no longer on a line — started
    /// and then abandoned, or displaced when the line was reassigned. Calling these "finished"
    /// would be a lie, and it also made an item's own state disagree with ?state=finished.
    /// They usually carry no shots at all.
    /// </summary>
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
    string? Rfid,
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
    int? TargetType,
    // Target letter + ring scale, e.g. "A10" or "B4" — the notation used in program names.
    string TargetCode,
    int ShotCount,
    int Subtotal,
    // Best (lowest-numbered ring, highest tenth) fine value in the series, or null if empty.
    int? BestFineValue,
    IReadOnlyList<Shot> Shots);

public sealed record ProgramTotal(int Value, int ShotCount, int Valuation);

/// <summary>
/// One row of dbo.Programs: one shooter shooting one program on one lane at one time
/// ("Passe"). Named ShootingProgram because <c>Program</c> is the application entry point;
/// it is exposed as the <c>program</c> resource.
/// </summary>
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
    // Counting-shot ring values in firing order — what result software usually wants.
    IReadOnlyList<int> ShotValues,
    // The same values space-joined, for the viewer's single-column display.
    string ShotValuesText,
    IReadOnlyList<ShotSeries> Series,
    // Sighting shots (Probe). Never counted towards Total.
    ShotSeries? Sighting);

public sealed record LaneStatus(int Number, ShootingProgram? CurrentProgram);

public sealed record ProgramCatalogEntry(int Number, string Name, int ProgramCount, DateTimeOffset? LastShotAt);
