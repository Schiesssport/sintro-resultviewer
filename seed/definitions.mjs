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

// skill: the fine value (0..100) a shooter hits on average; spread: how much it varies.
// licence: six digits, zero-padded, the number printed on the SSV licence.
export const SHOOTERS = [
    { club: 0, first: 'Hans',    last: 'Muster',    licence: '100101', skill: 88, spread: 9 },
    { club: 0, first: 'Vreni',   last: 'Muster',    licence: '100102', skill: 92, spread: 6 },
    { club: 0, first: 'Ueli',    last: 'Beispiel',  licence: '100103', skill: 74, spread: 14 },
    { club: 1, first: 'Res',     last: 'Probst',    licence: '100201', skill: 84, spread: 10 },
    { club: 1, first: 'Marie',   last: 'Exemple',   licence: '100202', skill: 95, spread: 4 },
    { club: 1, first: 'Fritz',   last: 'Platzhalter', licence: '100203', skill: 68, spread: 16 },
    { club: 2, first: 'Jean',    last: 'Dupont',    licence: '100301', skill: 80, spread: 11 },
    { club: 2, first: 'Anna',    last: 'Modell',    licence: '100302', skill: 90, spread: 7 },
    { club: 2, first: 'Peter',   last: 'Vorlage',   licence: '100303', skill: 77, spread: 12 },
];

// A program is what the operator loads on a lane. Stages are shot in order; each stage is one
// ShotGroup. kind: 'sighting' (Probe, ShotType 0, never counts), 'EF' (Einzelfeuer, one shot at a
// time), 'SF' (Serienfeuer, a burst). target: 'A' | 'B' | 'S'; valuation: 4, 5, 10 or 100.
// Names follow what operators actually type: the stage notation, or a plain event name.
export const PROGRAMS = [
    { number: 31, name: 'A10-Probe', target: 'A', valuation: 10,
      stages: [{ kind: 'sighting', shots: 3 }] },
    { number: 32, name: 'A10-EF6-SF4', target: 'A', valuation: 10,
      stages: [{ kind: 'sighting', shots: 3 }, { kind: 'EF', shots: 6 }, { kind: 'SF', shots: 4 }] },
    { number: 36, name: 'A10-EF10', target: 'A', valuation: 10,
      stages: [{ kind: 'EF', shots: 10 }] },
    { number: 200, name: 'Obligatorisches Programm', target: 'A', valuation: 5,
      stages: [{ kind: 'sighting', shots: 3 }, { kind: 'EF', shots: 4 }, { kind: 'SF', shots: 3 }, { kind: 'SF', shots: 3 }] },
    { number: 141, name: 'Feldschiessen', target: 'B', valuation: 4,
      stages: [{ kind: 'sighting', shots: 3 }, { kind: 'EF', shots: 6 }, { kind: 'SF', shots: 3 }, { kind: 'SF', shots: 3 }, { kind: 'SF', shots: 6 }] },
    { number: 100, name: 'A100-EF8', target: 'A', valuation: 100,
      stages: [{ kind: 'sighting', shots: 2 }, { kind: 'EF', shots: 8 }] },
    { number: 42, name: 'Saustich', target: 'S', valuation: 10,
      stages: [{ kind: 'sighting', shots: 2 }, { kind: 'EF', shots: 5 }] },
    // Valuation changes mid-pass: the API must refuse a grand total for this one.
    { number: 922, name: 'Wertungswechsel 5 10', target: 'A', valuation: 5,
      stages: [{ kind: 'EF', shots: 3 }, { kind: 'EF', shots: 3, valuation: 10 }] },
];

// A session is one block of shooting on one day. Each pass names a lane, a program and a shooter
// (index into SHOOTERS, or null for an anonymous pass, which is the common case on a range).
// matchCode is the Stich number the operator enters in contest mode (0 outside events).
// state: 'finished' (default), 'active' (still on the lane, no end marker), 'abandoned' (loaded
// and left: only the marker row, no shots).
export const SESSIONS = [
    { day: '2026-07-01', start: '18:30', matchCode: 0, passes: [
        { lane: 1, program: 36, shooter: 0 },
        { lane: 2, program: 36, shooter: 1 },
        { lane: 3, program: 200, shooter: null },
        { lane: 4, program: 141, shooter: 6 },
        { lane: 5, program: 100, shooter: 4 },
    ] },
    { day: TODAY, start: '19:00', matchCode: 12, passes: [
        { lane: 1, program: 31, shooter: 0 },
        { lane: 1, program: 32, shooter: 0 },
        { lane: 2, program: 32, shooter: 3 },
        { lane: 3, program: 32, shooter: 7 },
        { lane: 4, program: 200, shooter: null },
        { lane: 5, program: 141, shooter: 2 },
        { lane: 6, program: 42, shooter: 8 },
        { lane: 2, program: 922, shooter: 5 },
        { lane: 4, program: 36, shooter: null, state: 'abandoned' },
        { lane: 6, program: 32, shooter: 1, state: 'active' },
    ] },
];
