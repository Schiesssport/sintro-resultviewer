import { test, describe } from 'node:test';
import assert from 'node:assert/strict';

import { normaliseBoard, defaultBoards, parseBoards, boardPath } from '../core/boards.js';

describe('boards', () => {
    test('the defaults are one board per fullscreen mode with default settings', () => {
        assert.deepEqual(defaultBoards().map((board) => board.mode), ['live+results', 'live', 'results']);
        assert.deepEqual(defaultBoards()[0], { name: '', mode: 'live+results', seconds: 20, results: 50, skip: 0, hidden: false });
    });

    test('a stored board is normalised like a URL would be', () => {
        assert.deepEqual(normaliseBoard({ name: ' Saal ', mode: 'results', results: '9999', skip: -1, seconds: '8', hidden: true }),
            { name: 'Saal', mode: 'results', seconds: 8, results: 500, skip: 0, hidden: true });
        assert.equal(normaliseBoard({ mode: 'nonsense' }).mode, 'live+results');
    });

    test('unreadable or empty storage yields the defaults', () => {
        assert.deepEqual(parseBoards(null), defaultBoards());
        assert.deepEqual(parseBoards('{not json'), defaultBoards());
        assert.deepEqual(parseBoards('[]'), defaultBoards());
        assert.deepEqual(parseBoards('{"a":1}'), defaultBoards());
    });

    test('stored boards round-trip', () => {
        const boards = [normaliseBoard({ name: 'Bar', mode: 'live', seconds: 5 })];
        assert.deepEqual(parseBoards(JSON.stringify(boards)), boards);
    });

    test('the path carries mode and settings in the documented order', () => {
        assert.equal(boardPath(normaliseBoard({ mode: 'results', results: 40, skip: 6, seconds: 8, hidden: true })),
            '/fullscreen/results?resultCount=40&tickerSkip=6&tickerSeconds=8&ticker=off');
    });
});
