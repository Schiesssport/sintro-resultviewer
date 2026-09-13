// When a line stops counting as occupied. Pure — takes "now" as an argument.

export const IDLE_AFTER_FINISH_MS = 5 * 60 * 1000;
// The device does not always write an end total, so the last shot is the fallback signal.
export const IDLE_AFTER_LAST_SHOT_MS = 6 * 60 * 1000;
// The device drops Lanes.ProgramID with the end marker; without a hold the result would vanish unread.
export const HOLD_AFTER_CLEAR_MS = 30 * 1000;

const parse = (iso) => {
    const value = Date.parse(iso ?? '');
    return Number.isFinite(value) ? value : null;
};

// Sighting shots included: the shooter is present either way.
export const lastActivityAt = (program) => {
    const times = [...(program?.series ?? []), ...(program?.sighting ?? [])]
        .flatMap((series) => series.shots ?? [])
        .map((shot) => parse(shot?.at))
        .filter((value) => value !== null);

    return times.length === 0 ? null : Math.max(...times);
};

// Derived from the data every time, never latched: a resumed program simply fills its line again.
export const isLineAvailable = (program, nowMs) => {
    if (!program) return true;

    const finished = parse(program.finishedAt);
    if (finished !== null) return nowMs - finished >= IDLE_AFTER_FINISH_MS;

    const last = lastActivityAt(program);
    if (last === null) return false;   // loaded but nothing fired yet: setting up, not gone

    return nowMs - last >= IDLE_AFTER_LAST_SHOT_MS;
};

// memory: lane number → { program, clearedAt }. A cleared line keeps its program for HOLD_AFTER_CLEAR_MS.
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

// Non-zero only in development, where ReferenceDate pins "today" to an old backup.
export const dayOffsetMs = (referenceDate, nowMs) => {
    const reference = parse(`${referenceDate}T00:00:00`);
    if (reference === null) return 0;

    const today = new Date(nowMs);
    const realMidnight = new Date(today.getFullYear(), today.getMonth(), today.getDate()).getTime();

    return reference - realMidnight;
};
