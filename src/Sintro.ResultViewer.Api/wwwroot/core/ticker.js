// =============================================================================
// Splitting a result list between the rows that fit on screen and a scrolling ticker.
// Pure — no DOM, no timers; the app layer measures and schedules.
//
// A fullscreen display cannot be scrolled, so anything past the last visible row would
// otherwise never be seen. The overflow scrolls through a marquee strip along the bottom edge.
// =============================================================================

/** Reading time per entry. 20s is a comfortable glance-up-and-read pace on a wall display. */
export const DEFAULT_TICKER_SECONDS = 20;

/**
 * The ticker scrolls rather than swapping, so it is not limited to what fits in a table row
 * and can carry a much longer tail of results.
 */
export const DEFAULT_TICKER_COUNT = 50;

export const MAX_TICKER_SECONDS = 120;
export const MAX_TICKER_COUNT = 500;

const clampNumber = (raw, fallback, min, max) => {
    const value = Number.parseInt(raw, 10);
    return Number.isFinite(value) ? Math.min(Math.max(value, min), max) : fallback;
};

/**
 * The settings a display will actually run with, from whatever was typed or passed: whole
 * numbers within the allowed range, defaults for anything unreadable. Used both when reading
 * a URL and when building one, so the URL the picker shows is exactly what the display gets.
 */
export const normaliseTickerSettings = ({ seconds, count } = {}) => ({
    // How long a given entry stays on screen — from entering at one edge to leaving at
    // the other. Effectively the reading time, and therefore the scroll speed.
    seconds: clampNumber(seconds, DEFAULT_TICKER_SECONDS, 1, MAX_TICKER_SECONDS),
    count: clampNumber(count, DEFAULT_TICKER_COUNT, 0, MAX_TICKER_COUNT),
});

/**
 * Per-display configuration, read from the query string so two screens on the same range
 * can cycle at different speeds without a server change:
 * /fullscreen/results?tickerSeconds=8&tickerCount=20
 */
export const tickerConfig = (search) => {
    const params = new URLSearchParams(search ?? '');

    return normaliseTickerSettings({
        seconds: params.get('tickerSeconds'),
        count: params.get('tickerCount'),
    });
};

/** How many rows of a given height fit in the space available. Always at least one. */
export const rowsThatFit = (availableHeight, rowHeight, minimum = 1) => {
    if (!Number.isFinite(availableHeight) || !Number.isFinite(rowHeight) || rowHeight <= 0) {
        return minimum;
    }
    return Math.max(minimum, Math.floor(availableHeight / rowHeight));
};

/**
 * Splits results into the rows to render and the overflow to scroll.
 *
 * A ticker holding a single entry is pointless, so in that case the entry is simply shown
 * as an ordinary row and the ticker stays empty.
 */
export const splitForTicker = (items, visibleCount, tickerCount) => {
    const all = items ?? [];
    const visible = Math.max(0, visibleCount);

    if (all.length <= visible) return { visible: all, ticker: [] };

    const overflow = all.slice(visible, visible + Math.max(0, tickerCount));
    if (overflow.length <= 1) {
        return { visible: all.slice(0, visible + overflow.length), ticker: [] };
    }

    return { visible: all.slice(0, visible), ticker: overflow };
};

/**
 * How long one full pass of the ticker content takes, given that each entry should remain
 * readable for `secondsVisible`.
 *
 * An entry is on screen from the moment it appears at one edge until it disappears at the
 * other, so it travels containerWidth + its own width in that time. Deriving the speed from
 * that keeps reading time constant no matter how wide the screen or how long the names.
 */
export const tickerDurationSeconds = (
    { containerWidth, contentWidth, entryCount, secondsVisible }) => {
    const content = Number.isFinite(contentWidth) ? contentWidth : 0;
    const container = Number.isFinite(containerWidth) ? Math.max(containerWidth, 0) : 0;
    const seconds = Number.isFinite(secondsVisible) && secondsVisible > 0 ? secondsVisible : 1;
    const count = Math.max(1, entryCount || 1);

    if (content <= 0) return 0;

    const averageEntry = content / count;
    const pixelsPerSecond = (container + averageEntry) / seconds;

    return pixelsPerSecond > 0 ? content / pixelsPerSecond : 0;
};

/**
 * Identity of the ticker's content, used to avoid rebuilding it when nothing changed.
 *
 * This matters more than it looks: results reload on every live message — every shot fired
 * anywhere on the range — and rebuilding the DOM restarts the CSS animation from zero. The
 * ticker would visibly snap back to the start several times a minute.
 */
export const tickerContentKey = (items) =>
    (items ?? []).map((item) => item?.id ?? '').join(',');

/** The query string a display should carry, so a copied URL keeps its settings. */
export const tickerQuery = (settings) => {
    const { seconds, count } = normaliseTickerSettings(settings);
    return `?tickerSeconds=${seconds}&tickerCount=${count}`;
};
