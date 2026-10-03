import { test, describe } from 'node:test';
import assert from 'node:assert/strict';

import { parseList, buildRows, sortRows } from '../core/browse.js';

const shot = (number, value, fineValue, matchCode = 11) => ({ number, value, fineValue, matchCode });

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
        const rows = buildRows([program()], { groupBy: 'shot', detail: 'value', order: 'value' });
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
        const rows = buildRows([tied], { order: 'value', detail: 'fineValue' });
        assert.deepEqual(rows[0].breakdown[0].values, ['100', '97', '91']);
    });

    test('match codes: several in a pass are listed, none stays empty', () => {
        const mixed = program({ series: [{ index: 1, targetType: 'A10', subtotal: 19, shots: [shot(1, 9, 90, 31), shot(2, 10, 100, 32), shot(3, 10, 100, null)] }] });
        assert.equal(buildRows([mixed])[0].matchCode, '31, 32');
        assert.equal(buildRows([mixed], { groupBy: 'shot' })[2].matchCode, '');
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
