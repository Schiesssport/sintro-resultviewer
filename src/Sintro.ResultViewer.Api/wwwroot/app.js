import { TRANSLATIONS, DEFAULT_LANGUAGE, translate } from './core/i18n.js';
import { escapeHtml, shooterLabel, matchesFilter, programLabel, laneContext, localIsoDate } from './core/format.js';
import {
    totalCell, laneTotalCell, shooterName, clubCell, shotGroupsCell, messageRow, tickerRun,
} from './core/markup.js';
import { isLineAvailable, dayOffsetMs, holdClearedLines } from './core/lanes.js';
import { parseViewMode, layoutFor, pathForMode } from './core/viewmode.js';
import {
    DEFAULT_RESULT_COUNT, parseDisplayQuery, displayQuery,
    tickerDurationSeconds, tickerContentKey,
} from './core/display.js';
import { applyTranslations } from './dom.js';
import { createBoardsDialog } from './boards-dialog.js';
import { SintroApi } from './api.js';

const IDLE_SWEEP_MS = 15_000;

const api = new SintroApi(window.SINTRO_TOKEN);
let display = parseDisplayQuery(location.search);

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
let latestResultsRequest = 0;

const t = (key, params) => translate(TRANSLATIONS[language], key, params);
const el = (id) => document.getElementById(id);

// Read from the markup so a message row's colspan cannot drift from the <colgroup>.
const resultColumns = () => document.querySelectorAll('.results colgroup col').length;

// Wall clock shifted onto the data's day when a ReferenceDate pins development.
const now = () => Date.now() + clockOffsetMs;

const freeLineRow = (lane) => `
            <tr class="lane-row is-free">
                <td class="lane-number">${lane.number}</td>
                <td class="lane-free" colspan="3">${escapeHtml(t('lane.available'))}</td>
            </tr>`;

const occupiedLineRow = (lane, program) => {
    const { label, html } = shooterName(program, t);

    return `
        <tr class="lane-row">
            <td class="lane-number">${lane.number}</td>
            <td class="lane-shooter">
                <div class="lane-shooter-name ${label.fallback ? 'is-fallback' : ''}">${html}</div>
                <div class="lane-context">${escapeHtml(laneContext(program))}</div>
            </td>
            <td class="lane-total">${laneTotalCell(program)}</td>
            <td class="lane-shots">${shotGroupsCell(program, t, { live: true })}</td>
        </tr>`;
};

const lineRow = (lane) => {
    const program = lane.currentProgram;
    return !program || isLineAvailable(program, now()) ? freeLineRow(lane) : occupiedLineRow(lane, program);
};

// The device writes the last shot and clears the line in one step, so the held snapshot lacks that
// shot. Fetch the finished pass and swap it in while the hold lasts.
const refreshHeldProgram = async (laneNumber) => {
    const id = laneMemory.get(laneNumber)?.program?.id;
    if (id === undefined) return;

    try {
        const fresh = await api.program(id);
        const entry = laneMemory.get(laneNumber);
        if (entry?.program?.id !== id) return;   // the line moved on meanwhile

        laneMemory.set(laneNumber, { ...entry, program: fresh });
        renderLines();
    } catch {
        // The stale snapshot stays; the result list shows the final pass anyway.
    }
};

const renderLines = () => {
    // A hold ends purely through time, so recompute on every render, not only on new data.
    const held = holdClearedLines(laneMemory, lanes, now());
    laneMemory = held.memory;
    held.justCleared.forEach(refreshHeldProgram);

    el('lanes-body').innerHTML = lanes.length === 0
        ? `<tr><td class="message" colspan="4">${escapeHtml(t('msg.noLanes'))}</td></tr>`
        : held.lanes.map(lineRow).join('');

    // CSS cannot count rows, and the line-only view spreads them over the whole screen.
    document.body.style.setProperty('--lane-count', String(Math.max(1, lanes.length)));
};

const programRow = (program) => {
    const { label, html } = shooterName(program, t);

    return `<tr>
        <td class="col-club">${clubCell(program)}</td>
        <td class="col-shooter ${label.fallback ? 'shooter-fallback' : 'shooter-name'}">${html}</td>
        <td class="col-total">${totalCell(program)}</td>
        <td class="col-shots">${shotGroupsCell(program, t)}</td>
        <td class="col-program">${escapeHtml(programLabel(program))}</td></tr>`;
};

const messageRowHere = (text) => messageRow(text, resultColumns());

const visibleResults = () => programs.filter(
    (program) => matchesFilter(program, filterText, shooterLabel(program, t).text));

// The box clips what does not fit (overflow hidden), so nothing is measured or guessed.
const renderFullscreenResults = (body, visible) => {
    body.innerHTML = visible.map(programRow).join('');
    return display.hidden ? [] : visible.slice(display.skip);
};

