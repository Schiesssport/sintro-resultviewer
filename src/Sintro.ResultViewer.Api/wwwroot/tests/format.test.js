import { test, describe } from 'node:test';
import assert from 'node:assert/strict';

import {
    escapeHtml, formatTime, shooterLabel, resultTotals, resultText, activeTotal, matchesFilter, laneContext,
    shotGroups, programLabel, tickerEntry, localIsoDate,
} from '../core/format.js';
import { TRANSLATIONS, translate } from '../core/i18n.js';

const t = (key, params) => translate(TRANSLATIONS.de, key, params);

const program = (overrides = {}) => ({
    id: 2000,
    targetCode: 31,
    targetTitle: 'Obligatorisches Programm',
    lane: 6,
    startedAt: '2026-07-08T20:45:54+02:00',
    state: 'finished',
    shooter: null,
    contestShooterName: null,
    series: [
        { valuation: 5, targetType: 'A5', subtotal: 14, shots: [4, 3, 4, 3].map((value) => ({ value })) },
        { valuation: 5, targetType: 'A5', subtotal: 11, shots: [3, 5, 3].map((value) => ({ value })) },
        { valuation: 5, targetType: 'A5', subtotal: 6, shots: [4, 2, 0].map((value) => ({ value })) },
    ],
    sighting: [],
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

describe('localIsoDate', () => {
    test('uses the local day, not the UTC one', () => {
        assert.equal(localIsoDate(new Date(2026, 6, 8, 23, 30)), '2026-07-08');
    });
});

describe('formatTime', () => {
    test('shows the wall-clock time the API sent', () => {
        // Not the viewer's local zone: a tablet in the wrong timezone must not shift range times.
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
        // Identified by barcode, but the name lookup came back empty.
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
        // The common case; the pass must stay identifiable for manual processing.
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

const series = (targetType, valuation, subtotal) => ({ targetType, valuation, subtotal, shots: [] });

describe('resultTotals', () => {
    test('one scale gives one total, summed over its series', () => {
        assert.deepEqual(resultTotals(program()), [{ label: 'A5', value: 31 }]);
    });

    test('4er and 5er results are added together, as the sport does', () => {
        const mixed = program({ series: [series('A5', 5, 23), series('B4', 4, 56)] });
        assert.deepEqual(resultTotals(mixed), [{ label: 'A5/B4', value: 79 }]);
    });

    test('other scales stay apart, in the order first shot', () => {
        const mixed = program({ series: [series('A10', 10, 87), series('A100', 100, 173), series('A10', 10, 9)] });
        assert.deepEqual(resultTotals(mixed), [{ label: 'A10', value: 96 }, { label: 'A100', value: 173 }]);
    });

    test('a series whose scale the device never recorded is its own total', () => {
        const mixed = program({ series: [series('A10', 10, 87), series('??', null, 7)] });
        assert.deepEqual(resultTotals(mixed), [{ label: 'A10', value: 87 }, { label: '??', value: 7 }]);
    });

    test('no series, no totals', () => {
        assert.deepEqual(resultTotals(program({ series: [] })), []);
    });
});

describe('resultText', () => {
    test('the bare number for one scale', () => {
        assert.equal(resultText(program()), '31');
    });

    test('labelled sums when scales do not add', () => {
        assert.equal(resultText(program({ series: [series('A10', 10, 87), series('A100', 100, 173)] })), 'A10 87 · A100 173');
    });

    test('empty without shots', () => {
        assert.equal(resultText(program({ series: [] })), '');
    });
});

describe('activeTotal', () => {
    test('is the sum of the scale being shot right now, the last series', () => {
        assert.equal(activeTotal(program({ series: [series('A10', 10, 87), series('A100', 100, 173)] })), 173);
        assert.equal(activeTotal(program({ series: [series('A100', 100, 173), series('A10', 10, 87), series('A10', 10, 9)] })), 96);
    });

    test('is null before the first shot', () => {
        assert.equal(activeTotal(program({ series: [] })), null);
    });
});

describe('laneContext', () => {
    test('club and program, separated by a middle dot', () => {
        assert.equal(
            laneContext(program({ shooter: { club: { name: 'SG Muster' } }, name: 'Obligatorisches Programm' })),
            'SG Muster · Obligatorisches Programm');
    });

    test('an anonymous pass shows the program alone, with no dangling separator', () => {
        assert.equal(laneContext(program({ shooter: null, targetTitle: 'A10-Probe' })), 'A10-Probe');
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

describe('shotGroups', () => {
    const withSeries = (series) => program({ series });

    test('one group per series, carrying code, shots and best fine value', () => {
        const groups = shotGroups(withSeries([
            {
                valuation: 10, targetType: 'A10', subtotal: 19,
                shots: [{ value: 9, fineValue: 88, innerTen: false }, { value: 10, fineValue: 96, innerTen: true }],
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
            { targetType: 'A10', subtotal: 9, shots: [{ value: 9 }] },
            { targetType: 'A10', subtotal: 8, shots: [{ value: 8 }] },
        ]));

        assert.deepEqual(groups.map((group) => group.subtotal), [9, 8]);
    });

    test('a B target keeps its own code', () => {
        const groups = shotGroups(withSeries([
            { valuation: 4, targetType: 'B4', subtotal: 4, shots: [{ value: 4 }] },
        ]));
        assert.equal(groups[0].code, 'B4');
    });

    test('sighting shots are excluded — they are not the result', () => {
        const groups = shotGroups(program({
            series: [{ targetType: 'A10', subtotal: 9, shots: [{ value: 9 }] }],
            sighting: { targetType: 'A5', subtotal: 5, shots: [{ value: 5 }] },
        }));

        assert.equal(groups.length, 1);
        assert.equal(groups[0].code, 'A10');
    });

    test('a program with no series yields no groups', () => {
        assert.deepEqual(shotGroups(program({ series: [] })), []);
        assert.deepEqual(shotGroups(program({ series: undefined })), []);
    });

    test('carries the last shot\'s fine value, null when the series has none', () => {
        const groups = shotGroups(withSeries([
            { targetType: 'A10', subtotal: 19, shots: [{ value: 9, fineValue: 94 }, { value: 10, fineValue: 102 }] },
            { targetType: 'A10', subtotal: 0, shots: [] },
        ]));
        assert.equal(groups[0].lastFineValue, 102);
        assert.equal(groups[1].lastFineValue, null);
    });

    test('a 100er series drops its fine values, which only repeat the ring value', () => {
        const groups = shotGroups(withSeries([
            { valuation: 100, targetType: 'A100', subtotal: 97, shots: [{ value: 97, fineValue: 97 }] },
            { valuation: 10, targetType: 'A10', subtotal: 9, shots: [{ value: 9, fineValue: 94 }] },
        ]));
        assert.equal(groups[0].bestFineValue, null);
        assert.equal(groups[0].lastFineValue, null);
        assert.equal(groups[1].bestFineValue, 94);
        assert.equal(groups[1].lastFineValue, 94);
    });

    test('misses never win the best fine value, and only misses leave it null', () => {
        const groups = shotGroups(withSeries([
            { targetType: 'A10', subtotal: 7, shots: [{ value: 0, fineValue: 0 }, { value: 7, fineValue: 61 }] },
            { targetType: 'A10', subtotal: 0, shots: [{ value: 0, fineValue: 0 }] },
        ]));
        assert.equal(groups[0].bestFineValue, 61);
        assert.equal(groups[1].bestFineValue, null);
    });
});

describe('shotGroups shot entries', () => {
    const shotsOf = (shots) => shotGroups(program({
        series: [{ targetType: 'A10', subtotal: 19, shots }],
    }))[0].shots;

    test('each shot carries its text and the raw sector for the ring', () => {
        assert.deepEqual(shotsOf([
            { value: 9, innerTen: false, hitSector: 3 },
            { value: 10, innerTen: true, hitSector: 0 },
            { value: 8, innerTen: false, hitSector: null },
            { value: 0, innerTen: false, hitSector: 6 },
        ]), [
            { text: '9', sector: 3, innerTen: false },
            { text: '10', sector: 0, innerTen: true },
            { text: '8', sector: null, innerTen: false },
            { text: '0', sector: 6, innerTen: false },
        ]);
    });

    test('an inner ten shows its plain ring value, never a glyph', () => {
        // On 5er and 4er targets a centre hit scores 5 or 4; a glyph in place of that digit read as a zero.
        const texts = shotsOf([
            { value: 10, innerTen: true }, { value: 5, innerTen: true }, { value: 4, innerTen: true },
        ]).map((shot) => shot.text);

        assert.deepEqual(texts, ['10', '5', '4']);
    });

    test('a missing hitSector becomes null rather than undefined', () => {
        // The renderer decides "no ring" on === null, so undefined must not leak through.
        const groups = shotGroups(program({
            series: [{ targetType: 'A10', subtotal: 9, shots: [{ value: 9 }] }],
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
            programLabel({ targetTitle: 'Feldschiessen', startedAt: null, lane: null }),
            'Feldschiessen');
    });

    test('keeps the line when the time is unusable', () => {
        assert.equal(
            programLabel({ targetTitle: 'Feldschiessen', startedAt: 'nonsense', lane: 3 }),
            'Feldschiessen (L3)');
    });

    test('keeps the time when the line is missing', () => {
        assert.equal(
            programLabel({ targetTitle: 'Feldschiessen', startedAt: '2026-07-08T09:05:00+02:00' }),
            'Feldschiessen (09:05)');
    });

    test('line 0 is a real line, not a missing one', () => {
        assert.match(programLabel({ targetTitle: 'X', lane: 0 }), /L0/);
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

    test('a pass without shots shows a dash rather than a wrong number', () => {
        assert.match(tickerEntry(program({ shooter: shooter(), series: [] }), t), /Hans Muster: –/);
    });

    test('scales that do not add are both named', () => {
        const mixed = program({ shooter: shooter(), series: [series('A10', 10, 87), series('A100', 100, 173)] });
        assert.match(tickerEntry(mixed, t), /Hans Muster: A10 87 · A100 173/);
    });

    test('drops context that is not there instead of leaving empty brackets', () => {
        assert.equal(
            tickerEntry({ targetTitle: '', startedAt: null, lane: null, series: [series('A10', 10, 7)], shooter: shooter() }, t),
            'Hans Muster: 7');
    });
});
