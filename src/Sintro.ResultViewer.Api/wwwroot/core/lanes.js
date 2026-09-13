// =============================================================================
// When a line stops counting as occupied. Pure — takes "now" as an argument.
// =============================================================================

/** After the device wrote an end total, the line stays shown briefly, then frees up. */
export const IDLE_AFTER_FINISH_MS = 5 * 60 * 1000;

/** No end total (the device does not always write one), so fall back to the last shot. */
export const IDLE_AFTER_LAST_SHOT_MS = 6 * 60 * 1000;

const parse = (iso) => {
    const value = Date.parse(iso ?? '');
    return Number.isFinite(value) ? value : null;
};

/** Timestamp of the most recent shot on a program, sighting shots included. */
export const lastActivityAt = (program) => {
    const times = [
        ...(program?.series ?? []).flatMap((series) => series.shots ?? []),
        ...(program?.sighting?.shots ?? []),
    ]
        .map((shot) => parse(shot?.at))
        .filter((value) => value !== null);

    return times.length === 0 ? null : Math.max(...times);
};

/**
 * Whether the line should read as available.
 *
 * Derived from the data every time rather than latched by a timer, which is what makes a
 * long interruption work: if the range breaks for weather and the same shooter resumes an
 * hour later, the next shot moves lastActivityAt forward and the line simply fills again
 * with everything already shot. Nothing has to be un-expired.
 */
export const isLineAvailable = (program, nowMs) => {
    if (!program) return true;

    const finished = parse(program.finishedAt);
    if (finished !== null) return nowMs - finished >= IDLE_AFTER_FINISH_MS;

    const last = lastActivityAt(program);

    // Loaded but nothing fired yet: the shooter is setting up, not gone.
    if (last === null) return false;

    return nowMs - last >= IDLE_AFTER_LAST_SHOT_MS;
};

/**
 * Offset between the wall clock and the day the data is from.
 *
 * Only ever non-zero in development, where ReferenceDate pins "today" to an old backup.
 * Without it every line on the demo data reads as available, because its last shot is
 * weeks old. Production has no reference date, so this is exactly zero.
 */
export const dayOffsetMs = (referenceDate, nowMs) => {
    const reference = parse(`${referenceDate}T00:00:00`);
    if (reference === null) return 0;

    const today = new Date(nowMs);
    const realMidnight = new Date(today.getFullYear(), today.getMonth(), today.getDate()).getTime();

    return reference - realMidnight;
};
