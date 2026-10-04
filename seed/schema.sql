-- The six tables the viewer reads, as the Sintro 300 creates them (column order, types and
-- constraints taken from a device database; see docs/device-database.md for what they mean).
-- AnnualProg and JungschuetzenProg are left out: the viewer never reads them.

CREATE TABLE dbo.Club (
    ClubID      int           NOT NULL IDENTITY(1,1) PRIMARY KEY,
    ClubNumber  nvarchar(13)  NOT NULL,
    ClubName    nvarchar(255) NOT NULL
);

CREATE TABLE dbo.Shooters (
    ShooterID   int          NOT NULL IDENTITY(1,1) PRIMARY KEY,
    FirstName   varchar(255) NOT NULL,
    LastName    varchar(255) NOT NULL,
    RFID        varchar(255) NOT NULL,
    StartNr     varchar(255) NOT NULL,
    ClubID      int          NULL REFERENCES dbo.Club (ClubID)
);

CREATE TABLE dbo.Programs (
    ProgramID           int          NOT NULL IDENTITY(1,1) PRIMARY KEY,
    Number              int          NOT NULL,
    Name                varchar(255) NOT NULL,
    StartTime           varchar(255) NOT NULL,
    LaneNr              int          NOT NULL,
    ContestShooterName  varchar(255) NOT NULL,
    ShooterID           int          NULL REFERENCES dbo.Shooters (ShooterID)
);

CREATE TABLE dbo.Shots (
    ShotID            int          NOT NULL IDENTITY(1,1) PRIMARY KEY,
    StartNr           int          NOT NULL,
    LaneNr            int          NOT NULL,
    ShotNr            int          NOT NULL,
    PrimaryResult     int          NOT NULL,
    SecondaryResult   int          NOT NULL,
    HitPosition       int          NOT NULL,
    ShotType          int          NOT NULL,
    ShotTime          varchar(255) NOT NULL,
    Mouche            int          NOT NULL,
    X                 float        NOT NULL,
    Y                 float        NOT NULL,
    InTime            int          NOT NULL,
    InsDel            int          NOT NULL,
    TotalType         int          NOT NULL,
    ShotGroup         int          NOT NULL,
    FireMethod        int          NOT NULL,
    LogEvent          int          NOT NULL,
    LogType           int          NOT NULL,
    TimeSinceNewYear  varchar(255) NOT NULL,
    GunType           int          NOT NULL,
    ShotPosition      int          NOT NULL,
    TargetType        varchar(255) NOT NULL,
    ExternalNumber    int          NOT NULL,
    BreakMode         int          NOT NULL,
    ProgramID         int          NULL REFERENCES dbo.Programs (ProgramID)
);

CREATE TABLE dbo.Targetinformation (
    TargeinformationID  int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    TargetType          int NOT NULL,
    TargetValuation     int NOT NULL,
    ShotGroup           int NOT NULL,
    ProgramID           int NULL REFERENCES dbo.Programs (ProgramID)
);

CREATE TABLE dbo.Lanes (
    LaneID     int NOT NULL IDENTITY(1,1) PRIMARY KEY,
    Number     int NOT NULL,
    ProgramID  int NULL REFERENCES dbo.Programs (ProgramID)
);
