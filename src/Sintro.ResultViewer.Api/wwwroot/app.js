// DOM layer of the viewer; all pure logic lives in core/. No framework, no build step.

import { TRANSLATIONS, DEFAULT_LANGUAGE, translate } from './core/i18n.js';
import {
    escapeHtml, shooterLabel, totalDisplay, matchesFilter, shotGroups, programLabel, laneContext,
    tickerEntry,
} from './core/format.js';
import { shotDial } from './core/sectors.js';
import { isLineAvailable, dayOffsetMs, holdClearedLines } from './core/lanes.js';
import { parseViewMode, layoutFor, pathForMode, FULLSCREEN_MODES } from './core/viewmode.js';
import {
    tickerConfig, rowsThatFit, splitForTicker, tickerDurationSeconds, tickerContentKey, tickerQuery,
    normaliseTickerSettings,
} from './core/ticker.js';
import { SintroApi } from './api.js';

const RESULT_LIMIT = 100;
const IDLE_SWEEP_MS = 15_000;
const ESTIMATED_ROW_HEIGHT = 44;

const api = new SintroApi(window.SINTRO_TOKEN);
let ticker = tickerConfig(location.search);

let language = DEFAULT_LANGUAGE;
let programs = [];
let lanes = [];
// What each line last showed, so a result outlives the device clearing the line.
let laneMemory = new Map();
let filterText = '';
let mode = 'dashboard';
let clockOffsetMs = 0;
let tickerItems = [];
let tickerKey = null;
let liveStatus = 'connecting';
let health = null;
let resultsRequest = 0;

const t = (key, params) => translate(TRANSLATIONS[language], key, params);
const el = (id) => document.getElementById(id);

// Read from the markup so a message row's colspan cannot drift from the <colgroup>.
const resultColumns = () => document.querySelectorAll('.results colgroup col').length;

// Wall clock shifted onto the data's day when a ReferenceDate pins development.
const now = () => Date.now() + clockOffsetMs;

// -- Shared cells -------------------------------------------------------------

const totalCell = (program) => {
    const display = totalDisplay(program);
    if (display.hasTotal) return String(display.value);

    return display.reasonKey
        ? `<span class="total-missing" title="${escapeHtml(t(display.reasonKey))}">–</span>`
        : '–';
};

const shooterName = (program) => {
    const label = shooterLabel(program, t);
    const duplicate = label.shooter?.duplicateLicense
        ? `<span class="badge badge-dup">${t('badge.duplicateLicense')}</span>`
        : '';

    return { label, html: `${escapeHtml(label.text)}${duplicate}` };
};

const clubCell = (program) => {
    const name = program.shooter?.club?.name;
    return name ? escapeHtml(name) : '<span class="value-none">–</span>';
};

// Eight wedges around the ring value with the reported sector filled; the number stays readable.
const shotRing = (sector) => {
    const dial = shotDial({ hitSector: sector });
    const { cx, cy } = dial.geometry;
    const wedges = dial.wedges.map((wedge) =>
        `<path d="${wedge.ringPath}" class="${wedge.filled ? 'ring-wedge is-hit' : 'ring-wedge'}"/>`)
        .join('');

    return `<svg class="shot-ring${dial.isCentre ? ' is-centre' : ''}" viewBox="0 0 ${cx * 2} ${cy * 2}" aria-hidden="true">${wedges}</svg>`;
};

const shotHtml = (shot, withRing) => {
    const value = `<span class="shot-value">${escapeHtml(shot.text)}</span>`;
    const ring = withRing && shot.sector !== null ? shotRing(shot.sector) : '';

    return `<span class="shot${withRing ? ' is-ringed' : ''}">${ring}${value}</span>`;
};

