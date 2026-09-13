import { test, describe } from 'node:test';
import assert from 'node:assert/strict';

import {
    escapeHtml, formatTime, shooterLabel, totalDisplay, matchesFilter, laneContext,
    shotText, shotGroups, programLabel, tickerEntry,
} from '../core/format.js';
import { TRANSLATIONS, translate } from '../core/i18n.js';

const t = (key, params) => translate(TRANSLATIONS.de, key, params);

const program = (overrides = {}) => ({
    id: 2000,
    number: 31,
    name: 'Obligatorisches Programm',
    lane: 6,
    startedAt: '2026-07-08T20:45:54+02:00',
    state: 'finished',
    shooter: null,
    contestShooterName: null,
    total: { value: 31, shotCount: 10, valuation: 5 },
    totalUnavailable: null,
    shotValuesText: '4 3 4 3 3 5 3 4 2 0',
    series: [],
    sighting: null,
    ...overrides,
});

const shooter = (overrides = {}) => ({
    license: '012345',
    firstName: 'Hans',
    lastName: 'Muster',
    club: { id: 1, number: '1.02.1.04.133', name: 'Feldschützen Musterdorf' },
    duplicateLicense: false,
    ...overrides,
});

describe('escapeHtml', () => {
    test('neutralises markup', () => {
        assert.equal(escapeHtml('<script>alert("x")</script>'),
            '&lt;script&gt;alert(&quot;x&quot;)&lt;/script&gt;');
    });

    test('handles null and undefined', () => {
        assert.equal(escapeHtml(null), '');
        assert.equal(escapeHtml(undefined), '');
    });

    test('escapes apostrophes, which appear in Swiss club names', () => {
        assert.equal(escapeHtml("d'Arc"), 'd&#39;Arc');
    });
});

describe('formatTime', () => {
    test('shows the wall-clock time the API sent', () => {
        // Not the viewer's local zone: a tablet set to the wrong timezone must
        // not shift the shooting times shown on the range.
        assert.equal(formatTime('2026-07-08T20:45:54+02:00'), '20:45');
    });

    test('is unaffected by the offset in the string', () => {
        assert.equal(formatTime('2026-01-15T09:05:00+01:00'), '09:05');
    });

    test('returns empty for missing or unparseable input', () => {
        assert.equal(formatTime(null), '');
        assert.equal(formatTime(''), '');
        assert.equal(formatTime('not a date'), '');
    });
});

describe('shooterLabel', () => {
    test('prefers the full name', () => {
        const label = shooterLabel(program({ shooter: shooter() }), t);
        assert.equal(label.text, 'Hans Muster');
        assert.equal(label.fallback, false);
    });

    test('falls back to the licence when the name is blank', () => {
        // The shooter was identified by barcode but the name lookup came back empty.
        const label = shooterLabel(program({ shooter: shooter({ firstName: '', lastName: '' }) }), t);
        assert.equal(label.text, 'Lizenz 012345');
        assert.equal(label.fallback, true);
    });

    test('falls back to the device free-text field', () => {
        const label = shooterLabel(program({ contestShooterName: 'Gast Muster' }), t);
        assert.equal(label.text, 'Gast Muster');
        assert.equal(label.fallback, false);
    });

    test('falls back to lane and time when nothing identifies the shooter', () => {
        // The 85% case. The pass must stay identifiable for manual processing.
        const label = shooterLabel(program(), t);
        assert.equal(label.text, 'Linie 6 · 20:45');
        assert.equal(label.fallback, true);
    });

    test('prefers the licence over the free-text field', () => {
        const label = shooterLabel(program({
            shooter: shooter({ firstName: '', lastName: '' }),
            contestShooterName: 'Gast Muster',
        }), t);
        assert.equal(label.text, 'Lizenz 012345');
    });

    test('French produces French fallbacks', () => {
        const fr = (key, params) => translate(TRANSLATIONS.fr, key, params);
        assert.equal(shooterLabel(program(), fr).text, 'Ligne 6 · 20:45');
    });
});

