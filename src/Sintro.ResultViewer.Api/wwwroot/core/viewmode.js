// =============================================================================
// Which view the URL asks for. Pure — parses a pathname, touches no DOM.
//
// Fullscreen variants are real routes so a wall display can be pointed straight at one,
// bookmarked, and driven by the back button.
// =============================================================================

export const MODES = {
    dashboard: { fullscreen: false, lanes: true, results: true, leaderboard: false },
    live: { fullscreen: true, lanes: true, results: false, leaderboard: false },
    results: { fullscreen: true, lanes: false, results: true, leaderboard: false },
    leaderboard: { fullscreen: true, lanes: false, results: false, leaderboard: true },
    'live+results': { fullscreen: true, lanes: true, results: true, leaderboard: false },
};

export const DEFAULT_FULLSCREEN_MODE = 'live+results';

/** Offered by the picker, in the order an operator is most likely to want them. */
export const FULLSCREEN_MODES = ['live+results', 'live', 'results', 'leaderboard'];
export const FULLSCREEN_PREFIX = '/fullscreen';

/** The path for a mode, so links and history entries are built in exactly one place. */
export const pathForMode = (mode) =>
    mode === 'dashboard' ? '/' : `${FULLSCREEN_PREFIX}/${mode}`;

/**
 * Resolves a pathname to a mode name.
 *
 * A bare /fullscreen keeps working and means live+results, which is what it did before the
 * variants existed; anything unrecognised under /fullscreen falls back to the same rather
 * than showing a blank screen on a TV nobody can reach.
 */
export const parseViewMode = (pathname) => {
    const path = String(pathname ?? '/').replace(/\/+$/, '') || '/';

    if (path !== FULLSCREEN_PREFIX && !path.startsWith(`${FULLSCREEN_PREFIX}/`)) {
        return 'dashboard';
    }

    const requested = decodeURIComponent(path.slice(FULLSCREEN_PREFIX.length).replace(/^\//, ''));
    if (requested === '') return DEFAULT_FULLSCREEN_MODE;

    // "live results" arrives when a "+" is percent-decoded or typed as a space.
    const normalised = requested.toLowerCase().trim().replace(/[\s_]+/g, '+');
    return Object.hasOwn(MODES, normalised) ? normalised : DEFAULT_FULLSCREEN_MODE;
};

export const layoutFor = (mode) => MODES[mode] ?? MODES.dashboard;
