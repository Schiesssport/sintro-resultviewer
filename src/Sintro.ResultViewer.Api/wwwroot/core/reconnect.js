// Reconnect backoff for the live feed. Pure — no timers, no sockets.

export const INITIAL_RETRY_MS = 1000;
// Long enough that a stopped API is not hammered, short enough that nobody waits for a TV.
export const MAX_RETRY_MS = 15000;

export const nextRetryDelay = (current) => {
    const previous = Number.isFinite(current) && current > 0 ? current : INITIAL_RETRY_MS;
    return Math.min(previous * 2, MAX_RETRY_MS);
};
