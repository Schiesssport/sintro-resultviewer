import { escapeHtml, formatTime, formatDateTime, resultTotals, activeTotal, shotGroups, tickerEntry, shooterLabel } from './format.js';
import { shotDial } from './sectors.js';

const dash = '<span class="value-none">–</span>';

// Each "label value" pair is one unbreakable unit; a narrow column breaks between pairs, never inside.
export const totalsHtml = (totals) => {
    if (totals.length === 0) return dash;
    if (totals.length === 1) return String(totals[0].value);
    return totals.map((total) => `<span class="total-part">${escapeHtml(total.label)} ${total.value}</span>`).join(' ');
};

export const totalCell = (program) => totalsHtml(resultTotals(program));

export const laneTotalCell = (program) => {
    const total = activeTotal(program);
    return total === null ? dash : String(total);
};

export const shooterName = (program, t) => {
    const label = shooterLabel(program, t);
    const duplicate = label.shooter?.duplicateLicense
        ? `<span class="badge badge-dup">${t('badge.duplicateLicense')}</span>`
        : '';

    return { label, html: `${escapeHtml(label.text)}${duplicate}` };
};

export const clubCell = (program) => {
    const name = program.shooter?.club?.name;
    return name ? escapeHtml(name) : '<span class="value-none">–</span>';
};

export const shotRing = (sector) => {
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

const fineValues = (group, withLast, t) => {
    const last = withLast && Number.isFinite(group.lastFineValue)
        ? `<span class="shot-fine-last" title="${escapeHtml(t('series.lastFine'))}">${group.lastFineValue}</span>`
        : '';
    const best = !Number.isFinite(group.bestFineValue) ? ''
        : `<span class="shot-fine-best" title="${escapeHtml(t('series.bestFine'))}">${group.bestFineValue}</span>`;

    return last || best ? `<span class="shot-group-fine">${last}${best}</span>` : '';
};

const shotGroupChip = (group, t, { withRings, withLast }) => `
        <span class="shot-group" title="${escapeHtml(t('series.subtotal'))} ${group.subtotal}">
            <span class="shot-group-code">${escapeHtml(group.code)}</span>
            <span class="shot-group-values">${group.shots.map((shot) => shotHtml(shot, withRings)).join('')}</span>
            ${fineValues(group, withLast, t)}
        </span>`;

export const shotGroupsCell = (program, t, { live = false } = {}) => {
    const groups = shotGroups(program);
    if (groups.length === 0) return '';

    const chips = groups.map((group, index) =>
        shotGroupChip(group, t, { withRings: live, withLast: live && index === groups.length - 1 }));

    return `<div class="shot-groups">${chips.join('')}</div>`;
};

export const messageRow = (text, columnCount) =>
    `<tr><td colspan="${columnCount}" class="message">${escapeHtml(text)}</td></tr>`;

export const tickerRun = (items, t) => {
    const entries = items
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

export const browseGroupHtml = (group) => `
        <span class="shot-group">
            <span class="shot-group-code">${escapeHtml(group.code)}</span>
            <span class="shot-group-values">${group.values.map((value) => `<span class="shot">${escapeHtml(value)}</span>`).join('')}</span>
        </span>`;

// Program name and start time ride along as a tooltip: in series and shot mode many rows share a shooter.
export const browseRowHtml = (row) => `
    <tr title="${escapeHtml(`${row.program} ${formatTime(row.startedAt)}`.trim())}">
        <td class="col-time">${formatDateTime(row.at)}</td>
        <td class="col-license">${escapeHtml(row.license)}</td>
        <td class="col-shooter">${escapeHtml(`${row.lastName} ${row.firstName}`.trim())}</td>
        <td class="col-club">${escapeHtml(row.club)}</td>
        <td class="col-code">${row.targetCode ?? ''}</td>
        <td class="col-code">${escapeHtml(row.matchCode)}</td>
        <td class="col-total">${totalsHtml(row.totals)}</td>
        <td class="col-shots"><div class="shot-groups">${row.breakdown.map(browseGroupHtml).join('')}</div></td>
    </tr>`;
