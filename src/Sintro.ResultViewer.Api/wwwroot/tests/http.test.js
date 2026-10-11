import { test, describe } from 'node:test';
import assert from 'node:assert/strict';

import { describeFailure, prettyJson } from '../core/http.js';

describe('describeFailure', () => {
    test('prefers the detail of an ApiError body, then its code', () => {
        assert.equal(describeFailure(400, '{"error":"invalid_state","detail":"state must be one of …"}'), '400 — state must be one of …');
        assert.equal(describeFailure(401, '{"error":"unauthorized","detail":null}'), '401 — unauthorized');
    });

    test('falls back to the status for an empty or foreign body', () => {
        assert.equal(describeFailure(404, ''), 'HTTP 404');
        assert.equal(describeFailure(502, '<html>bad gateway</html>'), 'HTTP 502');
    });
});

describe('prettyJson', () => {
    test('indents JSON and leaves anything else alone', () => {
        assert.equal(prettyJson('{"a":1}'), '{\n  "a": 1\n}');
        assert.equal(prettyJson('not json'), 'not json');
    });
});
