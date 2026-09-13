// Fullscreen variants are real routes so a wall display can be pointed straight at one. Pure.

export const MODES = {
    dashboard: { fullscreen: false, lanes: true, results: true },
    live: { fullscreen: true, lanes: true, results: false },
    results: { fullscreen: true, lanes: false, results: true },
    'live+results': { fullscreen: true, lanes: true, results: true },
};

// Picker order: the one an operator most likely wants comes first.
export const FULLSCREEN_MODES = ['live+results', 'live', 'results'];
const FULLSCREEN_PREFIX = '/fullscreen/';

export const pathForMode = (mode) =>
    mode === 'dashboard' ? '/' : `${FULLSCREEN_PREFIX}${mode}`;

// The server serves exactly the three fullscreen routes; anything else it hands this page for is the root.
export const parseViewMode = (pathname) => {
    const path = String(pathname ?? '/').replace(/\/+$/, '');
    if (!path.startsWith(FULLSCREEN_PREFIX)) return 'dashboard';

    const mode = decodeURIComponent(path.slice(FULLSCREEN_PREFIX.length)).toLowerCase();
    return Object.hasOwn(MODES, mode) ? mode : 'dashboard';
};

export const layoutFor = (mode) => MODES[mode] ?? MODES.dashboard;
