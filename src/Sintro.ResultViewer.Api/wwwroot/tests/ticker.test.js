import { test, describe } from 'node:test';
import assert from 'node:assert/strict';

import {
    tickerConfig, rowsThatFit, splitForTicker, tickerDurationSeconds,
    tickerContentKey, tickerQuery, normaliseTickerSettings,
} from '../core/ticker.js';

const items = (count) => Array.from({ length: count }, (_, index) => ({ id: index + 1 }));
const ids = (list) => list.map((item) => item.id);

describe('tickerConfig', () => {
    test('defaults to the documented count and reading time', () => {
        const config = tickerConfig('');
        assert.equal(config.seconds, 20);
        assert.equal(config.count, 50);
    });

    test('reads per-display overrides from the query string', () => {
        const config = tickerConfig('?tickerSeconds=8&tickerCount=20');
        assert.equal(config.seconds, 8);
        assert.equal(config.count, 20);
    });

    test('ignores nonsense instead of producing NaN timers', () => {
        const config = tickerConfig('?tickerSeconds=abc&tickerCount=');
        assert.equal(config.seconds, 20);
        assert.equal(config.count, 50);
    });

    test('clamps values that would freeze or thrash the display', () => {
        assert.equal(tickerConfig('?tickerSeconds=0').seconds, 1);
        assert.equal(tickerConfig('?tickerSeconds=-5').seconds, 1);
        assert.equal(tickerConfig('?tickerSeconds=9999').seconds, 120);
        assert.equal(tickerConfig('?tickerCount=99999').count, 500);
    });

    test('a count of zero is allowed and switches the ticker off', () => {
        assert.equal(tickerConfig('?tickerCount=0').count, 0);
    });
});

describe('rowsThatFit', () => {
    test('divides the space by the row height', () => {
        assert.equal(rowsThatFit(400, 40), 10);
        assert.equal(rowsThatFit(419, 40), 10);
    });

    test('never returns zero rows, however cramped', () => {
        assert.equal(rowsThatFit(10, 40), 1);
        assert.equal(rowsThatFit(0, 40), 1);
    });

    test('survives an unmeasurable layout', () => {
        // Called before first paint, heights can be NaN or zero.
        assert.equal(rowsThatFit(NaN, 40), 1);
        assert.equal(rowsThatFit(400, 0), 1);
        assert.equal(rowsThatFit(400, NaN), 1);
    });
});

describe('splitForTicker', () => {
    test('everything is visible when it all fits', () => {
        const split = splitForTicker(items(5), 10, 36);
        assert.deepEqual(ids(split.visible), [1, 2, 3, 4, 5]);
        assert.deepEqual(split.ticker, []);
    });

    test('the overflow goes to the ticker', () => {
        const split = splitForTicker(items(20), 8, 36);
        assert.deepEqual(ids(split.visible), [1, 2, 3, 4, 5, 6, 7, 8]);
        assert.deepEqual(ids(split.ticker), [9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20]);
    });

    test('the ticker is capped, and the rest is simply not shown', () => {
        const split = splitForTicker(items(100), 10, 50);
        assert.equal(split.visible.length, 10);
        assert.equal(split.ticker.length, 50);
        assert.equal(ids(split.ticker).at(-1), 60);
    });

    test('a single overflow row is shown inline rather than in a ticker', () => {
        // A ticker that never changes is just a row that is hard to read.
        const split = splitForTicker(items(9), 8, 36);
        assert.equal(split.visible.length, 9);
        assert.deepEqual(split.ticker, []);
    });

    test('tickerCount 0 hides the overflow entirely', () => {
        const split = splitForTicker(items(20), 8, 0);
        assert.equal(split.visible.length, 8);
        assert.deepEqual(split.ticker, []);
    });

    test('handles an empty list and a zero-row viewport', () => {
        assert.deepEqual(splitForTicker([], 10, 36), { visible: [], ticker: [] });
        assert.deepEqual(splitForTicker(undefined, 10, 36), { visible: [], ticker: [] });

        const none = splitForTicker(items(5), 0, 36);
        assert.deepEqual(none.visible, []);
        assert.equal(none.ticker.length, 5);
    });
});

describe('normaliseTickerSettings', () => {
    test('a cleared count box does not silently switch the ticker off', () => {
        // Number('') is 0; the URL must not end up saying tickerCount=0.
        assert.equal(normaliseTickerSettings({ seconds: '', count: '' }).count, 50);
    });

    test('clamps and truncates the way the display will read them back', () => {
        const settings = normaliseTickerSettings({ seconds: '7.9', count: '9999' });
        assert.equal(settings.seconds, 7);
        assert.equal(settings.count, 500);
    });

    test('the built query round-trips through tickerConfig unchanged', () => {
        const settings = normaliseTickerSettings({ seconds: '12', count: '30' });
        assert.deepEqual(tickerConfig(tickerQuery(settings)), settings);
    });

    test('tickerQuery normalises too, so a URL never carries an unreadable value', () => {
        assert.equal(tickerQuery({ seconds: 'abc', count: -4 }), '?tickerSeconds=20&tickerCount=0');
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
        const config = { seconds: 8, count: 40 };
        assert.deepEqual(tickerConfig(tickerQuery(config)), config);
    });

    test('always names both settings, so a copied URL is self-contained', () => {
        assert.equal(tickerQuery({ seconds: 20, count: 50 }), '?tickerSeconds=20&tickerCount=50');
    });
});
