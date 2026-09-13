import { test, describe } from 'node:test';
import assert from 'node:assert/strict';

import { parseViewMode, layoutFor, pathForMode, MODES } from '../core/viewmode.js';

describe('parseViewMode', () => {
    test('the root is the office dashboard', () => {
        assert.equal(parseViewMode('/'), 'dashboard');
        assert.equal(parseViewMode('/index.html'), 'dashboard');
        assert.equal(parseViewMode(undefined), 'dashboard');
    });

    test('each fullscreen variant resolves to itself', () => {
        assert.equal(parseViewMode('/fullscreen/live'), 'live');
        assert.equal(parseViewMode('/fullscreen/results'), 'results');
        assert.equal(parseViewMode('/fullscreen/live+results'), 'live+results');
    });

    test('tolerates what the server also tolerates: case and a trailing slash', () => {
        assert.equal(parseViewMode('/fullscreen/LIVE+RESULTS'), 'live+results');
        assert.equal(parseViewMode('/fullscreen/live%2Bresults'), 'live+results');
        assert.equal(parseViewMode('/fullscreen/live/'), 'live');
    });

    test('anything else reads as the dashboard; the server does not serve unknown variants anyway', () => {
        assert.equal(parseViewMode('/fullscreen'), 'dashboard');
        assert.equal(parseViewMode('/fullscreen/nonsense'), 'dashboard');
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

    test('every layout carries the same three flags', () => {
        for (const [name, layout] of Object.entries(MODES)) {
            assert.deepEqual(Object.keys(layout).sort(), ['fullscreen', 'lanes', 'results'], name);
        }
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
