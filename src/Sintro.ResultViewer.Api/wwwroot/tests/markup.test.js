import { test } from 'node:test';
import assert from 'node:assert/strict';

import { TRANSLATIONS, translate } from '../core/i18n.js';
import { messageRow, shotRing, totalCell, tickerRun, clubCell } from '../core/markup.js';

const t = (key, params) => translate(TRANSLATIONS.de, key, params);

test('a message row spans every column it is given', () => {
    assert.match(messageRow('x', 5), /colspan="5"/);
});

test('a centre hit fills the whole ring', () => {
    assert.match(shotRing(0), /shot-ring is-centre/);
});

test('a withheld total names the reason in its tooltip', () => {
    const html = totalCell({ total: null, totalUnavailable: 'mixedValuation' }, t);
    assert.match(html, /Wertung wechselt/);
});

test('the ticker run is doubled so the loop is seamless', () => {
    const html = tickerRun([{ id: 1, lane: 2, startedAt: '2026-07-08T20:45:00+02:00' }], t);
    assert.equal((html.match(/class="ticker-run"/g) ?? []).length, 2);
});

test('all markup escapes what it is given', () => {
    assert.doesNotMatch(clubCell({ shooter: { club: { name: '<b>' } } }), /<b>/);
});
