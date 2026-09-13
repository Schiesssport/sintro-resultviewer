// Fullscreen variants are real routes so a wall display can be pointed straight at one. Pure.

export const MODES = {
    dashboard: { fullscreen: false, lanes: true, results: true, leaderboard: false },
    live: { fullscreen: true, lanes: true, results: false, leaderboard: false },
    results: { fullscreen: true, lanes: false, results: true, leaderboard: false },
    leaderboard: { fullscreen: true, lanes: false, results: false, leaderboard: true },
    'live+results': { fullscreen: true, lanes: true, results: true, leaderboard: false },
};

export const DEFAULT_FULLSCREEN_MODE = 'live+results';

// Picker order: the one an operator most likely wants comes first.
export const FULLSCREEN_MODES = ['live+results', 'live', 'results', 'leaderboard'];
const FULLSCREEN_PREFIX = '/fullscreen';

export const pathForMode = (mode) =>
    mode === 'dashboard' ? '/' : `${FULLSCREEN_PREFIX}/${mode}`;

// decodeURIComponent throws on "%E0", and this runs before anything is on screen.
const decodeOrEmpty = (raw) => {
    try {
        return decodeURIComponent(raw);
    } catch {
        return '';
    }
};

// Anything unrecognised under /fullscreen falls back to live+results rather than blanking a TV.
export const parseViewMode = (pathname) => {
    const path = String(pathname ?? '/').replace(/\/+$/, '') || '/';
    if (path !== FULLSCREEN_PREFIX && !path.startsWith(`${FULLSCREEN_PREFIX}/`)) return 'dashboard';

    const requested = decodeOrEmpty(path.slice(FULLSCREEN_PREFIX.length).replace(/^\//, ''));
    if (requested === '') return DEFAULT_FULLSCREEN_MODE;

    // "live results" arrives when a "+" is percent-decoded or typed as a space.
    const normalised = requested.toLowerCase().trim().replace(/[\s_]+/g, '+');
    return Object.hasOwn(MODES, normalised) ? normalised : DEFAULT_FULLSCREEN_MODE;
};

export const layoutFor = (mode) => MODES[mode] ?? MODES.dashboard;
