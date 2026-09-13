import { test, describe } from 'node:test';
import assert from 'node:assert/strict';

import { typeOf, groupByTag, isProbeAllowed } from '../core/openapi.js';

describe('typeOf', () => {
    test('reads primitive, formatted, array and referenced schemas', () => {
        assert.equal(typeOf({ type: 'integer' }), 'integer');
        assert.equal(typeOf({ type: 'string', format: 'date' }), 'string date');
        assert.equal(typeOf({ type: 'array', items: { $ref: '#/components/schemas/Shot' } }), 'Shot[]');
        assert.equal(typeOf(undefined), '');
    });
});

describe('groupByTag', () => {
    const spec = {
        paths: {
            '/a': { get: { tags: ['2 · Resultate'] } },
            '/b': { get: { tags: ['10 · Später'] } },
            '/c': { get: { tags: ['1 · Live'] }, post: { tags: ['1 · Live'] } },
        },
    };

    test('sorts numbered tags numerically, so a tenth group lands last', () => {
        assert.deepEqual(groupByTag(spec).map(([tag]) => tag), ['1 · Live', '2 · Resultate', '10 · Später']);
    });

    test('keeps every method of every path under its tag', () => {
        const live = groupByTag(spec).find(([tag]) => tag === '1 · Live')[1];
        assert.deepEqual(live.map((entry) => entry.method), ['get', 'post']);
    });

    test('an empty spec yields no groups', () => {
        assert.deepEqual(groupByTag({}), []);
        assert.deepEqual(groupByTag(null), []);
    });
});

describe('isProbeAllowed', () => {
    const origin = 'http://range.local:8080';

    test('relative API and schema paths are allowed', () => {
        assert.equal(isProbeAllowed('/api/v2/programs?limit=5', origin), true);
        assert.equal(isProbeAllowed('/openapi/v2.json', origin), true);
        assert.equal(isProbeAllowed(`${origin}/api/v2/live`, origin), true);
    });

    test('the token never leaves the origin', () => {
        assert.equal(isProbeAllowed('https://example.org/api/v2/programs', origin), false);
        assert.equal(isProbeAllowed('//example.org/api/v2/programs', origin), false);
    });

    test('same-origin pages outside the API are not called with the token either', () => {
        assert.equal(isProbeAllowed('/', origin), false);
        assert.equal(isProbeAllowed('/docs', origin), false);
        assert.equal(isProbeAllowed('', origin), false);
    });
});
