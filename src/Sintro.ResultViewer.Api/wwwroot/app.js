// =============================================================================
// Sintro Result Viewer — app layer. Owns the DOM; all pure logic lives in core/.
// No framework, no build step (same approach as OpenRangeOffice).
// =============================================================================

import { TRANSLATIONS, DEFAULT_LANGUAGE, translate } from './core/i18n.js';
import {
    escapeHtml, shooterLabel, totalDisplay, matchesFilter, shotGroups, programLabel, tickerEntry,
} from './core/format.js';
import { shotDial } from './core/sectors.js';
import { isLineAvailable, dayOffsetMs } from './core/lanes.js';
import { parseViewMode, layoutFor, pathForMode, FULLSCREEN_MODES } from './core/viewmode.js';
import {
    tickerConfig, rowsThatFit, splitForTicker, tickerDurationSeconds, tickerContentKey, tickerQuery,
} from './core/ticker.js';
import { SintroApi } from './api.js';

const RESULT_LIMIT = 100;
const RESULT_COLUMNS = 5;

/** How often availability is re-evaluated when no new data arrives. */
const IDLE_SWEEP_MS = 15_000;

/** Fallback row height before anything has been measured. */
const ESTIMATED_ROW_HEIGHT = 44;

const api = new SintroApi(window.SINTRO_TOKEN);
let ticker = tickerConfig(location.search);

let language = DEFAULT_LANGUAGE;
let programs = [];
let lanes = [];
let filterText = '';
let mode = 'dashboard';
let clockOffsetMs = 0;
let tickerItems = [];
let tickerKey = null;

const t = (key, params) => translate(TRANSLATIONS[language], key, params);
const el = (id) => document.getElementById(id);

/** Wall clock, shifted onto the data's day when a ReferenceDate is pinning development. */
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

/**
 * The direction ring drawn AROUND a shot's ring value: eight wedges, the reported sector
 * filled. Sector 1 is twelve o'clock running clockwise, derived from the stored X/Y data.
 * The number always stays readable in the middle — the ring is context, not a replacement.
 */
const shotRing = (sector) => {
    const dial = shotDial({ hitSector: sector });
    const { cx, cy } = dial.geometry;

    const wedges = dial.wedges.map((wedge) =>
        `<path d="${wedge.ringPath}" class="${wedge.filled ? 'ring-wedge is-hit' : 'ring-wedge'}"/>`)
        .join('');

    // A true circle, never stretched: the dial is a target face, and an ellipse reads wrong.
    const centreClass = dial.isCentre ? ' is-centre' : '';
    return `<svg class="shot-ring${centreClass}" viewBox="0 0 ${cx * 2} ${cy * 2}" aria-hidden="true">${wedges}</svg>`;
};

const shotHtml = (shot, withRing) => {
    const value = `<span class="shot-value">${escapeHtml(shot.text)}</span>`;
    const ring = withRing && shot.sector !== null ? shotRing(shot.sector) : '';

    return `<span class="shot${withRing ? ' is-ringed' : ''}">${ring}${value}</span>`;
};

/** One chip per series: [A10 | 7 6 0 6 | 96]. A chip never breaks internally. */
const shotGroupsCell = (program, { withRings = false } = {}) => {
    const groups = shotGroups(program);
    if (groups.length === 0) return '';

    return `<div class="shot-groups">${groups.map((group) => `
        <span class="shot-group" title="${escapeHtml(t('series.subtotal'))} ${group.subtotal}">
            <span class="shot-group-code">${escapeHtml(group.code)}</span>
            <span class="shot-group-values">${group.shots.map((shot) => shotHtml(shot, withRings)).join('')}</span>
            ${group.bestFineValue === null ? '' :
                `<span class="shot-group-fine" title="${escapeHtml(t('series.bestFine'))}">${group.bestFineValue}</span>`}
        </span>`).join('')}</div>`;
};

// -- Lines --------------------------------------------------------------------

const lineRow = (lane) => {
    const program = lane.currentProgram;
    const available = isLineAvailable(program, now());

    if (!program || available) {
        return `
            <tr class="lane-row is-free">
                <td class="lane-number">${lane.number}</td>
                <td class="lane-free" colspan="3">${escapeHtml(t('lane.available'))}</td>
            </tr>`;
    }

    const { label, html } = shooterName(program);
    const club = program.shooter?.club?.name ?? '';

    // Club and program are context under the name: present, readable, never competing.
    const context = [club, program.name].filter(Boolean).map(escapeHtml).join(' · ');

    return `
        <tr class="lane-row">
            <td class="lane-number">${lane.number}</td>
            <td class="lane-shooter">
                <div class="lane-shooter-name ${label.fallback ? 'is-fallback' : ''}">${html}</div>
                <div class="lane-context">${context}</div>
            </td>
            <td class="lane-total">${totalCell(program)}</td>
            <td class="lane-shots">${shotGroupsCell(program, { withRings: true })}</td>
        </tr>`;
};