const renderResults = () => {
    const visible = visibleResults();
    const body = el('results-body');

    if (visible.length === 0) {
        body.innerHTML = messageRowHere(t('msg.empty'));
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

// Rebuilding restarts the marquee, and results reload on every live message: hence the key guard.
const renderTicker = () => {
    const bar = el('results-ticker');
    const key = tickerContentKey(tickerItems);
    if (key === tickerKey) return;
    tickerKey = key;

    // Resume at the same point in the loop rather than jumping back to the start.
    const elapsed = document.querySelector('.ticker-track')?.getAnimations?.()[0]?.currentTime ?? 0;

    bar.innerHTML = tickerItems.length === 0 ? '' : tickerRun(tickerItems, t);
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
        secondsVisible: display.seconds,
    });

    track.style.animationDuration = duration > 0 ? `${duration}s` : '';

    const animation = track.getAnimations?.()[0];
    if (animation && duration > 0 && resumeAtMs > 0) {
        animation.currentTime = resumeAtMs % (duration * 1000);
    }
};

const renderStaticText = () => {
    document.documentElement.lang = language;
    document.title = t('app.title');

    applyTranslations(t);

    liveState(liveStatus);
    showExposureWarning();
};

const renderAll = () => {
    renderLines();
    renderResults();
};

const loadResults = async () => {
    const request = ++latestResultsRequest;

    try {
        // Finished only: a running pass shows on its line, never twice. The API sends newest first.
        const page = await api.programs({
            date: el('date-input').value || undefined,
            // One request feeds both the table and the ticker; resultCount sizes it on a display.
            limit: layoutFor(mode).fullscreen ? display.results : DEFAULT_RESULT_COUNT,
            state: 'finished',
        });
        if (request !== latestResultsRequest) return;

        programs = page.items;
        renderResults();
    } catch (error) {
        if (request !== latestResultsRequest) return;

        programs = [];
        tickerItems = [];
        renderTicker();
        el('results-body').innerHTML = messageRowHere(t('msg.error', { detail: error.message }));
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

const applyMode = (next) => {
    mode = next;
    const layout = layoutFor(mode);

    document.body.classList.toggle('is-fullscreen', layout.fullscreen);
    document.body.dataset.mode = mode;

    el('section-lanes').classList.toggle('hidden', !layout.lanes);
    el('section-results').classList.toggle('hidden', !layout.results);
    el('fullscreen-exit').classList.toggle('hidden', !layout.fullscreen);

    renderAll();
};

// The display settings live in the URL, so every navigation re-reads them.
const applyTickerVisibility = () => document.body.classList.toggle('is-ticker-hidden', display.hidden);

const applyRoute = (next, query) => {
    display = parseDisplayQuery(query);
    applyTickerVisibility();
    tickerKey = null;
    applyMode(next);
    loadResults();   // the limit depends on the mode and resultCount
};

const goTo = (next, query = location.search) => {
    history.pushState({}, '', pathForMode(next) + query);
    applyRoute(next, query);
};

const showBoard = async (board) => {
    goTo(board.mode, displayQuery(board));

    try {
        // Needs a user gesture; a /fullscreen/* URL opened directly still drops the chrome.
        await document.documentElement.requestFullscreen();
    } catch {
        // Denied or unsupported; the chrome-less layout stands on its own.
    }
};

const boardsDialog = createBoardsDialog({ t, onShow: showBoard });

const exitFullscreen = async () => {
    goTo('dashboard');
    if (document.fullscreenElement) await document.exitFullscreen();
};

const attachToolbarHandlers = () => {
    el('filter-input').addEventListener('input', (event) => {
        filterText = event.target.value;
        renderResults();
    });
    el('date-input').addEventListener('change', loadResults);

    el('language-select').addEventListener('change', (event) => {
        language = event.target.value;
        tickerKey = null;   // anonymous ticker entries are language-dependent, the key is id-based
        renderStaticText();
        renderAll();
    });
};

const attachFullscreenHandlers = () => {
    el('fullscreen-button').addEventListener('click', boardsDialog.open);
    el('fullscreen-exit').addEventListener('click', exitFullscreen);

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

// The API says which day is "today", so the date box and idle detection follow the data's day.
const syncClock = async () => {
    try {
        health = await api.health();
        el('date-input').value = health.today;
        clockOffsetMs = dayOffsetMs(health.today, Date.now());
    } catch {
        el('date-input').value = localIsoDate(new Date());
    }
};

// Lines arrive over the feed; results are re-fetched because a finished pass moves into the list.
const openLiveFeed = () => api.openLive({
    onMessage: (payload) => {
        if (payload?.lanes) applyLanes(payload.lanes);
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
    applyTickerVisibility();

    await syncClock();
    await Promise.all([loadLanes(), loadResults()]);
    showExposureWarning();

    // A line frees up purely through time, so sweep even when the device sends nothing.
    setInterval(renderLines, IDLE_SWEEP_MS);
    openLiveFeed();
};

start();
