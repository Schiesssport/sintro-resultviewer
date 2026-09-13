// =============================================================================
// Pure presentation logic — no DOM, no fetch, no globals.
// Everything here is unit-tested in ../tests/format.test.js.
// =============================================================================

export const escapeHtml = (value) => String(value ?? '').replace(/[&<>"']/g, (char) => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;',
}[char]));

/** Time of day from an ISO timestamp. The offset in the string is authoritative. */
export const formatTime = (iso, locale = 'de-CH') => {
    if (!iso) return '';
    const parsed = new Date(iso);
    if (Number.isNaN(parsed.getTime())) return '';

    // Read the wall-clock time the API sent rather than the viewer's local zone:
    // a tablet with the wrong timezone must not shift the displayed shooting times.
    const match = /T(\d{2}):(\d{2})/.exec(iso);
    return match ? `${match[1]}:${match[2]}` : parsed.toLocaleTimeString(locale);
};

/**
 * Decides what to call the shooter of a pass.
 *
 * Most passes have no shooter: registering is optional and a barcode can be
 * misread. A result must never become unusable because of that, so this falls
 * back through licence, the device's free-text field, and finally lane + time —
 * which is always present.
 */
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

/**
 * A total exists only when every series used the same ring scale. Adding a 5er
 * series to a 10er one would produce a confident-looking wrong number, so the
 * API withholds it and names the reason instead.
 */
export const totalDisplay = (program) => {
    if (program.total) {
        return { hasTotal: true, value: program.total.value, valuation: program.total.valuation, reasonKey: null };
    }

    const reasonKey = program.totalUnavailable === 'mixedValuation' ? 'total.mixedValuation'
        : program.totalUnavailable === 'unknownValuation' ? 'total.unknownValuation'
        : null;

    return { hasTotal: false, value: null, valuation: null, reasonKey };
};

export const isActive = (program) => program.state === 'active';

/** Every whitespace-separated term must appear somewhere in the row. */
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
        program.shotValuesText,
    ].filter((part) => part !== null && part !== undefined).join(' ').toLowerCase();

    return terms.every((term) => haystack.includes(term));
};

/**
 * The program with its when and where folded in: "Obligatorisches Programm (20:45/L6)".
 *
 * Time and line do not deserve columns of their own in the result list — they only ever
 * matter as context for the program — so they ride along here and free the width for the
 * shots. "L" is for Linie / ligne, the term the device uses for a firing point.
 */
export const programLabel = (program) => {
    const name = program?.name ?? '';
    const time = formatTime(program?.startedAt);
    const line = program?.lane === null || program?.lane === undefined ? '' : `L${program.lane}`;

    const context = [time, line].filter(Boolean).join('/');
    return context ? `${name} (${context})` : name;
};

/**
 * One compact line for the scrolling ticker: "Hans Muster: 31 (Obligatorisches Programm, 20:45/L6)".
 *
 * The ticker trades detail for reach — sixty results instead of the handful a table row
 * allows — so it carries only who, how much, and enough context to place it.
 */
export const tickerEntry = (program, t) => {
    const name = shooterLabel(program, t).text;
    const total = program?.total ? String(program.total.value) : '–';

    const time = formatTime(program?.startedAt);
    const line = program?.lane === null || program?.lane === undefined ? '' : `L${program.lane}`;
    const where = [time, line].filter(Boolean).join('/');
    const context = [program?.name, where].filter(Boolean).join(', ');

    return context ? `${name}: ${total} (${context})` : `${name}: ${total}`;
};

/**
 * How one shot reads in the compact shots column: always the plain ring value.
 *
 * A mouche is deliberately not marked here. On 5er and 4er targets a centre hit scores 5 or 4,
 * and any glyph in place of that single digit reads as a zero.
 */
export const shotText = (shot) => String(shot.value);

/**
 * Groups the counting shots by series for display, e.g. [A10 | 7 6 0 6 | 96].
 *
 * The target code and the best fine value are context and render dimmed; the ring values are
 * the content and stay high-contrast. Sighting shots are excluded — they are not the result.
 */
export const shotGroups = (program) => (program.series ?? []).map((series) => ({
    index: series.index,
    code: series.targetCode ?? '',
    valuation: series.valuation ?? null,
    shots: (series.shots ?? []).map((shot) => ({
        text: shotText(shot),
        sector: shot.hitSector ?? null,
        mouche: Boolean(shot.mouche),
    })),
    bestFineValue: series.bestFineValue ?? null,
    subtotal: series.subtotal,
}));