const shotGroupChip = (group, withRings) => `
        <span class="shot-group" title="${escapeHtml(t('series.subtotal'))} ${group.subtotal}">
            <span class="shot-group-code">${escapeHtml(group.code)}</span>
            <span class="shot-group-values">${group.shots.map((shot) => shotHtml(shot, withRings)).join('')}</span>
            ${group.bestFineValue === null ? '' :
                `<span class="shot-group-fine" title="${escapeHtml(t('series.bestFine'))}">${group.bestFineValue}</span>`}
        </span>`;

// One chip per series: [A10 | 7 6 0 6 | 96].
const shotGroupsCell = (program, { withRings = false } = {}) => {
    const groups = shotGroups(program);
    if (groups.length === 0) return '';

    return `<div class="shot-groups">${groups.map((group) => shotGroupChip(group, withRings)).join('')}</div>`;
};

// -- Lines --------------------------------------------------------------------

const freeLineRow = (lane) => `
            <tr class="lane-row is-free">
                <td class="lane-number">${lane.number}</td>
                <td class="lane-free" colspan="3">${escapeHtml(t('lane.available'))}</td>
            </tr>`;

const occupiedLineRow = (lane, program) => {
    const { label, html } = shooterName(program);

    return `
        <tr class="lane-row">
            <td class="lane-number">${lane.number}</td>
            <td class="lane-shooter">
                <div class="lane-shooter-name ${label.fallback ? 'is-fallback' : ''}">${html}</div>
                <div class="lane-context">${escapeHtml(laneContext(program))}</div>
            </td>
            <td class="lane-total">${totalCell(program)}</td>
            <td class="lane-shots">${shotGroupsCell(program, { withRings: true })}</td>
        </tr>`;
};

const lineRow = (lane) => {
    const program = lane.currentProgram;
    return !program || isLineAvailable(program, now()) ? freeLineRow(lane) : occupiedLineRow(lane, program);
};

const renderLines = () => {
    // A hold ends purely through time, so recompute on every render, not only on new data.
    const held = holdClearedLines(laneMemory, lanes, now());
    laneMemory = held.memory;

    el('lanes-body').innerHTML = lanes.length === 0
        ? `<tr><td class="message" colspan="4">${escapeHtml(t('msg.noLanes'))}</td></tr>`
        : held.lanes.map(lineRow).join('');

    // CSS cannot count rows, and the line-only view spreads them over the whole screen.
    document.body.style.setProperty('--lane-count', String(Math.max(1, lanes.length)));
};

// -- Results ------------------------------------------------------------------

const programRow = (program) => {
    const { label, html } = shooterName(program);

    return `<tr>
        <td class="col-club">${clubCell(program)}</td>
        <td class="col-shooter ${label.fallback ? 'shooter-fallback' : 'shooter-name'}">${html}</td>
        <td class="col-total">${totalCell(program)}</td>
        <td class="col-shots">${shotGroupsCell(program)}</td>
        <td class="col-program">${escapeHtml(programLabel(program))}</td></tr>`;
};

const messageRow = (text) =>
    `<tr><td colspan="${resultColumns()}" class="message">${escapeHtml(text)}</td></tr>`;

const visibleResults = () => programs.filter(
    (program) => matchesFilter(program, filterText, shooterLabel(program, t).text));

// Fullscreen only. The layout always reserves the ticker strip, so the measured box excludes it.
const measureResultCapacity = () => {
    const scroll = el('results-scroll');
    const head = scroll.querySelector('thead');
    const sampleRow = el('results-body').querySelector('tr');

    const rowHeight = sampleRow?.getBoundingClientRect().height || ESTIMATED_ROW_HEIGHT;
    const available = scroll.getBoundingClientRect().height
        - (head?.getBoundingClientRect().height ?? 0);

    return rowsThatFit(available, rowHeight);
};

// Rendered once so a row can be measured, then split and rendered again.
const renderFullscreenResults = (body, visible) => {
    body.innerHTML = visible.map(programRow).join('');

    const split = splitForTicker(visible, measureResultCapacity(), ticker.count);
    body.innerHTML = split.visible.map(programRow).join('');

    return split.ticker;
};

