// Pure presentation logic — no DOM, no fetch, no globals.

export const escapeHtml = (value) => String(value ?? '').replace(/[&<>"']/g, (char) => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;',
}[char]));

// Read off the text, not through Date: a tablet in the wrong timezone must not shift range times.
export const formatTime = (iso) => {
    const match = /T(\d{2}):(\d{2})/.exec(String(iso ?? ''));
    return match ? `${match[1]}:${match[2]}` : '';
};

// Most passes are anonymous; the chain name → licence → free text → line + time never fails.
export const shooterLabel = (program, t) => {
    const shooter = program.shooter ?? null;

    if (shooter) {
        const name = `${shooter.firstName ?? ''} ${shooter.lastName ?? ''}`.trim();
        if (name) return { text: name, fallback: false, shooter };

        if (shooter.license) {
            return { text: t('shooter.licenseOnly', { license: shooter.license }), fallback: true, shooter };
        }
    }

    if (program.contestShooterName) {
        return { text: program.contestShooterName, fallback: false, shooter };
    }

    return {
        text: t('shooter.unidentified', { lane: program.lane, time: formatTime(program.startedAt) }),
        fallback: true,
        shooter,
    };
};

// A 5er series added to a 10er one is a confident wrong number, so the API withholds it and names why.
export const totalDisplay = (program) => {
    if (program.total) return { hasTotal: true, value: program.total.value, reasonKey: null };

    const reasonKey = program.totalUnavailable === 'mixedValuation' ? 'total.mixedValuation'
        : program.totalUnavailable === 'unknownValuation' ? 'total.unknownValuation'
        : null;

    return { hasTotal: false, value: null, reasonKey };
};

// Every whitespace-separated term must appear somewhere in the row.
export const matchesFilter = (program, query, labelText = '') => {
    const terms = String(query ?? '').trim().toLowerCase().split(/\s+/).filter(Boolean);
    if (terms.length === 0) return true;

    const haystack = [
        program.name,
        program.number,
        program.lane,
        labelText,
        program.shooter?.license,
        program.shooter?.club?.name,
        (program.shotValues ?? []).join(' '),
    ].filter((part) => part !== null && part !== undefined).join(' ').toLowerCase();

    return terms.every((term) => haystack.includes(term));
};

// "20:45/L6" — "L" is for Linie / ligne, the device's term.
const whenAndWhere = (program) => {
    const time = formatTime(program?.startedAt);
    const line = program?.lane === null || program?.lane === undefined ? '' : `L${program.lane}`;
    return [time, line].filter(Boolean).join('/');
};

// "Obligatorisches Programm (20:45/L6)": time and line ride along instead of taking columns.
export const programLabel = (program) => {
    const name = program?.name ?? '';
    const context = whenAndWhere(program);
    return context ? `${name} (${context})` : name;
};

// "SG Muster · Obligatorisches Programm" under a shooter's name on a line.
export const laneContext = (program) =>
    [program?.shooter?.club?.name, program?.name].filter(Boolean).join(' · ');

// "Hans Muster: 31 (Obligatorisches Programm, 20:45/L6)" — who, how much, and where.
export const tickerEntry = (program, t) => {
    const name = shooterLabel(program, t).text;
    const total = program?.total ? String(program.total.value) : '–';
    const context = [program?.name, whenAndWhere(program)].filter(Boolean).join(', ');

    return context ? `${name}: ${total} (${context})` : `${name}: ${total}`;
};

// A mouche is not marked: on 5er and 4er targets a glyph in place of the single digit reads as a zero.
export const shotGroups = (program) => (program.series ?? []).map((series) => ({
    code: series.targetCode ?? '',
    shots: (series.shots ?? []).map((shot) => ({ text: String(shot.value), sector: shot.hitSector ?? null })),
    bestFineValue: series.bestFineValue ?? null,
    lastFineValue: series.shots?.at(-1)?.fineValue ?? null,
    subtotal: series.subtotal,
}));