describe('totalDisplay', () => {
    test('reports the total and its valuation', () => {
        const display = totalDisplay(program());
        assert.equal(display.hasTotal, true);
        assert.equal(display.value, 31);
        assert.equal(display.valuation, 5);
    });

    test('names mixed valuation as the reason no total exists', () => {
        const display = totalDisplay(program({ total: null, totalUnavailable: 'mixedValuation' }));
        assert.equal(display.hasTotal, false);
        assert.equal(display.reasonKey, 'total.mixedValuation');
        assert.equal(t(display.reasonKey), 'Wertung wechselt – kein Gesamttotal');
    });

    test('names unknown valuation', () => {
        const display = totalDisplay(program({ total: null, totalUnavailable: 'unknownValuation' }));
        assert.equal(display.reasonKey, 'total.unknownValuation');
    });

    test('a pass with no shots has no total and no reason', () => {
        const display = totalDisplay(program({ total: null, totalUnavailable: null }));
        assert.equal(display.hasTotal, false);
        assert.equal(display.reasonKey, null);
    });
});

describe('laneContext', () => {
    test('club and program, separated by a middle dot', () => {
        assert.equal(
            laneContext(program({ shooter: { club: { name: 'SG Muster' } }, name: 'Obligatorisches Programm' })),
            'SG Muster · Obligatorisches Programm');
    });

    test('an anonymous pass shows the program alone, with no dangling separator', () => {
        assert.equal(laneContext(program({ shooter: null, name: 'A10-Probe' })), 'A10-Probe');
        assert.equal(laneContext(null), '');
    });
});

describe('matchesFilter', () => {
    const row = program({ shooter: shooter() });

    test('an empty query matches everything', () => {
        assert.equal(matchesFilter(row, ''), true);
        assert.equal(matchesFilter(row, '   '), true);
        assert.equal(matchesFilter(row, null), true);
    });

    test('matches program name, licence and club', () => {
        assert.equal(matchesFilter(row, 'obligatorisches', 'Hans Muster'), true);
        assert.equal(matchesFilter(row, '012345', 'Hans Muster'), true);
        assert.equal(matchesFilter(row, 'musterdorf', 'Hans Muster'), true);
    });

    test('matches the shooter label even for an anonymous pass', () => {
        assert.equal(matchesFilter(program(), 'linie 6', 'Linie 6 · 20:45'), true);
    });

    test('all terms must match, not just one', () => {
        assert.equal(matchesFilter(row, 'obligatorisches muster', 'Hans Muster'), true);
        assert.equal(matchesFilter(row, 'obligatorisches steiner', 'Hans Muster'), false);
    });

    test('matches on the shot values', () => {
        // Terms are matched independently, so every digit of the string is found.
        assert.equal(matchesFilter(row, '3 4 2 0', 'Hans Muster'), true);
        assert.equal(matchesFilter(row, '4', 'Hans Muster'), true);
        assert.equal(matchesFilter(row, '77', 'Hans Muster'), false);
    });

    test('is case-insensitive', () => {
        assert.equal(matchesFilter(row, 'OBLIGATORISCHES', 'Hans Muster'), true);
    });
});


describe('shotText', () => {
    test('a plain shot is just its ring value', () => {
        assert.equal(shotText({ value: 7, mouche: false }), '7');
        assert.equal(shotText({ value: 10, mouche: false }), '10');
        assert.equal(shotText({ value: 0, mouche: false }), '0');
    });

    test('a mouche shows its plain ring value, never a glyph', () => {
        assert.equal(shotText({ value: 10, mouche: true }), '10');
        // On 5er and 4er targets a centre hit scores 5 or 4; a glyph in place of that
        // single digit read as a zero on the range.
        assert.equal(shotText({ value: 5, mouche: true }), '5');
        assert.equal(shotText({ value: 4, mouche: true }), '4');
    });
});

