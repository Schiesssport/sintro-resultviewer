// =============================================================================
// Reconnect backoff for the live feed. Pure — no timers, no sockets.
//
// A wall display is expected to run unattended for a whole shooting day, so it has to survive
// the API restarting, the network blinking, or a laptop lid closing. It reconnects on its own;
// the server pushes current state on connect, so the view recovers fully without a reload.
// =============================================================================

export const INITIAL_RETRY_MS = 1000;

/** Long enough that a stopped API is not hammered, short enough that nobody waits for a TV. */
export const MAX_RETRY_MS = 15000;

/** Doubles up to the ceiling. Called after each failed attempt. */
export const nextRetryDelay = (current) => {
    const previous = Number.isFinite(current) && current > 0 ? current : INITIAL_RETRY_MS;
    return Math.min(previous * 2, MAX_RETRY_MS);
};