const renderResults = () => {
    const visible = visibleResults();
    const body = el('results-body');

    if (visible.length === 0) {
        body.innerHTML = messageRow(t('msg.empty'));
        tickerItems = [];
    } else if (!layoutFor(mode).fullscreen) {
        body.innerHTML = visible.map(programRow).join('');
        tickerItems = [];
    } else {
        tickerItems = renderFullscreenResults(body, visible);
    }

    renderTicker();
    el('result-count').textContent = t('msg.count', { shown: visible.length });
};

// -- Ticker -------------------------------------------------------------------

const tickerMarkup = () => {
    const entries = tickerItems
        .map((item) => `<span class="ticker-entry">${escapeHtml(tickerEntry(item, t))}</span>`)
        .join('');

    // The run is doubled and translated by exactly -50%, which makes the loop seamless.
    return `
        <div class="ticker-viewport">
            <div class="ticker-track">
                <span class="ticker-run">${entries}</span>
                <span class="ticker-run" aria-hidden="true">${entries}</span>
            </div>
        </div>`;
};

// Rebuilding restarts the marquee, and results reload on every live message: hence the key guard.
const renderTicker = () => {
    const bar = el('results-ticker');
    const key = tickerContentKey(tickerItems);
    if (key === tickerKey) return;
    tickerKey = key;

    if (tickerItems.length === 0) {
        bar.classList.add('hidden');
        bar.innerHTML = '';
        return;
    }

    // Resume at the same point in the loop rather than jumping back to the start.
    const elapsed = document.querySelector('.ticker-track')?.getAnimations?.()[0]?.currentTime ?? 0;

    bar.classList.remove('hidden');
    bar.innerHTML = tickerMarkup();
    applyTickerSpeed(elapsed);
};

// Speed comes from measurement, so each entry stays legible for the configured seconds.
const applyTickerSpeed = (resumeAtMs = 0) => {
    const viewport = document.querySelector('.ticker-viewport');
    const track = document.querySelector('.ticker-track');
    const run = document.querySelector('.ticker-run');
    if (!viewport || !track || !run) return;

    const duration = tickerDurationSeconds({
        containerWidth: viewport.getBoundingClientRect().width,
        contentWidth: run.getBoundingClientRect().width,
        entryCount: tickerItems.length,
        secondsVisible: ticker.seconds,
    });

    track.style.animationDuration = duration > 0 ? `${duration}s` : '';

    const animation = track.getAnimations?.()[0];
    if (animation && duration > 0 && resumeAtMs > 0) {
        animation.currentTime = resumeAtMs % (duration * 1000);
    }
};

// -- Static text --------------------------------------------------------------

const renderStaticText = () => {
    document.documentElement.lang = language;
    document.title = t('app.title');

    for (const node of document.querySelectorAll('[data-i18n]')) {
        node.textContent = t(node.dataset.i18n);
    }
    for (const node of document.querySelectorAll('[data-i18n-placeholder]')) {
        node.placeholder = t(node.dataset.i18nPlaceholder);
    }
    for (const node of document.querySelectorAll('[data-i18n-title]')) {
        node.title = t(node.dataset.i18nTitle);
    }
    for (const node of document.querySelectorAll('[data-i18n-aria-label]')) {
        node.setAttribute('aria-label', t(node.dataset.i18nAriaLabel));
    }

    liveState(liveStatus);
    showExposureWarning();
};

const renderAll = () => {
    renderLines();
    renderResults();
};

// -- Data ---------------------------------------------------------------------

