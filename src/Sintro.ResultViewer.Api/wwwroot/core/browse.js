import { formatDateTime, resultTotals, resultText } from './format.js';

// Licences lose their leading zeros so 012345 and 12345 agree.
export const parseList = (text, { numeric = false } = {}) =>
    String(text ?? '')
        .split(',')
        .map((part) => part.trim())
        .filter(Boolean)
        .map((part) => (numeric ? part.replace(/^0+(?=\d)/, '') : part));

const countingShots = (program) => (program.series ?? []).flatMap((series) => series.shots ?? []);

const byValueDesc = (a, b) => (b.value - a.value) || ((b.fineValue ?? 0) - (a.fineValue ?? 0));

const orderShots = (shots, order) => (order === 'value' ? [...shots].sort(byValueDesc) : [...shots]);

// Equal subtotals keep their shooting order.
const orderSeries = (series, order) =>
    (order === 'value' ? [...series].sort((a, b) => (b.subtotal ?? 0) - (a.subtotal ?? 0)) : [...series]);

const shotText = (shot, detail) => String(detail === 'fineValue' ? shot.fineValue ?? '' : shot.value);

const seriesGroup = (series, detail, order) => ({
    code: series.targetType ?? '',
    values: orderShots(series.shots ?? [], order).map((shot) => shotText(shot, detail)),
});

const lastShotAt = (shots) => shots.at(-1)?.at ?? null;

const distinctMatchCodes = (shots) =>
    [...new Set(shots.map((shot) => shot.matchCode).filter((code) => code !== null && code !== undefined))].join(', ');

const shooterColumns = (program) => ({
    targetCode: program.targetCode ?? null,
    license: program.shooter?.license ?? '',
    lastName: program.shooter?.lastName ?? '',
    firstName: program.shooter?.firstName ?? '',
    club: program.shooter?.club?.name ?? '',
    startedAt: program.startedAt ?? '',
    program: program.targetProgram ?? '',
});

// Sorting and the result range use the first scale's sum; the text shows every scale when they do not add.
const programTotalColumns = (program) => {
    const totals = resultTotals(program);
    return { total: totals[0]?.value ?? null, totalText: totals.length > 1 ? resultText(program) : null };
};

const programRow = (program, detail, seriesOrder, shotOrder) => ({
    key: `p${program.id}`,
    ...shooterColumns(program),
    matchCode: distinctMatchCodes(countingShots(program)),
    at: lastShotAt(countingShots(program)) ?? program.startedAt ?? '',
    ...programTotalColumns(program),
    breakdown: orderSeries(program.series ?? [], seriesOrder).map((series) => seriesGroup(series, detail, shotOrder)),
});

const seriesRows = (program, detail, shotOrder) => (program.series ?? []).map((series) => ({
    key: `p${program.id}s${series.index}`,
    ...shooterColumns(program),
    matchCode: distinctMatchCodes(series.shots ?? []),
    at: lastShotAt(series.shots ?? []) ?? program.startedAt ?? '',
    total: series.subtotal ?? null,
    breakdown: [seriesGroup(series, detail, shotOrder)],
}));

const shotRows = (program) => (program.series ?? []).flatMap((series) => (series.shots ?? []).map((shot) => ({
    key: `p${program.id}s${series.index}n${shot.number}`,
    ...shooterColumns(program),
    matchCode: shot.matchCode === null || shot.matchCode === undefined ? '' : String(shot.matchCode),
    at: shot.at ?? '',
    total: shot.value,
    breakdown: [{ code: series.targetType ?? '', values: [String(shot.fineValue ?? '')] }],
})));

export const buildRows = (programs, { groupBy = 'program', detail = 'value', seriesOrder = 'time', shotOrder = 'time' } = {}) =>
    programs.flatMap((program) => {
        if (groupBy === 'shot') return shotRows(program);
        if (groupBy === 'series') return seriesRows(program, detail, shotOrder);
        return [programRow(program, detail, seriesOrder, shotOrder)];
    });

const compareText = (a, b) => a.localeCompare(b, undefined, { sensitivity: 'base' });

// Totals the API withheld sort last in both directions, so they never pose as the best or the worst.
const compareTotal = (a, b) => {
    if (a === null && b === null) return 0;
    if (a === null) return 1;
    if (b === null) return -1;
    return a - b;
};

const comparators = {
    at: (a, b) => compareText(a.at, b.at),
    targetCode: (a, b) => (a.targetCode ?? 0) - (b.targetCode ?? 0),
    matchCode: (a, b) => compareText(a.matchCode, b.matchCode),
    license: (a, b) => compareText(a.license, b.license),
    name: (a, b) => compareText(a.lastName, b.lastName) || compareText(a.firstName, b.firstName),
    club: (a, b) => compareText(a.club, b.club),
    total: (a, b) => compareTotal(a.total, b.total),
};

