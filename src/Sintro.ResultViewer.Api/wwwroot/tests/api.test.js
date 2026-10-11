import { test, describe, afterEach } from 'node:test';
import assert from 'node:assert/strict';

import { SintroApi } from '../api.js';

const realFetch = globalThis.fetch;
afterEach(() => { globalThis.fetch = realFetch; });

describe('programs', () => {
    const requested = async (filters) => {
        let url;
        globalThis.fetch = async (path) => { url = path; return { ok: true, text: async () => '{"items":[]}' }; };
        await new SintroApi('abc').programs(filters);
        return new URLSearchParams(url.split('?')[1]);
    };

    test('a chosen day is a one-day window', async () => {
        const query = await requested({ date: '2026-10-10', state: 'finished' });
        assert.equal(query.get('from'), '2026-10-10');
        assert.equal(query.get('to'), '2026-10-10');
        assert.equal(query.get('state'), 'finished');
    });

    test('no day asks for today, which the API resolves', async () => {
        const query = await requested({ state: 'finished' });
        assert.equal(query.get('dates'), 'today');
        assert.equal(query.has('from'), false);
    });
});

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
