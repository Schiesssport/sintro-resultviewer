import { test, describe, afterEach } from 'node:test';
import assert from 'node:assert/strict';

import { SintroApi } from '../api.js';

const realFetch = globalThis.fetch;
afterEach(() => { globalThis.fetch = realFetch; });

describe('tokenRejected', () => {
    test('a 401 on the live probe means the server minted a new session token', async () => {
        globalThis.fetch = async (path, { headers }) => {
            assert.equal(path, '/api/v2/live');
            assert.equal(headers.Authorization, 'Bearer abc');
            return { status: 401 };
        };
        assert.equal(await new SintroApi('abc').tokenRejected(), true);
    });

    test('a server that is down or a gate refusal keeps the feed retrying', async () => {
        globalThis.fetch = async () => { throw new TypeError('Failed to fetch'); };
        assert.equal(await new SintroApi('abc').tokenRejected(), false);
        globalThis.fetch = async () => ({ status: 403 });
        assert.equal(await new SintroApi('abc').tokenRejected(), false);
    });
});
