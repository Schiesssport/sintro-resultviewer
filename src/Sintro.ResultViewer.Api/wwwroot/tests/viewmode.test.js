import { test, describe } from 'node:test';
import assert from 'node:assert/strict';

import { parseViewMode, layoutFor, pathForMode, MODES, DEFAULT_FULLSCREEN_MODE }
    from '../core/viewmode.js';

describe('parseViewMode', () => {
    test('the root is the office dashboard', () => {
        assert.equal(parseViewMode('/'), 'dashboard');
        assert.equal(parseViewMode('/index.html'), 'dashboard');
    });

    test('a malformed percent escape falls back rather than throwing', () => {
        // decodeURIComponent throws on "%E0", and a typo in a TV's bookmark must not blank the display.
        assert.equal(parseViewMode('/fullscreen/%E0'), DEFAULT_FULLSCREEN_MODE);
    });

    test('each fullscreen variant resolves to itself', () => {
        assert.equal(parseViewMode('/fullscreen/live'), 'live');
        assert.equal(parseViewMode('/fullscreen/results'), 'results');
        assert.equal(parseViewMode('/fullscreen/leaderboard'), 'leaderboard');
        assert.equal(parseViewMode('/fullscreen/live+results'), 'live+results');
    });

    test('a bare /fullscreen still means live+results', () => {
        // The variants came later; the original URL must keep doing what it did.
        assert.equal(parseViewMode('/fullscreen'), DEFAULT_FULLSCREEN_MODE);
        assert.equal(parseViewMode('/fullscreen/'), DEFAULT_FULLSCREEN_MODE);
    });

    test('an unknown variant falls back rather than showing nothing', () => {
        // Nobody can fix a typo on a wall-mounted TV, so a blank screen is the worst answer.
        assert.equal(parseViewMode('/fullscreen/nonsense'), DEFAULT_FULLSCREEN_MODE);
    });

    test('accepts a decoded plus, which is what a typed or copied URL often carries', () => {
        assert.equal(parseViewMode('/fullscreen/live results'), 'live+results');
        assert.equal(parseViewMode('/fullscreen/LIVE+RESULTS'), 'live+results');
        assert.equal(parseViewMode('/fullscreen/live%2Bresults'), 'live+results');
    });

    test('tolerates a trailing slash and missing input', () => {
        assert.equal(parseViewMode('/fullscreen/live/'), 'live');
        assert.equal(parseViewMode(undefined), 'dashboard');
    });
});

describe('layoutFor', () => {
    test('live shows only the lines', () => {
        const layout = layoutFor('live');
        assert.equal(layout.lanes, true);
        assert.equal(layout.results, false);
        assert.equal(layout.fullscreen, true);
    });

    test('results shows only the results', () => {
        const layout = layoutFor('results');
        assert.equal(layout.lanes, false);
        assert.equal(layout.results, true);
    });

    test('leaderboard shows neither yet — it is a placeholder', () => {
        const layout = layoutFor('leaderboard');
        assert.equal(layout.lanes, false);
        assert.equal(layout.results, false);
        assert.equal(layout.leaderboard, true);
    });

    test('the dashboard is the only non-fullscreen mode', () => {
        const nonFullscreen = Object.entries(MODES)
            .filter(([, layout]) => !layout.fullscreen)
            .map(([name]) => name);

        assert.deepEqual(nonFullscreen, ['dashboard']);
    });

    test('an unknown mode degrades to the dashboard layout', () => {
        assert.deepEqual(layoutFor('nope'), MODES.dashboard);
    });
});

describe('pathForMode', () => {
    test('round-trips every mode', () => {
        for (const mode of Object.keys(MODES)) {
            assert.equal(parseViewMode(pathForMode(mode)), mode, `round-trip failed for ${mode}`);
        }
    });

    test('the dashboard lives at the root, not under /fullscreen', () => {
        assert.equal(pathForMode('dashboard'), '/');
    });
});
