import { test, describe } from 'node:test';
import assert from 'node:assert/strict';

import { nextRetryDelay, INITIAL_RETRY_MS, MAX_RETRY_MS } from '../core/reconnect.js';

describe('nextRetryDelay', () => {
    test('doubles each time', () => {
        assert.equal(nextRetryDelay(1000), 2000);
        assert.equal(nextRetryDelay(2000), 4000);
    });

    test('stops at the ceiling instead of growing forever', () => {
        // A display left on overnight must not end up retrying once an hour.
        assert.equal(nextRetryDelay(MAX_RETRY_MS), MAX_RETRY_MS);
        assert.equal(nextRetryDelay(MAX_RETRY_MS * 10), MAX_RETRY_MS);
    });

    test('reaches the ceiling in a handful of attempts, not a hundred', () => {
        let delay = INITIAL_RETRY_MS;
        let attempts = 0;
        while (delay < MAX_RETRY_MS && attempts < 100) {
            delay = nextRetryDelay(delay);
            attempts++;
        }

        assert.ok(attempts <= 5, `took ${attempts} attempts to back off fully`);
    });

    test('the total wait before the ceiling is short enough to go unnoticed', () => {
        // Everything from the first retry to the steady state happens inside half a minute.
        let delay = INITIAL_RETRY_MS;
        let total = delay;
        while (delay < MAX_RETRY_MS) {
            delay = nextRetryDelay(delay);
            total += delay;
        }

        assert.ok(total < 35000, `${total}ms of backoff before settling`);
    });

    test('survives a nonsense starting value', () => {
        for (const bad of [undefined, null, NaN, 0, -5]) {
            assert.equal(nextRetryDelay(bad), INITIAL_RETRY_MS * 2);
        }
    });
});