describe('shotGroups', () => {
    const withSeries = (series) => program({ series });

    test('one group per series, carrying code, shots and best fine value', () => {
        const groups = shotGroups(withSeries([
            {
                index: 1, valuation: 10, targetCode: 'A10', subtotal: 19, bestFineValue: 96,
                shots: [{ value: 9, mouche: false }, { value: 10, mouche: true }],
            },
        ]));

        assert.equal(groups.length, 1);
        assert.equal(groups[0].code, 'A10');
        assert.deepEqual(groups[0].shots.map((shot) => shot.text), ['9', '10']);
        assert.equal(groups[0].bestFineValue, 96);
        assert.equal(groups[0].subtotal, 19);
    });

    test('keeps series order so the groups read as they were shot', () => {
        const groups = shotGroups(withSeries([
            { index: 1, targetCode: 'A10', subtotal: 9, shots: [{ value: 9 }] },
            { index: 2, targetCode: 'A10', subtotal: 8, shots: [{ value: 8 }] },
        ]));

        assert.deepEqual(groups.map((group) => group.index), [1, 2]);
    });

    test('a B target keeps its own code', () => {
        const groups = shotGroups(withSeries([
            { index: 1, valuation: 4, targetCode: 'B4', subtotal: 4, shots: [{ value: 4 }] },
        ]));
        assert.equal(groups[0].code, 'B4');
    });

    test('sighting shots are excluded — they are not the result', () => {
        const groups = shotGroups(program({
            series: [{ index: 1, targetCode: 'A10', subtotal: 9, shots: [{ value: 9 }] }],
            sighting: { index: 0, targetCode: 'A5', subtotal: 5, shots: [{ value: 5 }] },
        }));

        assert.equal(groups.length, 1);
        assert.equal(groups[0].code, 'A10');
    });

    test('a program with no series yields no groups', () => {
        assert.deepEqual(shotGroups(program({ series: [] })), []);
        assert.deepEqual(shotGroups(program({ series: undefined })), []);
    });

    test('a missing best fine value stays null rather than rendering as 0', () => {
        const groups = shotGroups(withSeries([
            { index: 1, targetCode: 'A10', subtotal: 0, bestFineValue: null, shots: [{ value: 0 }] },
        ]));
        assert.equal(groups[0].bestFineValue, null);
    });
});


describe('shotGroups shot entries', () => {
    test('each shot carries its text and the raw sector for the ring', () => {
        const groups = shotGroups(program({
            series: [{
                index: 1, targetCode: 'A10', subtotal: 19, bestFineValue: 96,
                shots: [
                    { value: 9, mouche: false, hitSector: 3 },
                    { value: 10, mouche: true, hitSector: 0 },
                    { value: 8, mouche: false, hitSector: null },
                ],
            }],
        }));

        assert.deepEqual(groups[0].shots, [
            { text: '9', sector: 3, mouche: false },
            { text: '10', sector: 0, mouche: true },
            { text: '8', sector: null, mouche: false },
        ]);
    });

    test('a missing hitSector becomes null rather than undefined', () => {
        // The renderer decides "no ring" on === null, so undefined must not leak through.
        const groups = shotGroups(program({
            series: [{ index: 1, targetCode: 'A10', subtotal: 9, shots: [{ value: 9 }] }],
        }));

        assert.equal(groups[0].shots[0].sector, null);
    });
});

describe('programLabel', () => {
    test('folds time and line into the program name', () => {
        assert.equal(programLabel(program()), 'Obligatorisches Programm (20:45/L6)');
    });

    test('drops the empty parenthesis when there is no context at all', () => {
        assert.equal(
            programLabel({ name: 'Feldschiessen', startedAt: null, lane: null }),
            'Feldschiessen');
    });

    test('keeps the line when the time is unusable', () => {
        assert.equal(
            programLabel({ name: 'Feldschiessen', startedAt: 'nonsense', lane: 3 }),
            'Feldschiessen (L3)');
    });

    test('keeps the time when the line is missing', () => {
        assert.equal(
            programLabel({ name: 'Feldschiessen', startedAt: '2026-07-08T09:05:00+02:00' }),
            'Feldschiessen (09:05)');
    });

    test('line 0 is a real line, not a missing one', () => {
        assert.match(programLabel({ name: 'X', lane: 0 }), /L0/);
    });
});

describe('tickerEntry', () => {
    test('is a compact one-liner: name, total, program, time and line', () => {
        assert.equal(
            tickerEntry(program({ shooter: shooter() }), t),
            'Hans Muster: 31 (Obligatorisches Programm, 20:45/L6)');
    });

    test('an anonymous pass falls back to the line and time as its name', () => {
        assert.equal(
            tickerEntry(program(), t),
            'Linie 6 · 20:45: 31 (Obligatorisches Programm, 20:45/L6)');
    });

    test('a pass with no usable total shows a dash rather than a wrong number', () => {
        const entry = tickerEntry(
            program({ shooter: shooter(), total: null, totalUnavailable: 'MixedValuation' }), t);
        assert.match(entry, /Hans Muster: –/);
    });

    test('drops context that is not there instead of leaving empty brackets', () => {
        assert.equal(
            tickerEntry({ name: '', startedAt: null, lane: null, total: { value: 7 }, shooter: shooter() }, t),
            'Hans Muster: 7');
    });
});
