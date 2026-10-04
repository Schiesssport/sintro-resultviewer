import { test, describe } from 'node:test';
import assert from 'node:assert/strict';

import {
    tickerConfig, tickerDurationSeconds,
    tickerContentKey, tickerQuery, normaliseTickerSettings,
} from '../core/ticker.js';

const items = (count) => Array.from({ length: count }, (_, index) => ({ id: index + 1 }));

describe('tickerConfig', () => {
    test('defaults to the documented values', () => {
        assert.deepEqual(tickerConfig(''), { seconds: 20, results: 50, skip: 0, hidden: false });
    });

    test('reads every parameter', () => {
        assert.deepEqual(tickerConfig('?tickerSeconds=8&resultCount=40&tickerSkip=6&ticker=off'),
            { seconds: 8, results: 40, skip: 6, hidden: true });
    });

    test('nonsense falls back to the defaults', () => {
        assert.deepEqual(tickerConfig('?tickerSeconds=abc&resultCount=&tickerSkip=x&ticker=on'),
            { seconds: 20, results: 50, skip: 0, hidden: false });
    });

    test('values are clamped to a readable range', () => {
        assert.equal(tickerConfig('?tickerSeconds=0').seconds, 1);
        assert.equal(tickerConfig('?tickerSeconds=9999').seconds, 120);
        assert.equal(tickerConfig('?resultCount=0').results, 1);
        assert.equal(tickerConfig('?resultCount=99999').results, 500);
        assert.equal(tickerConfig('?tickerSkip=-3').skip, 0);
    });
});

describe('normaliseTickerSettings', () => {
    test('a cleared box keeps the default rather than becoming zero', () => {
        // Number('') is 0; the URL must not end up saying resultCount=0.
        const settings = normaliseTickerSettings({ seconds: '', results: '', skip: '' });
        assert.deepEqual(settings, { seconds: 20, results: 50, skip: 0, hidden: false });
    });

    test('whole numbers only', () => {
        assert.equal(normaliseTickerSettings({ seconds: '7.9' }).seconds, 7);
    });
});

describe('tickerDurationSeconds', () => {
    const base = { containerWidth: 1200, contentWidth: 6000, entryCount: 20, secondsVisible: 6 };

    test('derives the pass duration from the reading time per entry', () => {
        // An average 300px entry travels 1200 + 300 = 1500px in 6s, so 6000px of content takes 24s.
        assert.equal(tickerDurationSeconds(base), 24);
    });

    test('a longer reading time scrolls proportionally slower', () => {
        const slow = tickerDurationSeconds({ ...base, secondsVisible: 12 });
        assert.equal(slow, tickerDurationSeconds(base) * 2);
    });

    test('a wider screen keeps the same reading time by scrolling faster', () => {
        const wide = tickerDurationSeconds({ ...base, containerWidth: 2400 });
        assert.ok(wide < tickerDurationSeconds(base));
    });

    test('more content takes longer at the same speed', () => {
        const long = tickerDurationSeconds({ ...base, contentWidth: 12000, entryCount: 40 });
        assert.equal(long, tickerDurationSeconds(base) * 2);
    });

    test('returns zero rather than NaN or Infinity when nothing is measurable', () => {
        assert.equal(tickerDurationSeconds({ ...base, contentWidth: 0 }), 0);
        assert.equal(tickerDurationSeconds({ ...base, contentWidth: NaN }), 0);
        assert.ok(Number.isFinite(tickerDurationSeconds({ ...base, containerWidth: NaN })));
        assert.ok(Number.isFinite(tickerDurationSeconds({ ...base, secondsVisible: 0 })));
        assert.ok(Number.isFinite(tickerDurationSeconds({ ...base, entryCount: 0 })));
    });
});

describe('tickerContentKey', () => {
    test('is stable while the content is unchanged', () => {
        // The guard that stops the marquee restarting on every incoming shot.
        assert.equal(tickerContentKey(items(5)), tickerContentKey(items(5)));
    });

    test('changes when an entry is added, removed or reordered', () => {
        const five = tickerContentKey(items(5));
        assert.notEqual(five, tickerContentKey(items(6)));
        assert.notEqual(five, tickerContentKey(items(4)));
        assert.notEqual(five, tickerContentKey([...items(5)].reverse()));
    });

    test('handles empty and missing input', () => {
        assert.equal(tickerContentKey([]), '');
        assert.equal(tickerContentKey(undefined), '');
    });
});

describe('tickerQuery', () => {
    test('round-trips through tickerConfig', () => {
        const config = { seconds: 8, results: 40, skip: 6, hidden: false };
        assert.deepEqual(tickerConfig(tickerQuery(config)), config);
    });

    test('a hidden ticker travels as ticker=off, and the query normalises too', () => {
        assert.equal(tickerQuery({ seconds: 8, results: 40, skip: 6, hidden: true }),
            '?resultCount=40&tickerSkip=6&tickerSeconds=8&ticker=off');
        assert.equal(tickerQuery({ seconds: 'abc', results: -4 }), '?resultCount=1&tickerSkip=0&tickerSeconds=20');
    });
});
