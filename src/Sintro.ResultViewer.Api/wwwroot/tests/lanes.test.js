import { test, describe } from 'node:test';
import assert from 'node:assert/strict';

import {
    isLineAvailable, lastActivityAt, dayOffsetMs,
    IDLE_AFTER_FINISH_MS, IDLE_AFTER_LAST_SHOT_MS, holdClearedLines, HOLD_AFTER_CLEAR_MS,
} from '../core/lanes.js';

const at = (iso) => Date.parse(iso);
const NOW = at('2026-07-08T21:00:00+02:00');

const shot = (iso) => ({ value: 9, at: iso });
const program = (overrides = {}) => ({
    finishedAt: null,
    series: [{ index: 1, shots: [shot('2026-07-08T20:55:00+02:00')] }],
    sighting: [],
    ...overrides,
});

describe('lastActivityAt', () => {
    test('takes the newest shot across all series', () => {
        const value = lastActivityAt(program({
            series: [
                { shots: [shot('2026-07-08T20:30:00+02:00')] },
                { shots: [shot('2026-07-08T20:58:00+02:00'), shot('2026-07-08T20:45:00+02:00')] },
            ],
        }));

        assert.equal(value, at('2026-07-08T20:58:00+02:00'));
    });

    test('counts sighting shots — the shooter is present either way', () => {
        const value = lastActivityAt(program({
            series: [],
            sighting: [{ shots: [shot('2026-07-08T20:59:00+02:00')] }],
        }));

        assert.equal(value, at('2026-07-08T20:59:00+02:00'));
    });

    test('is null when nothing has been fired', () => {
        assert.equal(lastActivityAt(program({ series: [], sighting: [] })), null);
        assert.equal(lastActivityAt(null), null);
    });

    test('ignores shots with an unusable timestamp', () => {
        const value = lastActivityAt(program({
            series: [{ shots: [{ at: null }, shot('2026-07-08T20:50:00+02:00'), { at: 'x' }] }],
        }));

        assert.equal(value, at('2026-07-08T20:50:00+02:00'));
    });
});

describe('isLineAvailable', () => {
    test('an empty line is available', () => {
        assert.equal(isLineAvailable(null, NOW), true);
    });

    test('a line being shot on is occupied', () => {
        assert.equal(isLineAvailable(program(), NOW), false);
    });

    test('a program loaded but not yet started is occupied, not free', () => {
        // The shooter is setting up; freeing the line would flicker it away on assignment.
        assert.equal(isLineAvailable(program({ series: [], sighting: [] }), NOW), false);
    });

    test('frees five minutes after the device wrote an end total', () => {
        const finished = program({ finishedAt: '2026-07-08T20:00:00+02:00' });

        assert.equal(isLineAvailable(finished, at('2026-07-08T20:04:00+02:00')), false);
        assert.equal(isLineAvailable(finished, at('2026-07-08T20:05:00+02:00')), true);
    });

    test('the end total wins over the last shot', () => {
        // A program can end well after its final shot; the explicit signal is better.
        const finished = program({
            finishedAt: '2026-07-08T20:58:00+02:00',
            series: [{ shots: [shot('2026-07-08T20:10:00+02:00')] }],
        });

        assert.equal(isLineAvailable(finished, NOW), false);
    });

    test('without an end total, frees six minutes after the last shot', () => {
        const running = program({ series: [{ shots: [shot('2026-07-08T20:50:00+02:00')] }] });

        assert.equal(isLineAvailable(running, at('2026-07-08T20:55:00+02:00')), false);
        assert.equal(isLineAvailable(running, at('2026-07-08T20:56:00+02:00')), true);
    });

    test('a resumed program reclaims its line, carrying everything already shot', () => {
        // The weather-break case: derived from the data, so a new shot simply re-occupies the line.
        const before = program({ series: [{ shots: [shot('2026-07-08T18:00:00+02:00')] }] });
        assert.equal(isLineAvailable(before, NOW), true);

        const resumed = program({
            series: [{ shots: [shot('2026-07-08T18:00:00+02:00'), shot('2026-07-08T20:59:00+02:00')] }],
        });
        assert.equal(isLineAvailable(resumed, NOW), false);
    });

    test('the two thresholds are the documented five and six minutes', () => {
        assert.equal(IDLE_AFTER_FINISH_MS, 5 * 60 * 1000);
        assert.equal(IDLE_AFTER_LAST_SHOT_MS, 6 * 60 * 1000);
    });
});

describe('dayOffsetMs', () => {
    test('is zero in production, where no reference date is set', () => {
        assert.equal(dayOffsetMs(null, NOW), 0);
        assert.equal(dayOffsetMs('', NOW), 0);
        assert.equal(dayOffsetMs('not-a-date', NOW), 0);
    });

    test('shifts whole days so demo data reads as today', () => {
        const now = new Date(2026, 6, 25, 14, 30).getTime();   // 25 July, local
        const offset = dayOffsetMs('2026-07-08', now);

        // Exactly 17 days back, and only whole days — the time of day still ticks live.
        assert.equal(offset, -17 * 24 * 60 * 60 * 1000);
    });

    test('is zero when the reference date is already today', () => {
        const now = new Date(2026, 6, 8, 9, 0).getTime();
        assert.equal(dayOffsetMs('2026-07-08', now), 0);
    });
});

describe('holdClearedLines', () => {
    const T0 = Date.parse('2026-07-08T21:00:00+02:00');
    const pass = (id) => program({ id, finishedAt: null });
    const lanes = (current) => [{ number: 1, currentProgram: current }, { number: 2, currentProgram: null }];

    test('a line reporting a program shows it and remembers it', () => {
        const held = holdClearedLines(new Map(), lanes(pass(7)), T0);
        assert.equal(held.lanes[0].currentProgram.id, 7);
        assert.equal(held.memory.get(1).program.id, 7);
    });

    test('a line the device just cleared keeps showing its last program', () => {
        // The device drops the lane assignment the moment the end marker is written.
        const first = holdClearedLines(new Map(), lanes(pass(7)), T0);
        const cleared = holdClearedLines(first.memory, lanes(null), T0 + 1000);

        assert.equal(cleared.lanes[0].currentProgram.id, 7);
    });

    test('the hold ends after HOLD_AFTER_CLEAR_MS, measured from the first empty snapshot', () => {
        const first = holdClearedLines(new Map(), lanes(pass(7)), T0);
        const cleared = holdClearedLines(first.memory, lanes(null), T0 + 1000);
        const still = holdClearedLines(cleared.memory, lanes(null), T0 + 1000 + HOLD_AFTER_CLEAR_MS - 1);
        const over = holdClearedLines(still.memory, lanes(null), T0 + 1000 + HOLD_AFTER_CLEAR_MS);

        assert.equal(still.lanes[0].currentProgram.id, 7);
        assert.equal(over.lanes[0].currentProgram, null);
        assert.equal(over.memory.has(1), false);
    });

    test('a new program on the line replaces the held one at once', () => {
        const first = holdClearedLines(new Map(), lanes(pass(7)), T0);
        const cleared = holdClearedLines(first.memory, lanes(null), T0 + 1000);
        const next = holdClearedLines(cleared.memory, lanes(pass(8)), T0 + 2000);

        assert.equal(next.lanes[0].currentProgram.id, 8);
    });

    test('a line that never had a program is simply free', () => {
        const held = holdClearedLines(new Map(), lanes(null), T0);
        assert.equal(held.lanes[0].currentProgram, null);
        assert.equal(held.lanes[1].currentProgram, null);
    });
});
