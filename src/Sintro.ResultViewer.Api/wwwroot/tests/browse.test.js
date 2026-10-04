import { test, describe } from 'node:test';
import assert from 'node:assert/strict';

import { parseList, buildRows, sortRows, formatDateTime, filterShooters, summarize, filterByTotal, parseBound, exportText, exportFileName, EXPORT_COLUMNS, DEFAULT_EXPORT_COLUMNS } from '../core/browse.js';

const shot = (number, value, fineValue, matchCode = 11) =>
    ({ number, value, fineValue, matchCode, at: `2026-07-08T20:46:0${number}+02:00` });

const program = (overrides = {}) => ({
    id: 2000,
    targetCode: 41,
    targetProgram: 'Ehrengaben',
    startedAt: '2026-07-08T20:45:54+02:00',
    shooter: {
        license: '012345', firstName: 'Hans', lastName: 'Muster',
        club: { id: 1, number: '1.02.1.04.133', name: 'Feldschützen Musterdorf' },
    },
    total: { value: 27, valuation: 10 },
    series: [
        { index: 1, valuation: 10, targetType: 'A10', subtotal: 17, shots: [shot(1, 8, 84), shot(2, 9, 93)] },
        { index: 2, valuation: 10, targetType: 'A10', subtotal: 10, shots: [shot(3, 10, 102)] },
    ],
    ...overrides,
});

describe('parseList', () => {
    test('splits on commas and drops blanks', () => {
        assert.deepEqual(parseList(' 41, 44,,'), ['41', '44']);
        assert.deepEqual(parseList(''), []);
        assert.deepEqual(parseList(null), []);
    });

    test('numeric lists lose leading zeros so licences agree with the API', () => {
        assert.deepEqual(parseList('012345, 0', { numeric: true }), ['12345', '0']);
    });
});

describe('buildRows', () => {
    test('program mode: one row, program total, series in shot order', () => {
        const rows = buildRows([program()]);

        assert.equal(rows.length, 1);
        assert.equal(rows[0].total, 27);
        assert.equal(rows[0].license, '012345');
        assert.equal(rows[0].lastName, 'Muster');
        assert.equal(rows[0].club, 'Feldschützen Musterdorf');
        assert.equal(rows[0].targetCode, 41);
        assert.equal(rows[0].matchCode, '11');
        assert.deepEqual(rows[0].breakdown, [
            { code: 'A10', values: ['8', '9'] },
            { code: 'A10', values: ['10'] },
        ]);
    });

    test('a withheld program total stays null', () => {
        assert.equal(buildRows([program({ total: null })])[0].total, null);
    });

    test('series mode: one row per series with its subtotal', () => {
        const rows = buildRows([program()], { groupBy: 'series' });

        assert.deepEqual(rows.map((row) => row.total), [17, 10]);
        assert.deepEqual(rows[0].breakdown, [{ code: 'A10', values: ['8', '9'] }]);
    });

    test('shot mode: one row per shot, ring value as total, fine value as breakdown', () => {
        const rows = buildRows([program()], { groupBy: 'shot', detail: 'value', shotOrder: 'value' });
        assert.deepEqual(rows.map((row) => row.matchCode), ['11', '11', '11']);

        assert.deepEqual(rows.map((row) => row.total), [8, 9, 10]);
        assert.deepEqual(rows.map((row) => row.breakdown[0].values[0]), ['84', '93', '102']);
    });

    test('fine value detail shows tenths instead of rings', () => {
        const rows = buildRows([program()], { detail: 'fineValue' });
        assert.deepEqual(rows[0].breakdown[0].values, ['84', '93']);
    });

    test('value order lists the best shot first, fine value breaking ties', () => {
        const tied = program({
            series: [{ index: 1, targetType: 'A10', subtotal: 27, shots: [shot(1, 9, 91), shot(2, 10, 100), shot(3, 9, 97)] }],
        });
        const rows = buildRows([tied], { shotOrder: 'value', detail: 'fineValue' });
        assert.deepEqual(rows[0].breakdown[0].values, ['100', '97', '91']);
    });

    test('match codes: several in a pass are listed, none stays empty', () => {
        const mixed = program({ series: [{ index: 1, targetType: 'A10', subtotal: 19, shots: [shot(1, 9, 90, 31), shot(2, 10, 100, 32), shot(3, 10, 100, null)] }] });
        assert.equal(buildRows([mixed])[0].matchCode, '31, 32');
        assert.equal(buildRows([mixed], { groupBy: 'shot' })[2].matchCode, '');
    });

    test('a row is timed by its last shot, the program falling back to its start', () => {
        assert.equal(buildRows([program()])[0].at, '2026-07-08T20:46:03+02:00');
        assert.deepEqual(buildRows([program()], { groupBy: 'series' }).map((row) => row.at),
            ['2026-07-08T20:46:02+02:00', '2026-07-08T20:46:03+02:00']);
        assert.equal(buildRows([program()], { groupBy: 'shot' })[0].at, '2026-07-08T20:46:01+02:00');
        assert.equal(buildRows([program({ series: [] })])[0].at, '2026-07-08T20:45:54+02:00');
    });

    test('best series first orders the breakdown by subtotal, shots untouched', () => {
        const better = program({ series: [
            { index: 1, targetType: 'A10', subtotal: 10, shots: [shot(1, 10, 100)] },
            { index: 2, targetType: 'A10', subtotal: 17, shots: [shot(2, 8, 84), shot(3, 9, 93)] },
        ] });
        const rows = buildRows([better], { seriesOrder: 'value' });
        assert.deepEqual(rows[0].breakdown.map((group) => group.values), [['8', '9'], ['10']]);
    });

    test('anonymous passes yield empty shooter columns', () => {
        const [row] = buildRows([program({ shooter: null })]);
        assert.deepEqual([row.license, row.lastName, row.firstName, row.club], ['', '', '', '']);
    });

    test('row keys are unique across modes', () => {
        for (const groupBy of ['program', 'series', 'shot']) {
            const keys = buildRows([program(), program({ id: 2001 })], { groupBy }).map((row) => row.key);
            assert.equal(new Set(keys).size, keys.length, groupBy);
        }
    });
});

