export const escapeHtml = (value) => String(value ?? '').replace(/[&<>"']/g, (char) => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;',
}[char]));

// Read off the text, not through Date: a tablet in the wrong timezone must not shift range times.
export const formatTime = (iso) => {
    const match = /T(\d{2}):(\d{2})/.exec(String(iso ?? ''));
    return match ? `${match[1]}:${match[2]}` : '';
};

const pad = (n) => String(n).padStart(2, '0');

// Local day: toISOString is UTC, which is behind local time, so it names the previous day shortly after midnight.
export const localIsoDate = (date) => `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;

// "19.09. 17:52:08", read off the ISO text.
export const formatDateTime = (iso) => {
    const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}:\d{2}:\d{2})/.exec(String(iso ?? ''));
    return match ? `${match[3]}.${match[2]}. ${match[4]}` : '';
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

// In the sport, 4er and 5er results are added together (an A5 series and a B4 series make one result);
// every other scale stands alone, and a series whose scale the device never recorded does too.
const SCALES_SUMMED_TOGETHER = [4, 5];

const scaleFamily = (valuation) =>
    SCALES_SUMMED_TOGETHER.includes(valuation) ? SCALES_SUMMED_TOGETHER.join('/') : String(valuation);

// Derived from series[], the detailed data: one { label, value } per scale family, in the order first shot.
export const resultTotals = (program) => {
    const groups = new Map();
    for (const series of program?.series ?? []) {
        const key = scaleFamily(series.valuation);
        const group = groups.get(key) ?? { labels: [], value: 0 };
        if (!group.labels.includes(series.targetType)) group.labels.push(series.targetType);
        group.value += series.subtotal ?? 0;
        groups.set(key, group);
    }
    return [...groups.values()].map((group) => ({ label: group.labels.join('/'), value: group.value }));
};

// The bare number when one scale was shot; "A10 87 · A100 173" when scales that do not add were mixed.
export const resultText = (program) => {
    const totals = resultTotals(program);
    if (totals.length === 0) return '';
    if (totals.length === 1) return String(totals[0].value);
    return totals.map((total) => `${total.label} ${total.value}`).join(' · ');
};

// On a live lane only the scale being shot right now matters: the family of the last series.
export const activeTotal = (program) => {
    const last = (program?.series ?? []).at(-1);
    if (!last) return null;
    const family = scaleFamily(last.valuation);
    return (program.series ?? [])
        .filter((series) => scaleFamily(series.valuation) === family)
        .reduce((sum, series) => sum + (series.subtotal ?? 0), 0);
};

export const matchesFilter = (program, query, labelText = '') => {
    const terms = String(query ?? '').trim().toLowerCase().split(/\s+/).filter(Boolean);
    if (terms.length === 0) return true;

    const haystack = [
        program.targetTitle,
        program.targetCode,
        program.lane,
        labelText,
        program.shooter?.license,
        program.shooter?.club?.name,
        (program.series ?? []).flatMap((series) => series.shots ?? []).map((shot) => shot.value).join(' '),
    ].filter((part) => part !== null && part !== undefined).join(' ').toLowerCase();

    return terms.every((term) => haystack.includes(term));
};

// "L" is for Linie / ligne, the device's term.
const whenAndWhere = (program) => {
    const time = formatTime(program?.startedAt);
    const line = program?.lane === null || program?.lane === undefined ? '' : `L${program.lane}`;
    return [time, line].filter(Boolean).join('/');
};

// Time and line ride along instead of taking columns.
export const programLabel = (program) => {
    const name = program?.targetTitle ?? '';
    const context = whenAndWhere(program);
    return context ? `${name} (${context})` : name;
};

export const laneContext = (program) =>
    [program?.shooter?.club?.name, program?.targetTitle].filter(Boolean).join(' · ');

export const tickerEntry = (program, t) => {
    const name = shooterLabel(program, t).text;
    const total = resultText(program) || '–';
    const context = [program?.targetTitle, whenAndWhere(program)].filter(Boolean).join(', ');

    return context ? `${name}: ${total} (${context})` : `${name}: ${total}`;
};

// A mouche is not marked: on 5er and 4er targets a glyph in place of the single digit reads as a zero.
// On a 100er target the fine value is the ring value itself, so showing it twice would only confuse.
export const shotGroups = (program) => (program.series ?? []).map((series) => {
    const fineIsRing = series.valuation === 100;
    return {
        code: series.targetType ?? '',
        shots: (series.shots ?? []).map((shot) => ({ text: String(shot.value), sector: shot.hitSector ?? null })),
        bestFineValue: fineIsRing ? null : series.bestFineValue ?? null,
        lastFineValue: fineIsRing ? null : series.shots?.at(-1)?.fineValue ?? null,
        subtotal: series.subtotal,
    };
});