// Requests overlap on every live message; only the latest may render, or a stale list lands last.
const loadResults = async () => {
    const request = ++resultsRequest;

    try {
        // Finished only: a running pass shows on its line, never twice. The API sends newest first.
        const page = await api.programs({
            date: el('date-input').value || undefined,
            limit: RESULT_LIMIT,
            state: 'finished',
        });
        if (request !== resultsRequest) return;

        programs = page.items;
        renderResults();
    } catch (error) {
        if (request !== resultsRequest) return;

        programs = [];
        tickerItems = [];
        renderTicker();
        el('results-body').innerHTML = messageRow(t('msg.error', { detail: error.message }));
        el('result-count').textContent = '';
    }
};

const applyLanes = (incoming) => {
    lanes = incoming ?? [];
    renderLines();
};

const loadLanes = async () => {
    try {
        applyLanes(await api.lanes());
    } catch {
        // The results error already tells the operator the API is unreachable.
    }
};

const showExposureWarning = () => {
    if (!health?.publicExposure?.length) return;

    const banner = el('exposure-warning');
    banner.textContent = t('warn.publicAccess', { ranges: health.publicExposure.join(' | ') });
    banner.classList.remove('hidden');
};

const liveState = (state) => {
    liveStatus = state;
    const indicator = el('live-indicator');
    indicator.classList.toggle('is-live', state === 'connected');
    indicator.classList.toggle('is-down', state === 'offline');
    indicator.textContent = t(`live.${state}`);
};

// -- Routing ------------------------------------------------------------------

const applyMode = (next) => {
    mode = next;
    const layout = layoutFor(mode);

    document.body.classList.toggle('is-fullscreen', layout.fullscreen);
    document.body.dataset.mode = mode;

    el('section-lanes').classList.toggle('hidden', !layout.lanes);
    el('section-results').classList.toggle('hidden', !layout.results);
    el('section-leaderboard').classList.toggle('hidden', !layout.leaderboard);
    el('fullscreen-exit').classList.toggle('hidden', !layout.fullscreen);

    renderAll();
};

// The display settings live in the URL, so every navigation re-reads them.
const applyRoute = (next, query) => {
    ticker = tickerConfig(query);
    tickerKey = null;
    applyMode(next);
};

const goTo = (next, query = location.search) => {
    history.pushState({}, '', pathForMode(next) + query);
    applyRoute(next, query);
};

// -- Fullscreen picker --------------------------------------------------------

// Clamped and whole, so the URL beside each mode is exactly what that display gets.
const pickerTicker = () => normaliseTickerSettings({
    seconds: el('ticker-seconds-input').value,
    count: el('ticker-count-input').value,
});

const absoluteUrlFor = (target) =>
    new URL(pathForMode(target) + tickerQuery(pickerTicker()), location.origin).href;

// A wall display is usually another screen, so every mode also offers its URL for pasting.
const renderFullscreenModes = () => {
    el('fullscreen-modes').innerHTML = FULLSCREEN_MODES.map((target) => `
        <div class="picker-item">
            <div class="picker-text">
                <div class="picker-name">${escapeHtml(t(`fullscreen.mode.${target}`))}</div>
                <div class="picker-url">${escapeHtml(absoluteUrlFor(target))}</div>
            </div>
            <button class="btn-action" data-open-mode="${escapeHtml(target)}">${escapeHtml(t('fullscreen.show'))}</button>
            <button class="btn-secondary" data-copy-mode="${escapeHtml(target)}">${escapeHtml(t('fullscreen.copy'))}</button>
        </div>`).join('');
};

const openFullscreenPicker = () => {
    el('ticker-seconds-input').value = String(ticker.seconds);
    el('ticker-count-input').value = String(ticker.count);
    renderFullscreenModes();
    el('fullscreen-dialog').showModal();
};

const showFullscreen = async (target) => {
    const query = tickerQuery(pickerTicker());
    el('fullscreen-dialog').close();
    goTo(target, query);

    try {
        // Needs a user gesture; a /fullscreen/* URL opened directly still drops the chrome.
        await document.documentElement.requestFullscreen();
    } catch {
        // Denied or unsupported; the chrome-less layout stands on its own.
    }
};