const renderLines = () => {
    el('lanes-body').innerHTML = lanes.length === 0
        ? `<tr><td class="message" colspan="4">${escapeHtml(t('msg.noLanes'))}</td></tr>`
        : lanes.map(lineRow).join('');

    // The line-only view spreads the lines over the whole screen, so the row height has to
    // come from how many there are. CSS cannot count rows; this is the one number it needs.
    document.body.style.setProperty('--lane-count', String(Math.max(1, lanes.length)));
};

// -- Results ------------------------------------------------------------------

const programCells = (program) => {
    const { label, html } = shooterName(program);

    return `
        <td class="col-club">${clubCell(program)}</td>
        <td class="col-shooter ${label.fallback ? 'shooter-fallback' : 'shooter-name'}">${html}</td>
        <td class="col-total">${totalCell(program)}</td>
        <td class="col-shots">${shotGroupsCell(program)}</td>
        <td class="col-program">${escapeHtml(programLabel(program))}</td>`;
};

const programRow = (program) => `<tr>${programCells(program)}</tr>`;

const visibleResults = () => programs.filter(
    (program) => matchesFilter(program, filterText, shooterLabel(program, t).text));

/**
 * How many result rows fit in the space the layout gave the table. Only meaningful in
 * fullscreen, where there is nothing to scroll and the overflow has to go to the ticker.
 *
 * The ticker is not subtracted here: the layout already reserves its strip (--ticker-space),
 * so the box being measured excludes it whether or not one is showing. Reserving it always
 * is what keeps this measurement from depending on its own result.
 */
const measureResultCapacity = () => {
    const scroll = el('results-scroll');
    const head = scroll.querySelector('thead');
    const sampleRow = el('results-body').querySelector('tr');

    const rowHeight = sampleRow?.getBoundingClientRect().height || ESTIMATED_ROW_HEIGHT;
    const available = scroll.getBoundingClientRect().height
        - (head?.getBoundingClientRect().height ?? 0);

    return rowsThatFit(available, rowHeight);
};

/**
 * A continuously scrolling ticker rather than a row that swaps: it fits far more results,
 * and a moving line is easier to follow than one that jumps.
 *
 * The content is rendered twice and translated by exactly -50%, which makes the loop
 * seamless — the second copy is in the first copy's place at the moment it restarts.
 */
const renderTicker = () => {
    const bar = el('results-ticker');
    const key = tickerContentKey(tickerItems);

    // Rebuilding restarts the CSS animation, and results reload on every live message —
    // every shot fired anywhere. Without this guard the marquee snaps back to the start
    // several times a minute. Untouched DOM keeps running undisturbed.
    if (key === tickerKey) return;
    tickerKey = key;

    if (tickerItems.length === 0) {
        bar.classList.add('hidden');
        bar.innerHTML = '';
        return;
    }

    // When the content genuinely changed, resume at the same point in the loop rather than
    // jumping to the beginning.
    const elapsed = document.querySelector('.ticker-track')
        ?.getAnimations?.()[0]?.currentTime ?? 0;

    const entries = tickerItems
        .map((item) => `<span class="ticker-entry">${escapeHtml(tickerEntry(item, t))}</span>`)
        .join('');

    bar.classList.remove('hidden');
    bar.innerHTML = `
        <div class="ticker-viewport">
            <div class="ticker-track">
                <span class="ticker-run">${entries}</span>
                <span class="ticker-run" aria-hidden="true">${entries}</span>
            </div>
        </div>`;

    applyTickerSpeed(elapsed);
};

/**
 * Speed is set from measurement, not guessed: each entry should stay legible for the
 * configured number of seconds whatever the screen width or the length of the names.
 */
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

const renderResults = () => {
    const visible = visibleResults();
    const body = el('results-body');

    if (visible.length === 0) {
        body.innerHTML = `<tr><td colspan="${RESULT_COLUMNS}" class="message">${escapeHtml(t('msg.empty'))}</td></tr>`;
        tickerItems = [];
        renderTicker();
    } else if (!layoutFor(mode).fullscreen) {
        // The office view scrolls its own table, so every row stays reachable.
        body.innerHTML = visible.map(programRow).join('');
        tickerItems = [];
        renderTicker();
    } else {
        // Render everything once so a row can be measured, then split and re-render.
        body.innerHTML = visible.map(programRow).join('');

        const split = splitForTicker(visible, measureResultCapacity(), ticker.count);
        body.innerHTML = split.visible.map(programRow).join('');

        tickerItems = split.ticker;
        renderTicker();
    }

    el('result-count').textContent = t('msg.count', { shown: visible.length });
};

