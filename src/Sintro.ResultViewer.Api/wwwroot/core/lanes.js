// =============================================================================
// When a line stops counting as occupied. Pure — takes "now" as an argument.
// =============================================================================

/** After the device wrote an end total, the line stays shown briefly, then frees up. */
export const IDLE_AFTER_FINISH_MS = 5 * 60 * 1000;

/** No end total (the device does not always write one), so fall back to the last shot. */
export const IDLE_AFTER_LAST_SHOT_MS = 6 * 60 * 1000;

/**
 * How long a line keeps showing its last program after the device has cleared it.
 *
 * The device drops Lanes.ProgramID the moment it writes the end marker, so without this the
 * result would vanish from the line the instant the last shot lands and reappear in the list
 * below — before anyone at the firing point has read it.
 */
export const HOLD_AFTER_CLEAR_MS = 30 * 1000;

const parse = (iso) => {
    const value = Date.parse(iso ?? '');
    return Number.isFinite(value) ? value : null;
};

/** Timestamp of the most recent shot on a program, sighting shots included. */
export const lastActivityAt = (program) => {
    const times = [...(program?.series ?? []), ...(program?.sighting ?? [])]
        .flatMap((series) => series.shots ?? [])
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
 * Applies the hold above to a fresh lane snapshot.
 *
 * `memory` is what each line last showed (Map: lane number → { program, clearedAt }); the
 * returned memory replaces it. A line reporting a program shows that program and forgets any
 * hold. A line reporting nothing keeps showing what it had for HOLD_AFTER_CLEAR_MS from the
 * first empty snapshot, then frees up. The pass may already be in the result list meanwhile;
 * that is fine — the hold is about the firing point, not about where the result is listed.
 */
export const holdClearedLines = (memory, lanes, nowMs) => {
    const next = new Map();

    const shown = (lanes ?? []).map((lane) => {
        const program = lane.currentProgram ?? null;

        if (program) {
            next.set(lane.number, { program, clearedAt: null });
            return { ...lane, currentProgram: program };
        }

        const previous = memory?.get(lane.number);
        if (!previous?.program) return { ...lane, currentProgram: null };

        const clearedAt = previous.clearedAt ?? nowMs;
        if (nowMs - clearedAt >= HOLD_AFTER_CLEAR_MS) return { ...lane, currentProgram: null };

        next.set(lane.number, { program: previous.program, clearedAt });
        return { ...lane, currentProgram: previous.program };
    });

    return { lanes: shown, memory: next };
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
