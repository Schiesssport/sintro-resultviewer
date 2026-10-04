// Writes the rows a Sintro 300 would have produced for seed/definitions.mjs, as T-SQL on stdout.
// Deterministic: the same definitions give the same database every time.
//
//   node seed/generate.mjs > seed.sql
//
// Device conventions reproduced here are the ones docs/device-database.md records. Where that
// file says "unverified", so is this generator.

import { TODAY, LANE_COUNT, CLUBS, SHOOTERS, PROGRAMS, SESSIONS } from './definitions.mjs';

const TARGET_TYPE = { A: 0, B: 1, S: 3 };
const FIRE_METHOD = { sighting: 0, EF: 1, SF: 2 };
const MARKER_SHOT_NR = 9999;
const NO_SECTOR = 255;
const END_OF_PROGRAM = 7;
const END_OF_SERIES = 1;

// Small deterministic PRNG (mulberry32), so a re-run reproduces the same shots.
const random = (() => {
    let state = 0x5eed;
    return () => {
        state = (state + 0x6d2b79f5) | 0;
        let t = Math.imul(state ^ (state >>> 15), 1 | state);
        t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
        return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
})();

const gaussian = (mean, spread) => {
    const u = 1 - random();
    const v = random();
    return mean + spread * Math.sqrt(-2 * Math.log(u)) * Math.cos(2 * Math.PI * v);
};

const clamp = (value, min, max) => Math.min(max, Math.max(min, value));
const quote = (text) => `'${String(text).replace(/'/g, "''")}'`;
const pad = (value, width = 2) => String(value).padStart(width, '0');

// -- Time ----------------------------------------------------------------------

const startTimeText = (date) =>
    `${pad(date.getDate())}.${pad(date.getMonth() + 1)}.${date.getFullYear()}-${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`;

const shotTimeText = (date) =>
    `${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}.${pad(Math.floor(date.getMilliseconds() / 10))}`;

const centisecondsSinceNewYear = (date) =>
    String(Math.floor((date - new Date(date.getFullYear(), 0, 1)) / 10));

const localDate = (day, time) => new Date(`${day}T${time}:00`);

// -- Shots ---------------------------------------------------------------------

// Fine value 0..100 from the shooter's skill; anonymous passes shoot like a club average.
const fineValue = (shooter) =>
    Math.round(clamp(gaussian(shooter?.skill ?? 80, shooter?.spread ?? 12), 0, 100));

// Ring value in the active valuation. On a 100er target the fine value is the ring value.
const ringValue = (fine, valuation) => {
    if (fine === 0) return 0;
    if (valuation === 100) return fine;
    return clamp(Math.ceil(fine / (100 / valuation)), 1, valuation);
};

// Sector 1 is twelve o'clock, clockwise; 0 is a centre hit. X/Y in 1/100 mm, Y up.
const hitGeometry = (fine) => {
    if (fine >= 96) return { sector: 0, x: 0, y: 0 };
    const sector = 1 + Math.floor(random() * 8);
    const angle = (90 - (sector - 1) * 45) * Math.PI / 180;
    const radius = (100 - fine) * 12;
    return { sector, x: Math.round(radius * Math.cos(angle)), y: Math.round(radius * Math.sin(angle)) };
};

const shotRow = ({ program, lane, shooter, stage, groupIndex, shotNr, at, isLastOfSeries, isLastOfProgram, matchCode, logEvent }) => {
    const valuation = stage.valuation ?? program.valuation;
    const fine = fineValue(shooter);
    const ring = ringValue(fine, valuation);
    const hit = hitGeometry(fine);
    const totalType = isLastOfProgram ? END_OF_PROGRAM : isLastOfSeries ? END_OF_SERIES : 0;

    return [
        0, lane, shotNr, ring, fine, hit.sector, stage.kind === 'sighting' ? 0 : 1, quote(shotTimeText(at)),
        hit.sector === 0 && fine >= 98 ? 1 : 0, hit.x, hit.y, 1, 0, totalType, groupIndex,
        FIRE_METHOD[stage.kind], logEvent, 3, quote(centisecondsSinceNewYear(at)), 1, 9,
        quote(TARGET_TYPE[program.target]), matchCode, stage.kind === 'sighting' ? 1 : 0,
    ];
};

const markerRow = ({ lane, at, matchCode }) => [
    0, lane, MARKER_SHOT_NR, 0, 0, NO_SECTOR, 1, quote(shotTimeText(at)), 0, 0, 0, 1, 0, END_OF_PROGRAM, 0,
    FIRE_METHOD.EF, 0, 3, quote(centisecondsSinceNewYear(at)), 1, 9, quote('0'), matchCode, 0,
];

const SHOT_COLUMNS = 'StartNr, LaneNr, ShotNr, PrimaryResult, SecondaryResult, HitPosition, ShotType, ShotTime, Mouche, X, Y, InTime, InsDel, TotalType, ShotGroup, FireMethod, LogEvent, LogType, TimeSinceNewYear, GunType, ShotPosition, TargetType, ExternalNumber, BreakMode';

// -- Passes --------------------------------------------------------------------

// Ids are assigned here rather than by the server, so the generated file is plain INSERTs and
// a program id in a test or a URL (/api/v2/programs/2000) is stable across re-seeds.
const FIRST_PROGRAM_ID = 2000;
const shooterId = (index) => index + 1;

// One pass: the Programs row and one Targetinformation row per stage (written when the operator
// loads the program), then the shots and the end marker, each stamped with its moment so the
// session can interleave lanes the way the device does: ShotID order is wall-clock order.
const buildPass = ({ session, pass, programId, startAt }) => {
    const program = PROGRAMS.find((entry) => entry.number === pass.program);
    const shooter = pass.shooter === null ? null : SHOOTERS[pass.shooter];
    const state = pass.state ?? 'finished';

    const loaded = [
        `INSERT INTO dbo.Programs (ProgramID, Number, Name, StartTime, LaneNr, ContestShooterName, ShooterID) VALUES (${programId}, ${program.number}, ${quote(program.name)}, ${quote(startTimeText(startAt))}, ${pass.lane}, '', ${shooter ? shooterId(pass.shooter) : 'NULL'});`,
        ...program.stages.map((stage, groupIndex) =>
            `INSERT INTO dbo.Targetinformation (TargetType, TargetValuation, ShotGroup, ProgramID) VALUES (${TARGET_TYPE[program.target]}, ${stage.valuation ?? program.valuation}, ${groupIndex}, ${programId});`),
    ];

    let at = new Date(startAt.getTime() + 45_000);
    let logEvent = 0;
    const shots = [];
    const shotsToFire = state === 'abandoned' ? [] : program.stages;
    const lastStage = state === 'active' ? -1 : shotsToFire.length - 1;

    const insertShot = (row) => shots.push({ at, sql: `INSERT INTO dbo.Shots (${SHOT_COLUMNS}, ProgramID) VALUES (${row.join(', ')}, ${programId});` });

    shotsToFire.forEach((stage, groupIndex) => {
        for (let shotNr = 1; shotNr <= stage.shots; shotNr++) {
            at = new Date(at.getTime() + (stage.kind === 'SF' ? 2_500 : 20_000) + random() * 8_000);
            insertShot(shotRow({
                program, lane: pass.lane, shooter, stage, groupIndex, shotNr, at,
                isLastOfSeries: shotNr === stage.shots,
                isLastOfProgram: groupIndex === lastStage && shotNr === stage.shots,
                matchCode: session.matchCode, logEvent: logEvent++,
            }));
        }
    });

    if (state !== 'active') {
        at = new Date(at.getTime() + 5_000);
        insertShot(markerRow({ lane: pass.lane, at, matchCode: session.matchCode }));
    }

    const after = state === 'active' ? [`UPDATE dbo.Lanes SET ProgramID = ${programId} WHERE Number = ${pass.lane};`] : [];

    return { loaded, shots, after, endedAt: at };
};

// -- Whole database ------------------------------------------------------------

const header = () => [
    '-- Generated by seed/generate.mjs from seed/definitions.mjs. Do not edit; edit the definitions.',
    `-- "Today" for this data set is ${TODAY}.`,
    'SET NOCOUNT ON;',
];

// ClubID is the club number's digits as an integer, as on the device.
const clubId = (club) => Number(club.number.replace(/\./g, ''));

const clubsSql = () => CLUBS.map((club) =>
    `INSERT INTO dbo.Club (ClubID, ClubNumber, ClubName) VALUES (${clubId(club)}, ${quote(club.number)}, ${quote(club.name)});`);

// RFID is the all-zero placeholder every real installation carries; registration is by licence barcode.
const shootersSql = () => SHOOTERS.map((shooter, index) =>
    `INSERT INTO dbo.Shooters (ShooterID, FirstName, LastName, RFID, StartNr, ClubID) VALUES (${shooterId(index)}, ${quote(shooter.first)}, ${quote(shooter.last)}, '0000000000', ${quote(shooter.licence)}, ${clubId(CLUBS[shooter.club])});`);

const lanesSql = () =>
    Array.from({ length: LANE_COUNT }, (_, index) => `INSERT INTO dbo.Lanes (Number, ProgramID) VALUES (${index + 1}, NULL);`);

// Passes on one lane follow each other; passes on different lanes overlap, and their shots are
// written in the order they were fired across the whole line.
const sessionsSql = () => {
    const lines = [];
    let programId = FIRST_PROGRAM_ID;
    for (const session of SESSIONS) {
        const laneFreeAt = new Map();
        const built = session.passes.map((pass) => {
            const earliest = localDate(session.day, session.start);
            const startAt = new Date(Math.max(earliest.getTime(), (laneFreeAt.get(pass.lane) ?? 0) + 60_000));
            const result = buildPass({ session, pass, programId: programId++, startAt });
            laneFreeAt.set(pass.lane, result.endedAt.getTime());
            return result;
        });
        lines.push(...built.flatMap((pass) => pass.loaded));
        lines.push(...built.flatMap((pass) => pass.shots).sort((a, b) => a.at - b.at).map((shot) => shot.sql));
        lines.push(...built.flatMap((pass) => pass.after));
    }
    return lines;
};

const withIdentityInsert = (table, statements) =>
    statements.length === 0 ? [] : [`SET IDENTITY_INSERT dbo.${table} ON;`, ...statements, `SET IDENTITY_INSERT dbo.${table} OFF;`];

process.stdout.write([
    ...header(),
    ...withIdentityInsert('Club', clubsSql()),
    ...withIdentityInsert('Shooters', shootersSql()),
    ...lanesSql(),
    ...withIdentityInsert('Programs', sessionsSql()),
].join('\n') + '\n');
