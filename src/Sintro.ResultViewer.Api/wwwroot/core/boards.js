// Display boards: a named fullscreen mode plus its ticker settings. Seeded with one board per mode,
// then edited, extended and removed by the operator; the list is stored per browser. Pure.

import { FULLSCREEN_MODES, pathForMode } from './viewmode.js';
import { normaliseTickerSettings, tickerQuery } from './ticker.js';

export const BOARDS_STORAGE_KEY = 'sintro.fullscreen.boards';

export const normaliseBoard = (raw) => ({
    name: String(raw?.name ?? '').trim(),
    mode: FULLSCREEN_MODES.includes(raw?.mode) ? raw.mode : FULLSCREEN_MODES[0],
    ...normaliseTickerSettings(raw ?? {}),
});

export const defaultBoards = () => FULLSCREEN_MODES.map((mode) => normaliseBoard({ mode }));

// Stored text → boards; anything unreadable or empty falls back to the defaults.
export const parseBoards = (text) => {
    try {
        const parsed = JSON.parse(text ?? 'null');
        return Array.isArray(parsed) && parsed.length > 0 ? parsed.map(normaliseBoard) : defaultBoards();
    } catch {
        return defaultBoards();
    }
};

export const boardQuery = (board) => tickerQuery(board);

export const boardPath = (board) => pathForMode(board.mode) + boardQuery(board);
