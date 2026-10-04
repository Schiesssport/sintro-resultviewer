// What the seeded range looks like. Everything invented; edit freely.
// generate.mjs turns this into the rows the device would have written.

// The seed is dated relative to the day it is generated (SEED_TODAY, set by scripts/db-seed.sh
// from the host's clock), so the viewer shows live-looking data without a pinned ReferenceDate.
export const TODAY = process.env.SEED_TODAY ?? new Date().toISOString().slice(0, 10);

export const LANE_COUNT = 6;

// ClubNumber is the Swiss register format 1.KK.R.BB.VVV; ClubID is the same digits as an integer.
export const CLUBS = [
    { number: '1.02.1.04.901', name: 'Feldschützen Musterdorf' },
    { number: '1.02.1.04.902', name: 'Schützengesellschaft Beispielwil' },
    { number: '1.02.1.04.903', name: 'Sportschützen Probstetten' },
];

// licence: six digits, zero-padded, the number printed on the SSV licence; leading zeros are
// real (000001 exists) and must survive every lookup. Keep clear of the
// numbers the API documentation uses as examples (123456, 012345); a test checks the OpenAPI
// document names no licence from the database.
export const SHOOTERS = [
    { club: 0, first: 'Hans',  last: 'Muster',      licence: '000001' },
    { club: 0, first: 'Vreni', last: 'Muster',      licence: '000002' },
    { club: 0, first: 'Ueli',  last: 'Beispiel',    licence: '000003' },
    { club: 1, first: 'Res',   last: 'Probst',      licence: '101001' },
    { club: 1, first: 'Marie', last: 'Exemple',     licence: '101002' },
    { club: 1, first: 'Fritz', last: 'Platzhalter', licence: '102003' },
    { club: 2, first: 'Jean',  last: 'Dupont',      licence: '999001' },
    { club: 2, first: 'Anna',  last: 'Modell',      licence: '999002' },
    { club: 2, first: 'Peter', last: 'Vorlage',     licence: '999003' },
];

// How a real shooter's fine values (0..100) fall, per 100 shots. A 0 is a shot on the wrong
// target; the last band is the occasional wild one.
export const FINE_VALUE_BANDS = [
    { share: 15, min: 96, max: 100 },
    { share: 25, min: 91, max: 95 },
    { share: 40, min: 81, max: 90 },
    { share: 16, min: 71, max: 80 },
    { share: 1,  min: 0,  max: 0 },
    { share: 3,  min: 1,  max: 70 },
];

// Programs in the operator's notation: one token per stage, <target><valuation><kind><shots>,
// where the target is A, B or S, the kind P (Probe, sighting), E (Einzelfeuer, precision) or
// S (Serienfeuer, rapid fire). The device stores number and name; the stages are what it
// shoots. Numbers by family so they are easy to remember: 01x A5, 02x A10, 03x A100, 07x B4,
// 09x special (91 changes target and valuation mid-pass, so the API withholds its grand total).
export const PROGRAMS = [
    { number: 11, name: 'A5P2 A5E2 A5S3 A5S3' },
    { number: 21, name: 'A10P2 A10E10' },
    { number: 22, name: 'A10P2 A10E6 A10E4' },
    { number: 23, name: 'A10E10 A10E10 A10E10 A10E10 A10E10 A10E10' },
    { number: 31, name: 'A100E2 A100E2 A100E2 A100E2 A100E2 A100E2 A100E2 A100E2' },
    { number: 71, name: 'B4E6 B4S3 B4S3 B4S6' },
    { number: 91, name: 'A5E5 B4E5 B4S2 B4S3 B4S5' },
];

const STAGE_KIND = { P: 'sighting', E: 'EF', S: 'SF' };

export const parseStages = (name) => name.split(/\s+/).map((token) => {
    const match = /^([ABS])(\d+)([PES])(\d+)$/.exec(token);
    if (!match) throw new Error(`cannot read stage "${token}" in program "${name}"`);
    return { target: match[1], valuation: Number(match[2]), kind: STAGE_KIND[match[3]], shots: Number(match[4]) };
});

// A session is one block of shooting on one day, daysAgo days before TODAY. Each pass names a program and a shooter (index
// into SHOOTERS, or null for an anonymous pass). Lanes are assigned by the generator: the next
// lane to come free, so a long evening queues up the way a real one does. matchCode is the Stich
// number the operator enters in contest mode (0 outside events); a pass may override the
// session's. Event Stiche are the program family with a leading 1: 111, 121, …
// stopAfter: the shooter ends the program after that many stages (the A100 program is often
// stopped after 4 or 5 of its 8). state: 'finished' (default), 'active' (still on the lane, no
// end marker), 'abandoned' (loaded and left: only the marker row, no shots).
//
// Aim: about 60 Stiche, roughly one in ten anonymous, every program shot by at least three
// different named shooters, all within the last seven days.
const each = (program, shooters, extra = {}) => shooters.map((shooter) => ({ program, shooter, ...extra }));
const anonymous = (program, extra = {}) => ({ program, shooter: null, ...extra });

export const SESSIONS = [
    { daysAgo: 6, start: '18:30', matchCode: 0, passes: [
        ...each(21, [0, 1, 2, 3, 4]),
        ...each(71, [6, 7, 8]),
        anonymous(11),
    ] },
    { daysAgo: 4, start: '18:30', matchCode: 0, passes: [
        ...each(22, [0, 1, 5, 6, 7]),
        ...each(23, [3, 4]),
        ...each(31, [2], { stopAfter: 5 }),
        anonymous(21),
    ] },
    { daysAgo: 2, start: '18:30', matchCode: 0, passes: [
        ...each(11, [0, 1, 2, 3]),
        ...each(31, [4, 5], { stopAfter: 4 }),
        ...each(31, [6]),
        ...each(71, [0, 8]),
        anonymous(71),
    ] },
    { daysAgo: 1, start: '13:30', matchCode: 0, passes: [
        ...each(23, [6, 7, 8, 1]),
        ...each(91, [0, 3, 5]),
        ...each(22, [2, 4, 8]),
        anonymous(22),
    ] },
    { daysAgo: 0, start: '19:00', matchCode: 0, passes: [
        ...each(22, [0, 3, 7, 1, 4, 5, 6], { matchCode: 122 }),
        ...each(21, [0, 2, 8], { matchCode: 121 }),
        ...each(11, [1, 5, 7], { matchCode: 111 }),
        anonymous(11, { matchCode: 111 }),
        ...each(71, [2, 6, 4], { matchCode: 171 }),
        ...each(31, [8], { stopAfter: 4, matchCode: 131 }),
        ...each(31, [3], { stopAfter: 5, matchCode: 131 }),
        anonymous(31, { matchCode: 131 }),
        ...each(91, [5, 1], { matchCode: 191 }),
        anonymous(21, { state: 'abandoned' }),
        ...each(22, [1], { state: 'active', matchCode: 122 }),
    ] },
];
