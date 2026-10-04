// Every filter is an API query, so the rows are always current; only the row shape and the column sort are decided here.

import { TRANSLATIONS, DEFAULT_LANGUAGE, translate } from './core/i18n.js';
import { escapeHtml } from './core/format.js';
import { messageRow, browseRowHtml } from './core/markup.js';
import { parseList, buildRows, sortRows, filterShooters, summarize, filterByTotal, parseBound, exportText, exportFileName, EXPORT_COLUMNS, DEFAULT_EXPORT_COLUMNS } from './core/browse.js';
import { applyTranslations, readToday } from './dom.js';
import { SintroApi } from './api.js';

const RELOAD_DEBOUNCE_MS = 400;

const api = new SintroApi(window.SINTRO_TOKEN);
const el = (id) => document.getElementById(id);

let language = DEFAULT_LANGUAGE;
const t = (key, params) => translate(TRANSLATIONS[language], key, params);

let programs = [];
// The same order as the live view's result list.
let sort = { column: 'at', direction: 'desc' };
let currentRows = [];
let loadSequence = 0;
let reloadTimer = null;

const renderStaticText = () => {
    document.documentElement.lang = language;
    document.title = t('browse.title');
    applyTranslations(t);
};

const query = () => ({
    from: el('from-input').value,
    to: el('to-input').value,
    targetCode: parseList(el('target-code-input').value).join(','),
    matchCode: parseList(el('match-code-input').value).join(','),
    license: parseList(el('license-input').value).join(','),
});

// The order select encodes "<series order>-<shot order>", each time (as shot) or value (best first).
const options = () => {
    const [seriesOrder, shotOrder] = el('order-select').value.split('-');
    return { groupBy: el('group-select').value, detail: el('detail-select').value, seriesOrder, shotOrder };
};

const browseMessage = (text) => messageRow(text, document.querySelectorAll('table.browse colgroup col').length);

const renderSortMarkers = () => {
    for (const th of document.querySelectorAll('th[data-sort]')) {
        const active = th.dataset.sort === sort.column;
        th.classList.toggle('is-sorted', active);
        th.setAttribute('aria-sort', active ? (sort.direction === 'asc' ? 'ascending' : 'descending') : 'none');
    }
};

const render = () => {
    const bounds = { min: parseBound(el('total-min-input').value), max: parseBound(el('total-max-input').value) };
    const rows = sortRows(filterByTotal(buildRows(programs, options()), bounds), sort.column, sort.direction);
    currentRows = rows;

    const bounded = bounds.min !== null || bounds.max !== null;
    el('result-count').textContent = t('browse.count', summarize(programs)) + (bounded ? ` · ${t('browse.rowCount', { rows: rows.length })}` : '');
    el('results-body').innerHTML = rows.length ? rows.map(browseRowHtml).join('') : browseMessage(t('browse.empty'));
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
        el('results-body').innerHTML = browseMessage(t('msg.error', { detail: error.message }));
    }
};

const SHOOTER_LIST_LIMIT = 200;

let shooters = [];
let selectedLicenses = new Set();

const shooterRowHtml = (shooter) => `
    <label class="shooter-row">
        <input type="checkbox" value="${escapeHtml(shooter.license)}" ${selectedLicenses.has(shooter.license) ? 'checked' : ''}>
        <span>${escapeHtml(`${shooter.lastName} ${shooter.firstName}`.trim())}
            <span class="shooter-row-club">${escapeHtml(shooter.club?.name ?? '')}</span></span>
        <span class="shooter-row-license">${escapeHtml(shooter.license)}</span>
    </label>`;

const renderShooterList = () => {
    const matching = filterShooters(shooters ?? [], el('shooter-search').value);
    const shown = matching.slice(0, SHOOTER_LIST_LIMIT);
    const more = matching.length - shown.length;

    el('shooter-list').innerHTML = shown.length === 0
        ? `<p class="message">${escapeHtml(t('browse.empty'))}</p>`
        : shown.map(shooterRowHtml).join('') +
          (more > 0 ? `<p class="message">${escapeHtml(t('browse.moreShooters', { count: more }))}</p>` : '');
    el('shooter-selection').textContent = t('browse.selected', { count: selectedLicenses.size });
};

const openShooterDialog = async () => {
    selectedLicenses = new Set(parseList(el('license-input').value));
    el('shooter-search').value = '';
    el('shooter-dialog').showModal();
    await loadShooters();
};

// The list follows the dialog's own switch: everyone registered, or only those who shot in the date window.
const loadShooters = async () => {
    el('shooter-list').innerHTML = `<p class="message">${escapeHtml(t('browse.loading'))}</p>`;
    const windowed = el('shooter-window').checked;
    try {
        shooters = await api.allShooters(windowed ? { from: el('from-input').value, to: el('to-input').value } : {});
        renderShooterList();
        el('shooter-search').focus();
    } catch (error) {
        el('shooter-list').innerHTML = `<p class="message">${escapeHtml(t('msg.error', { detail: error.message }))}</p>`;
    }
};

