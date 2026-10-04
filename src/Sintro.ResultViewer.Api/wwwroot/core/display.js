// Per-display settings, result-count limits and marquee timing. Pure.

const DEFAULT_TICKER_SECONDS = 20;
export const DEFAULT_RESULT_COUNT = 50;
const MAX_TICKER_SECONDS = 120;
export const MAX_RESULT_COUNT = 500;

const clampNumber = (raw, fallback, min, max) => {
    const value = Number.parseInt(raw, 10);
    return Number.isFinite(value) ? Math.min(Math.max(value, min), max) : fallback;
};

// Used both when reading a URL and when building one, so the picker's URL is what the display gets.
// results: how many newest results a display loads (table and ticker share them);
// skip: how many of those the ticker leaves out at the top, so it does not repeat the visible rows.
export const normaliseDisplaySettings = ({ seconds, results, skip, hidden }) => ({
    seconds: clampNumber(seconds, DEFAULT_TICKER_SECONDS, 1, MAX_TICKER_SECONDS),
    results: clampNumber(results, DEFAULT_RESULT_COUNT, 1, MAX_RESULT_COUNT),
    skip: clampNumber(skip, 0, 0, MAX_RESULT_COUNT),
    hidden: hidden === true,
});

// Per-display settings from the query string: /fullscreen/results?resultCount=40&tickerSkip=6&tickerSeconds=8&ticker=off
export const parseDisplayQuery = (search) => {
    const params = new URLSearchParams(search ?? '');

    return normaliseDisplaySettings({
        seconds: params.get('tickerSeconds'),
        results: params.get('resultCount'),
        skip: params.get('tickerSkip'),
        hidden: params.get('ticker') === 'off',
    });
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

export const displayQuery = (settings) => {
    const { seconds, results, skip, hidden } = normaliseDisplaySettings(settings);
    return `?resultCount=${results}&tickerSkip=${skip}&tickerSeconds=${seconds}${hidden ? '&ticker=off' : ''}`;
};