describe('sortRows', () => {
    const rows = [
        { license: '2', lastName: 'Zyx', firstName: 'Anna', club: 'B', total: 5 },
        { license: '1', lastName: 'Abc', firstName: 'Bert', club: 'C', total: null },
        { license: '3', lastName: 'Abc', firstName: 'Anna', club: 'A', total: 9 },
    ];

    test('sorts by name, last name first then first name', () => {
        assert.deepEqual(sortRows(rows, 'name').map((row) => row.license), ['3', '1', '2']);
    });

    test('sorts by total with missing totals last in both directions', () => {
        assert.deepEqual(sortRows(rows, 'total', 'asc').map((row) => row.license), ['2', '3', '1']);
        assert.deepEqual(sortRows(rows, 'total', 'desc').map((row) => row.license), ['3', '2', '1']);
    });

    test('descending reverses text columns', () => {
        assert.deepEqual(sortRows(rows, 'club', 'desc').map((row) => row.club), ['C', 'B', 'A']);
    });

    test('an unknown column leaves the order alone', () => {
        assert.deepEqual(sortRows(rows, 'nope').map((row) => row.license), ['2', '1', '3']);
    });
});

describe('formatDateTime', () => {
    test('shows day, month and the time of day from the ISO text', () => {
        assert.equal(formatDateTime('2026-09-19T17:52:08.55+02:00'), '19.09. 17:52:08');
    });

    test('is empty for nothing or nonsense', () => {
        assert.equal(formatDateTime(null), '');
        assert.equal(formatDateTime('later'), '');
    });
});

describe('filterShooters', () => {
    const shooters = [
        { license: '000002', firstName: 'Anna', lastName: 'Zyx', club: { name: 'SG Musterdorf' } },
        { license: '000001', firstName: 'Bert', lastName: 'Abc', club: { name: 'FS Beispielingen' } },
        { license: null, firstName: 'Ohne', lastName: 'Lizenz', club: null },
    ];

    test('no query lists everyone with a licence, by name', () => {
        assert.deepEqual(filterShooters(shooters, '').map((shooter) => shooter.lastName), ['Abc', 'Zyx']);
    });

    test('every term must match name, licence or club', () => {
        assert.deepEqual(filterShooters(shooters, 'muster').map((shooter) => shooter.license), ['000002']);
        assert.deepEqual(filterShooters(shooters, '0001').map((shooter) => shooter.license), ['000001']);
        assert.deepEqual(filterShooters(shooters, 'abc anna'), []);
        assert.deepEqual(filterShooters(shooters, 'ZYX  anna').length, 1);
    });
});

