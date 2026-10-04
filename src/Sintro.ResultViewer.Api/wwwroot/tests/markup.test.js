import { test } from 'node:test';
import assert from 'node:assert/strict';

import { TRANSLATIONS, translate } from '../core/i18n.js';
import { messageRow, shotRing, totalCell, laneTotalCell, tickerRun, clubCell } from '../core/markup.js';

const t = (key, params) => translate(TRANSLATIONS.de, key, params);

test('a message row spans every column it is given', () => {
    assert.match(messageRow('x', 5), /colspan="5"/);
});

test('a centre hit fills the whole ring', () => {
    assert.match(shotRing(0), /shot-ring is-centre/);
});

const series = (targetType, valuation, subtotal) => ({ targetType, valuation, subtotal, shots: [] });

test('a result on one scale is the bare number', () => {
    assert.equal(totalCell({ series: [series('A10', 10, 87), series('A10', 10, 9)] }), '96');
});

test('scales that do not add are shown side by side, labelled', () => {
    assert.equal(totalCell({ series: [series('A10', 10, 87), series('A100', 100, 173)] }),
        '<span class="total-part">A10 87</span> <span class="total-part">A100 173</span>');
});

test('a pass without shots shows a dash', () => {
    assert.match(totalCell({ series: [] }), /value-none/);
});

test('the live lane shows only the scale being shot', () => {
    assert.equal(laneTotalCell({ series: [series('A10', 10, 87), series('A100', 100, 173)] }), '173');
    assert.match(laneTotalCell({ series: [] }), /value-none/);
});

test('the ticker run is doubled so the loop is seamless', () => {
    const html = tickerRun([{ id: 1, lane: 2, startedAt: '2026-07-08T20:45:00+02:00' }], t);
    assert.equal((html.match(/class="ticker-run"/g) ?? []).length, 2);
});

test('all markup escapes what it is given', () => {
    assert.doesNotMatch(clubCell({ shooter: { club: { name: '<b>' } } }), /<b>/);
});