const copyFullscreenUrl = async (target, button) => {
    try {
        await navigator.clipboard.writeText(absoluteUrlFor(target));
    } catch {
        // Clipboard needs a secure context; on plain http the URL beside the button is still selectable.
        button.textContent = t('fullscreen.copyFailed');
        return;
    }

    button.textContent = t('fullscreen.copied');
    setTimeout(() => { button.textContent = t('fullscreen.copy'); }, 2000);
};

const exitFullscreen = async () => {
    goTo('dashboard');
    if (document.fullscreenElement) await document.exitFullscreen();
};

// -- Wiring -------------------------------------------------------------------

const attachToolbarHandlers = () => {
    el('filter-input').addEventListener('input', (event) => {
        filterText = event.target.value;
        renderResults();
    });
    el('date-input').addEventListener('change', loadResults);
    el('reload-button').addEventListener('click', loadResults);

    el('language-select').addEventListener('change', (event) => {
        language = event.target.value;
        tickerKey = null;   // anonymous ticker entries are language-dependent, the key is id-based
        renderStaticText();
        renderAll();
    });
};

const attachFullscreenHandlers = () => {
    el('fullscreen-button').addEventListener('click', openFullscreenPicker);
    el('fullscreen-exit').addEventListener('click', exitFullscreen);
    el('fullscreen-dialog-close').addEventListener('click', () => el('fullscreen-dialog').close());

    for (const id of ['ticker-seconds-input', 'ticker-count-input']) {
        el(id).addEventListener('input', renderFullscreenModes);
    }

    el('fullscreen-modes').addEventListener('click', (event) => {
        const open = event.target.closest('[data-open-mode]');
        if (open) return void showFullscreen(open.dataset.openMode);

        const copy = event.target.closest('[data-copy-mode]');
        if (copy) copyFullscreenUrl(copy.dataset.copyMode, copy);
    });

    // Leaving browser fullscreen with Esc bypasses the button, so follow its state back.
    document.addEventListener('fullscreenchange', () => {
        if (!document.fullscreenElement && layoutFor(mode).fullscreen) exitFullscreen();
    });
};

const attachWindowHandlers = () => {
    window.addEventListener('popstate', () => {
        applyRoute(parseViewMode(location.pathname), location.search);
    });

    // A drag fires dozens of resize events; one re-render per frame is enough.
    let resizeFrame = 0;
    window.addEventListener('resize', () => {
        if (resizeFrame) return;
        resizeFrame = requestAnimationFrame(() => {
            resizeFrame = 0;
            if (!layoutFor(mode).fullscreen) return;   // nothing width-dependent in the office view
            renderResults();
            applyTickerSpeed();
        });
    });
};

// -- Start --------------------------------------------------------------------

// The API says which day is "today", so the date box and idle detection follow the data's day.
const syncClock = async () => {
    try {
        health = await api.health();
        el('date-input').value = health.today;
        clockOffsetMs = dayOffsetMs(health.today, Date.now());
    } catch {
        el('date-input').value = new Date().toISOString().slice(0, 10);
    }
};

// Lines arrive over the feed; results are re-fetched because a finished pass moves into the list.
const openLiveFeed = () => api.openLive({
    onMessage: (payload) => {
        if (payload?.type === 'lanes') applyLanes(payload.lanes);
        loadResults();
    },
    onStateChange: liveState,
});

const start = async () => {
    renderStaticText();
    attachToolbarHandlers();
    attachFullscreenHandlers();
    attachWindowHandlers();
    applyMode(parseViewMode(location.pathname));

    await syncClock();
    await Promise.all([loadLanes(), loadResults()]);
    showExposureWarning();

    // A line frees up purely through time, so sweep even when the device sends nothing.
    setInterval(renderLines, IDLE_SWEEP_MS);
    openLiveFeed();
};

start();
