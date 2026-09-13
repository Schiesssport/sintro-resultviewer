// Splits a result list between the rows that fit on screen and a marquee for the overflow. Pure.

const DEFAULT_TICKER_SECONDS = 20;
const DEFAULT_TICKER_COUNT = 50;
const MAX_TICKER_SECONDS = 120;
const MAX_TICKER_COUNT = 500;

const clampNumber = (raw, fallback, min, max) => {
    const value = Number.parseInt(raw, 10);
    return Number.isFinite(value) ? Math.min(Math.max(value, min), max) : fallback;
};

// Used both when reading a URL and when building one, so the picker's URL is what the display gets.
export const normaliseTickerSettings = ({ seconds, count }) => ({
    seconds: clampNumber(seconds, DEFAULT_TICKER_SECONDS, 1, MAX_TICKER_SECONDS),
    count: clampNumber(count, DEFAULT_TICKER_COUNT, 0, MAX_TICKER_COUNT),
});

// Per-display settings from the query string: /fullscreen/results?tickerSeconds=8&tickerCount=20
export const tickerConfig = (search) => {
    const params = new URLSearchParams(search ?? '');

    return normaliseTickerSettings({
        seconds: params.get('tickerSeconds'),
        count: params.get('tickerCount'),
    });
};

export const rowsThatFit = (availableHeight, rowHeight) => {
    if (!Number.isFinite(availableHeight) || !Number.isFinite(rowHeight) || rowHeight <= 0) return 1;
    return Math.max(1, Math.floor(availableHeight / rowHeight));
};

// A ticker holding a single entry is pointless, so that entry becomes an ordinary row instead.
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

// An entry travels containerWidth + its own width while visible, so reading time stays constant.
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

// Rebuilding the ticker DOM restarts the marquee; this key says whether the content changed at all.
export const tickerContentKey = (items) =>
    (items ?? []).map((item) => item?.id ?? '').join(',');

export const tickerQuery = (settings) => {
    const { seconds, count } = normaliseTickerSettings(settings);
    return `?tickerSeconds=${seconds}&tickerCount=${count}`;
};