const renderStaticText = () => {
    document.documentElement.lang = language;
    document.title = t('app.title');

    for (const node of document.querySelectorAll('[data-i18n]')) {
        node.textContent = t(node.dataset.i18n);
    }
    for (const node of document.querySelectorAll('[data-i18n-placeholder]')) {
        node.placeholder = t(node.dataset.i18nPlaceholder);
    }
};

const renderAll = () => {
    renderLines();
    renderResults();
};

// -- Data ---------------------------------------------------------------------

const loadResults = async () => {
    try {
        // Finished only: a pass still being shot appears on its line above, never twice.
        // The API already returns newest first, so no client-side sorting is needed.
        const page = await api.programs({
            date: el('date-input').value || undefined,
            limit: RESULT_LIMIT,
            state: 'finished',
        });
        programs = page.items;
        renderResults();
    } catch (error) {
        programs = [];
        el('results-body').innerHTML =
            `<tr><td colspan="${RESULT_COLUMNS}" class="message">${escapeHtml(t('msg.error', { detail: error.message }))}</td></tr>`;
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

const showExposureWarning = (health) => {
    if (!health?.publicExposure?.length) return;

    const banner = el('exposure-warning');
    banner.textContent = t('warn.publicAccess', { ranges: health.publicExposure.join(' | ') });
    banner.classList.remove('hidden');
};

const liveState = (state) => {
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

const goTo = (next, { replace = false, query = location.search } = {}) => {
    const url = pathForMode(next) + query;
    if (replace) history.replaceState({}, '', url); else history.pushState({}, '', url);

    // The display settings live in the URL, so navigating re-reads them.
    ticker = tickerConfig(query);
    tickerKey = null;

    applyMode(next);
};

/** The settings the dialog currently shows, which the built URLs carry. */
const pickerTicker = () => ({
    seconds: Number(el('ticker-seconds-input').value) || ticker.seconds,
    count: Number(el('ticker-count-input').value),
});

// Always spelled out in the URL, so what an operator copies is exactly what the display gets.
const absoluteUrlFor = (target) =>
    new URL(pathForMode(target) + tickerQuery(pickerTicker()), location.origin).href;

/**
 * The picker exists because a wall display is usually a *different* screen: opening the view
 * here is only half of it, so every mode also offers its URL for pasting into the TV browser.
 */
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
    goTo(target, { query });

    try {
        // Needs a user gesture, so this only works from the picker — opening a /fullscreen/*
        // URL directly still strips the chrome, which is what a TV actually needs.
        await document.documentElement.requestFullscreen();
    } catch {
        // Denied or unsupported; the chrome-less layout stands on its own.
    }
};

const copyFullscreenUrl = async (target, button) => {
    const url = absoluteUrlFor(target);

    try {
        await navigator.clipboard.writeText(url);
    } catch {
        // Clipboard access needs a secure context; on plain http the operator can still
        // select the URL shown beside the button.
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

const attachHandlers = () => {
    el('filter-input').addEventListener('input', (event) => {
        filterText = event.target.value;
        renderResults();
    });

    el('date-input').addEventListener('change', loadResults);
    el('reload-button').addEventListener('click', loadResults);
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

    window.addEventListener('popstate', () => {
        ticker = tickerConfig(location.search);
        tickerKey = null;
        applyMode(parseViewMode(location.pathname));
    });

    // Leaving browser fullscreen with Esc bypasses the button, so follow its state back.
    document.addEventListener('fullscreenchange', () => {
        if (!document.fullscreenElement && layoutFor(mode).fullscreen) exitFullscreen();
    });

    // A resize changes both how many rows fit and how fast the ticker must run.
    window.addEventListener('resize', () => {
        renderResults();
        applyTickerSpeed();
    });

    el('language-select').addEventListener('change', (event) => {
        language = event.target.value;
        renderStaticText();
        renderAll();
    });
};

const start = async () => {
    renderStaticText();
    attachHandlers();
    applyMode(parseViewMode(location.pathname));

    // Ask the API which day it treats as today, so the date box agrees with the today-only
    // default and idle detection is measured against the data's day, not the wall clock.
    let health = null;
    try {
        health = await api.health();
        el('date-input').value = health.today;
        clockOffsetMs = dayOffsetMs(health.today, Date.now());
    } catch {
        el('date-input').value = new Date().toISOString().slice(0, 10);
    }

    await Promise.all([loadLanes(), loadResults()]);
    showExposureWarning(health);

    // A line frees up purely through the passage of time, so re-render on a slow sweep
    // even when the device sends nothing.
    setInterval(renderLines, IDLE_SWEEP_MS);

    // The live feed carries line state directly; results are re-fetched too because a
    // finished pass leaves the lines and joins the list below.
    api.openLive({
        onMessage: (payload) => {
            if (payload?.type === 'lanes') applyLanes(payload.lanes);
            loadResults();
        },
        onStateChange: liveState,
    });
};

start();