describe('summarize', () => {
    test('counts programs, series and counting shots', () => {
        assert.deepEqual(summarize([program(), program({ id: 2001, series: [] })]), { programs: 2, series: 2, shots: 3 });
        assert.deepEqual(summarize([]), { programs: 0, series: 0, shots: 0 });
    });
});

describe('filterByTotal', () => {
    const rows = [{ total: 5 }, { total: 9 }, { total: null }, { total: 12 }];

    test('no bounds keep everything, including rows without a total', () => {
        assert.equal(filterByTotal(rows, {}).length, 4);
    });

    test('a lower bound alone is greater or equal, an upper bound alone less or equal', () => {
        assert.deepEqual(filterByTotal(rows, { min: 9 }).map((row) => row.total), [9, 12]);
        assert.deepEqual(filterByTotal(rows, { max: 9 }).map((row) => row.total), [5, 9]);
    });

    test('both bounds are an inclusive range, and a missing total never qualifies', () => {
        assert.deepEqual(filterByTotal(rows, { min: 5, max: 9 }).map((row) => row.total), [5, 9]);
    });

    test('parseBound reads a number and treats blanks or nonsense as open', () => {
        assert.equal(parseBound(' 90 '), 90);
        assert.equal(parseBound(''), null);
        assert.equal(parseBound('abc'), null);
    });
});

describe('exportText', () => {
    const rows = buildRows([program()]);
    const headers = { shooter: 'Schütze', total: 'Resultat', shots: 'Schüsse', series: 'Passe', shot: 'Schuss', time: 'Zeit' };

    test('writes a header row and the chosen columns, tab-separated', () => {
        assert.equal(exportText(rows, ['shooter', 'total'], headers, '\t'), 'Schütze\tResultat\r\nHans Muster\t27'.replace('Hans Muster', 'Muster Hans'));
    });

    test('flattens the breakdown without target codes and quotes fields holding the delimiter', () => {
        const text = exportText(rows, ['shots'], headers, ' ');
        assert.equal(text, 'Schüsse\r\n"8 9 10"');
    });

    test('a withheld total exports as an empty field and quotes are doubled', () => {
        const odd = buildRows([program({ total: null, shooter: { license: '1', firstName: 'A "B"', lastName: 'C', club: null } })]);
        assert.equal(exportText(odd, ['shooter', 'total'], headers, ';'), 'Schütze;Resultat\r\n"C A ""B""";');
    });

    test('one column per series pads shorter rows', () => {
        const two = [program(), program({ id: 2001, series: [{ index: 1, targetType: 'A10', subtotal: 9, shots: [shot(1, 9, 90)] }] })];
        assert.equal(exportText(buildRows(two), ['shotsBySeries'], headers, ';'),
            'Passe 1;Passe 2\r\n8 9;10\r\n9;');
    });

    test('one column per shot flattens the series', () => {
        assert.equal(exportText(rows, ['total', 'shotsByShot'], headers, ';'),
            'Resultat;Schuss 1;Schuss 2;Schuss 3\r\n27;8;9;10');
    });

    test('the shot layouts combine side by side', () => {
        assert.equal(exportText(rows, ['shots', 'shotsBySeries'], headers, ';'), 'Schüsse;Passe 1;Passe 2\r\n8 9 10;8 9;10');
    });

    test('defaults export shooter and result only', () => {
        assert.deepEqual(DEFAULT_EXPORT_COLUMNS, ['shooter', 'total']);
        assert.ok(DEFAULT_EXPORT_COLUMNS.every((column) => EXPORT_COLUMNS.includes(column)));
    });
});

describe('exportFileName', () => {
    test('stamps the time and names the match and target code filters', () => {
        const now = new Date(2026, 9, 4, 13, 5, 9);
        assert.equal(exportFileName(now, ['32'], ['43']), '2026-10-04-13-05-09_32_43.csv');
        assert.equal(exportFileName(now, ['11', '21'], []), '2026-10-04-13-05-09_11-21_alle.csv');
    });
});
