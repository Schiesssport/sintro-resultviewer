// Result browser. Every filter is an API query, so the rows are always current; only the row shape
// (per pass, series or shot) and the column sort are decided here. State lives in this tab's memory.

import { TRANSLATIONS, DEFAULT_LANGUAGE, translate } from './core/i18n.js';
import { escapeHtml, formatTime } from './core/format.js';
import { parseList, buildRows, sortRows } from './core/browse.js';
import { SintroApi } from './api.js';

const RELOAD_DEBOUNCE_MS = 400;

const api = new SintroApi(window.SINTRO_TOKEN);
const el = (id) => document.getElementById(id);

let language = DEFAULT_LANGUAGE;
const t = (key, params) => translate(TRANSLATIONS[language], key, params);

let programs = [];
let sort = { column: 'total', direction: 'desc' };
let loadSequence = 0;
let reloadTimer = null;

const renderStaticText = () => {
    document.documentElement.lang = language;
    document.title = t('browse.title');

    for (const node of document.querySelectorAll('[data-i18n]')) node.textContent = t(node.dataset.i18n);
    for (const node of document.querySelectorAll('[data-i18n-placeholder]')) node.placeholder = t(node.dataset.i18nPlaceholder);
    for (const node of document.querySelectorAll('[data-i18n-aria-label]')) node.setAttribute('aria-label', t(node.dataset.i18nAriaLabel));
};

const query = () => ({
    from: el('from-input').value,
    to: el('to-input').value,
    targetCode: parseList(el('target-code-input').value).join(','),
    matchCode: parseList(el('match-code-input').value).join(','),
    license: parseList(el('license-input').value).join(','),
});

const options = () => ({
    groupBy: el('group-select').value,
    detail: el('detail-select').value,
    order: el('order-select').value,
});

const groupHtml = (group) => `
        <span class="shot-group">
            <span class="shot-group-code">${escapeHtml(group.code)}</span>
            <span class="shot-group-values">${group.values.map((value) => `<span class="shot">${escapeHtml(value)}</span>`).join('')}</span>
        </span>`;

// Program name and start time ride along as a tooltip: in series and shot mode many rows share a shooter.
const rowHtml = (row) => `
    <tr title="${escapeHtml(`${row.program} ${formatTime(row.startedAt)}`.trim())}">
        <td class="col-license">${escapeHtml(row.license)}</td>
        <td class="col-shooter">${escapeHtml(`${row.lastName} ${row.firstName}`.trim())}</td>
        <td class="col-club">${escapeHtml(row.club)}</td>
        <td class="col-code">${row.targetCode ?? ''}</td>
        <td class="col-code">${escapeHtml(row.matchCode)}</td>
        <td class="col-total">${row.total === null ? '<span class="value-none">–</span>' : row.total}</td>
        <td class="col-shots"><div class="shot-groups">${row.breakdown.map(groupHtml).join('')}</div></td>
    </tr>`;

const message = (text) => `<tr><td colspan="7" class="message">${escapeHtml(text)}</td></tr>`;

const renderSortMarkers = () => {
    for (const th of document.querySelectorAll('th[data-sort]')) {
        const active = th.dataset.sort === sort.column;
        th.classList.toggle('is-sorted', active);
        th.setAttribute('aria-sort', active ? (sort.direction === 'asc' ? 'ascending' : 'descending') : 'none');
    }
};

const render = () => {
    const rows = sortRows(buildRows(programs, options()), sort.column, sort.direction);

    el('result-count').textContent = t('browse.count', { rows: rows.length, programs: programs.length });
    el('results-body').innerHTML = rows.length ? rows.map(rowHtml).join('') : message(t('browse.empty'));
    renderSortMarkers();
};

// A slow earlier response must not overwrite the rows of a later query.
const load = async () => {
    const sequence = ++loadSequence;
    el('result-count').textContent = t('browse.loading');
    try {
        const items = await api.allPrograms(query());
        if (sequence !== loadSequence) return;
        programs = items;
        render();
    } catch (error) {
        if (sequence !== loadSequence) return;
        programs = [];
        el('results-body').innerHTML = message(t('msg.error', { detail: error.message }));
    }
};

const scheduleLoad = () => {
    clearTimeout(reloadTimer);
    reloadTimer = setTimeout(load, RELOAD_DEBOUNCE_MS);
};

const onSortClick = (event) => {
    const th = event.target.closest('th[data-sort]');
    if (!th) return;

    const column = th.dataset.sort;
    sort = sort.column === column
        ? { column, direction: sort.direction === 'asc' ? 'desc' : 'asc' }
        : { column, direction: column === 'total' ? 'desc' : 'asc' };
    render();
};

const init = async () => {
    renderStaticText();

    el('filters').addEventListener('submit', (event) => {
        event.preventDefault();
        load();
    });
    for (const id of ['from-input', 'to-input']) el(id).addEventListener('change', load);
    for (const id of ['target-code-input', 'match-code-input', 'license-input']) el(id).addEventListener('input', scheduleLoad);
    for (const id of ['group-select', 'detail-select', 'order-select']) el(id).addEventListener('change', render);
    document.querySelector('thead').addEventListener('click', onSortClick);
    el('language-select').addEventListener('change', (event) => {
        language = event.target.value;
        renderStaticText();
        render();
    });

    let today;
    try {
        today = (await api.health()).today;
    } catch {
        today = new Date().toISOString().slice(0, 10);
    }
    el('from-input').value = today;
    el('to-input').value = today;
    load();
};

init();