const onShooterToggle = (event) => {
    const box = event.target.closest('input[type=checkbox]');
    if (!box) return;
    if (box.checked) selectedLicenses.add(box.value); else selectedLicenses.delete(box.value);
    el('shooter-selection').textContent = t('browse.selected', { count: selectedLicenses.size });
};

const applyShooterSelection = () => {
    el('license-input').value = [...selectedLicenses].join(', ');
    el('shooter-dialog').close();
    load();
};

// Exports exactly the rows on screen: current filters, grouping, order and column sort.

const exportColumnLabels = () => ({
    time: t('browse.time'), license: t('browse.license'), shooter: t('col.shooter'), club: t('col.club'),
    targetCode: t('browse.targetCode'), matchCode: t('browse.matchCode'), total: t('col.total'), shots: t('col.shots'),
    shotsBySeries: t('browse.shotsBySeries'), shotsByShot: t('browse.shotsByShot'),
    series: t('browse.group.series'), shot: t('browse.group.shot'),
});

const selectedText = (id) => el(id).selectedOptions[0]?.textContent ?? '';
const orNone = (text) => text || '–';

const exportSummary = () => [
    [t('browse.dateRange'), `${el('from-input').value} – ${el('to-input').value}`],
    [t('browse.targetCode'), orNone(el('target-code-input').value)],
    [t('browse.matchCode'), orNone(el('match-code-input').value)],
    [t('browse.license'), orNone(el('license-input').value)],
    [t('browse.groupBy'), selectedText('group-select')],
    [t('browse.order'), selectedText('order-select')],
    [t('browse.totalBetween'), orNone([el('total-min-input').value, el('total-max-input').value].filter(Boolean).length ? `${orNone(el('total-min-input').value)} – ${orNone(el('total-max-input').value)}` : '')],
    [t('browse.detail'), selectedText('detail-select')],
];

const openExportDialog = () => {
    const labels = exportColumnLabels();
    el('export-summary').innerHTML = exportSummary()
        .map(([label, value]) => `<dt>${escapeHtml(label)}</dt><dd>${escapeHtml(value)}</dd>`).join('');
    el('export-columns').innerHTML = EXPORT_COLUMNS.map((column) => `
        <label class="export-column">
            <input type="checkbox" value="${column}" ${DEFAULT_EXPORT_COLUMNS.includes(column) ? 'checked' : ''}>
            <span>${escapeHtml(labels[column])}</span>
        </label>`).join('');
    el('export-status').textContent = '';
    el('export-dialog').showModal();
};

const exportColumns = () => [...el('export-columns').querySelectorAll('input:checked')].map((box) => box.value);

const buildExport = (delimiter) => exportText(currentRows, exportColumns(), exportColumnLabels(), delimiter);

const copyExport = async () => {
    try {
        await navigator.clipboard.writeText(buildExport('\t'));
        el('export-status').textContent = t('fullscreen.copied');
    } catch {
        el('export-status').textContent = t('fullscreen.copyFailed');
    }
};

// Semicolon-separated with a BOM, so a Swiss Excel opens it in columns with the umlauts intact.
const downloadExport = () => {
    const blob = new Blob(['\uFEFF', buildExport(';')], { type: 'text/csv;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = exportFileName(new Date(), parseList(el('match-code-input').value), parseList(el('target-code-input').value));
    link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
    el('export-dialog').close();
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
        : { column, direction: column === 'total' || column === 'at' ? 'desc' : 'asc' };
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
    for (const id of ['total-min-input', 'total-max-input']) el(id).addEventListener('input', render);
    document.querySelector('thead').addEventListener('click', onSortClick);
    el('shooter-dialog-button').addEventListener('click', openShooterDialog);
    el('export-button').addEventListener('click', openExportDialog);
    el('export-cancel').addEventListener('click', () => el('export-dialog').close());
    el('export-copy').addEventListener('click', copyExport);
    el('export-download').addEventListener('click', downloadExport);
    el('shooter-search').addEventListener('input', renderShooterList);
    el('shooter-window').addEventListener('change', loadShooters);
    for (const button of document.querySelectorAll('[data-clear]')) button.addEventListener('click', () => {
        el(button.dataset.clear).value = '';
        load();
    });
    el('shooter-list').addEventListener('change', onShooterToggle);
    el('shooter-cancel').addEventListener('click', () => el('shooter-dialog').close());
    el('shooter-apply').addEventListener('click', applyShooterSelection);
    el('language-select').addEventListener('change', (event) => {
        language = event.target.value;
        renderStaticText();
        render();
    });

    const today = await readToday(api);
    el('from-input').value = today;
    el('to-input').value = today;
    load();
};

init();
