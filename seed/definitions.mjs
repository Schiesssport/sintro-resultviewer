// What the seeded range looks like. Everything invented; edit freely.
// generate.mjs turns this into the rows the device would have written.

// The day the tests and the dev config treat as "today" (Sintro__ReferenceDate).
export const TODAY = '2026-07-08';

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

// A session is one block of shooting on one day. Each pass names a lane, a program and a shooter
// (index into SHOOTERS, or null for an anonymous pass, the common case on a range).
// matchCode is the Stich number the operator enters in contest mode (0 outside events); a pass
// may override the session's. Event Stiche are the program family with a leading 1: 111, 121, …
// stopAfter: the shooter ends the program after that many stages (the A100 program is often
// stopped after 4 or 5 of its 8). state: 'finished' (default), 'active' (still on the lane, no
// end marker), 'abandoned' (loaded and left: only the marker row, no shots).
export const SESSIONS = [
    { day: '2026-07-01', start: '18:30', matchCode: 0, passes: [
        { lane: 1, program: 21, shooter: 0 },
        { lane: 2, program: 21, shooter: 1 },
        { lane: 3, program: 11, shooter: null },
        { lane: 4, program: 71, shooter: 6 },
        { lane: 5, program: 31, shooter: 4, stopAfter: 5 },
        { lane: 6, program: 23, shooter: 3 },
    ] },
    { day: TODAY, start: '19:00', matchCode: 121, passes: [
        { lane: 1, program: 22, shooter: 0, matchCode: 122 },
        { lane: 1, program: 21, shooter: 0 },
        { lane: 2, program: 22, shooter: 3, matchCode: 122 },
        { lane: 3, program: 22, shooter: 7, matchCode: 122 },
        { lane: 4, program: 11, shooter: null, matchCode: 111 },
        { lane: 5, program: 71, shooter: 2, matchCode: 171 },
        { lane: 6, program: 31, shooter: 8, stopAfter: 4, matchCode: 131 },
        { lane: 2, program: 91, shooter: 5, matchCode: 191 },
        { lane: 5, program: 31, shooter: null, stopAfter: 8, matchCode: 131 },
        { lane: 4, program: 21, shooter: null, state: 'abandoned' },
        { lane: 6, program: 22, shooter: 1, state: 'active', matchCode: 122 },
    ] },
];
