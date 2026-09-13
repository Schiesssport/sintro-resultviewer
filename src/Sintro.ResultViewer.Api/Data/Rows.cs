namespace Sintro.ResultViewer.Data;

// Raw shapes straight out of the device tables. Column names are kept exactly as the DB
// spells them (including the Targetinformation PK's missing "t") so a query and its row
// type can be compared line by line. Everything is translated into Domain types before
// it leaves the repository.

public sealed record ProgramRow(
    int ProgramID,
    int Number,
    string Name,
    string? StartTime,
    int LaneNr,
    string? ContestShooterName,
    int? ShooterID,
    DateTime? StartedAt,
    int HasEndMarker,
    int CountingShots,
    int IsActive);

public sealed record ShotRow(
    int ShotID,
    int? ProgramID,
    int ShotNr,
    int PrimaryResult,
    int SecondaryResult,
    int HitPosition,
    int ShotType,
    string? ShotTime,
    int Mouche,
    double X,
    double Y,
    int TotalType,
    int ShotGroup);

public sealed record TargetInfoRow(
    int TargeinformationID,
    int ProgramID,
    int ShotGroup,
    int TargetValuation,
    int TargetType);

public sealed record ShooterRow(
    int ShooterID,
    string? FirstName,
    string? LastName,
    string? RFID,
    string? StartNr,
    int? ClubID,
    string? ClubNumber,
    string? ClubName);

public sealed record ClubRow(int ClubID, string? ClubNumber, string? ClubName);

public sealed record LaneRow(int LaneID, int Number, int? ProgramID);

public sealed record CatalogRow(int Number, string Name, int ProgramCount, DateTime? LastStartedAt);
