// Result browser logic: flatten passes to rows per program, series or shot, and sort them. Filtering is the API's job.

// "41, 44" → ['41', '44']; licences lose their leading zeros so 012345 and 12345 agree.
export const parseList = (text, { numeric = false } = {}) =>
    String(text ?? '')
        .split(',')
        .map((part) => part.trim())
        .filter(Boolean)
        .map((part) => (numeric ? part.replace(/^0+(?=\d)/, '') : part));

const countingShots = (program) => (program.series ?? []).flatMap((series) => series.shots ?? []);

// Best first: ring value, then fine value as the tie-break.
const byValueDesc = (a, b) => (b.value - a.value) || ((b.fineValue ?? 0) - (a.fineValue ?? 0));

const orderShots = (shots, order) => (order === 'value' ? [...shots].sort(byValueDesc) : [...shots]);

const shotText = (shot, detail) => String(detail === 'fineValue' ? shot.fineValue ?? '' : shot.value);

const seriesGroup = (series, detail, order) => ({
    code: series.targetType ?? '',
    values: orderShots(series.shots ?? [], order).map((shot) => shotText(shot, detail)),
});

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

const programRow = (program, detail, order) => ({
    key: `p${program.id}`,
    ...shooterColumns(program),
    matchCode: distinctMatchCodes(countingShots(program)),
    total: program.total?.value ?? null,
    breakdown: (program.series ?? []).map((series) => seriesGroup(series, detail, order)),
});

const seriesRows = (program, detail, order) => (program.series ?? []).map((series) => ({
    key: `p${program.id}s${series.index}`,
    ...shooterColumns(program),
    matchCode: distinctMatchCodes(series.shots ?? []),
    total: series.subtotal ?? null,
    breakdown: [seriesGroup(series, detail, order)],
}));

const shotRows = (program) => (program.series ?? []).flatMap((series) => (series.shots ?? []).map((shot) => ({
    key: `p${program.id}s${series.index}n${shot.number}`,
    ...shooterColumns(program),
    matchCode: shot.matchCode === null || shot.matchCode === undefined ? '' : String(shot.matchCode),
    total: shot.value,
    breakdown: [{ code: series.targetType ?? '', values: [String(shot.fineValue ?? '')] }],
})));

// One row per program, series or shot. In shot mode the total is the ring value and the breakdown its fine value.
export const buildRows = (programs, { groupBy = 'program', detail = 'value', order = 'time' } = {}) =>
    programs.flatMap((program) => {
        if (groupBy === 'shot') return shotRows(program);
        if (groupBy === 'series') return seriesRows(program, detail, order);
        return [programRow(program, detail, order)];
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