export const sortRows = (rows, column, direction = 'asc') => {
    const compare = comparators[column];
    if (!compare) return [...rows];

    const sign = direction === 'desc' ? -1 : 1;
    const nullsLast = (a, b) => (column === 'total' && (a.total === null) !== (b.total === null)
        ? compareTotal(a.total, b.total)
        : sign * compare(a, b));

    return [...rows].sort(nullsLast);
};

const shooterText = (shooter) =>
    [shooter.lastName, shooter.firstName, shooter.license, shooter.club?.name].filter(Boolean).join(' ').toLowerCase();

export const filterShooters = (shooters, query) => {
    const terms = String(query ?? '').trim().toLowerCase().split(/\s+/).filter(Boolean);
    return shooters
        .filter((shooter) => shooter.license)
        .filter((shooter) => terms.every((term) => shooterText(shooter).includes(term)))
        .sort((a, b) => compareText(`${a.lastName} ${a.firstName}`, `${b.lastName} ${b.firstName}`));
};

export const summarize = (programs) => ({
    programs: programs.length,
    series: programs.reduce((sum, program) => sum + (program.series ?? []).length, 0),
    shots: programs.reduce((sum, program) => sum + countingShots(program).length, 0),
});

export const filterByTotal = (rows, { min = null, max = null } = {}) => {
    if (min === null && max === null) return rows;
    return rows.filter((row) => row.total !== null && (min === null || row.total >= min) && (max === null || row.total <= max));
};

export const parseBound = (text) => {
    const trimmed = String(text ?? '').trim();
    return trimmed === '' || Number.isNaN(Number(trimmed)) ? null : Number(trimmed);
};

export const EXPORT_COLUMNS = ['time', 'license', 'shooter', 'club', 'targetCode', 'matchCode', 'total', 'shots', 'shotsBySeries', 'shotsByShot'];
export const DEFAULT_EXPORT_COLUMNS = ['shooter', 'total'];

const exportCell = (row, column) => {
    switch (column) {
        case 'time': return formatDateTime(row.at);
        case 'shooter': return `${row.lastName} ${row.firstName}`.trim();
        case 'total': return row.total === null ? '' : row.totalText ?? String(row.total);
        case 'targetCode': return row.targetCode === null ? '' : String(row.targetCode);
        default: return String(row[column] ?? '');
    }
};

const SHOT_LAYOUTS = { shots: 'single', shotsBySeries: 'series', shotsByShot: 'shot' };

const shotCells = (row, layout, width) => {
    const perSeries = row.breakdown.map((group) => group.values.join(' '));
    const cells = layout === 'shot' ? row.breakdown.flatMap((group) => group.values)
        : layout === 'series' ? perSeries
        : [perSeries.join(' ')];
    return cells.concat(Array(Math.max(0, width - cells.length)).fill(''));
};

const shotWidth = (rows, layout) => {
    if (layout === 'single') return 1;
    const count = (row) => (layout === 'shot' ? row.breakdown.reduce((sum, group) => sum + group.values.length, 0) : row.breakdown.length);
    return Math.max(1, ...rows.map(count));
};

const shotHeaders = (headers, layout, width) => {
    if (layout === 'single') return [headers.shots];
    const base = layout === 'shot' ? headers.shot : headers.series;
    return Array.from({ length: width }, (_, index) => `${base} ${index + 1}`);
};

// A field holding the delimiter, a quote or a line break is quoted, quotes doubled (RFC 4180 style).
const quote = (text, delimiter) =>
    /["\r\n]/.test(text) || text.includes(delimiter) ? `"${text.replace(/"/g, '""')}"` : text;

export const exportText = (rows, columns, headers, delimiter) => {
    const widths = Object.fromEntries(Object.entries(SHOT_LAYOUTS).map(([column, layout]) => [column, shotWidth(rows, layout)]));
    const cells = (row) => columns.flatMap((column) =>
        (column in SHOT_LAYOUTS ? shotCells(row, SHOT_LAYOUTS[column], widths[column]) : [exportCell(row, column)]));
    const head = columns.flatMap((column) =>
        (column in SHOT_LAYOUTS ? shotHeaders(headers, SHOT_LAYOUTS[column], widths[column]) : [headers[column] ?? column]));
    const line = (values) => values.map((value) => quote(String(value), delimiter)).join(delimiter);
    return [line(head)].concat(rows.map((row) => line(cells(row)))).join('\r\n');
};

export const exportFileName = (now, matchCodes, targetCodes) => {
    const pad = (value) => String(value).padStart(2, '0');
    const stamp = [now.getFullYear(), pad(now.getMonth() + 1), pad(now.getDate()), pad(now.getHours()), pad(now.getMinutes()), pad(now.getSeconds())].join('-');
    const part = (codes) => (codes.length ? codes.join('-') : 'alle');
    return `${stamp}_${part(matchCodes)}_${part(targetCodes)}.csv`;
};
