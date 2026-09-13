// =============================================================================
// Thin API client shared by the viewer and the docs page.
// The token comes from the page (substituted server-side per process start), so
// nothing here reads or stores a credential.
// =============================================================================

import { INITIAL_RETRY_MS, nextRetryDelay } from './core/reconnect.js';

export class SintroApi {
    constructor(token) {
        this.token = token;
    }

    get headers() {
        return { Authorization: `Bearer ${this.token}`, Accept: 'application/json' };
    }

    async get(path) {
        const response = await fetch(path, { headers: this.headers });
        const body = await response.text();

        if (!response.ok) {
            throw new Error(this.describeFailure(response.status, body));
        }

        return body ? JSON.parse(body) : null;
    }

    /** Raw variant for the docs page, which shows status and body verbatim. */
    async probe(path) {
        const response = await fetch(path, { headers: this.headers });
        const body = await response.text();
        let pretty = body;

        try {
            pretty = JSON.stringify(JSON.parse(body), null, 2);
        } catch {
            // Not JSON (an error page, say) — show it as it arrived.
        }

        return { status: response.status, statusText: response.statusText, body: pretty };
    }

    describeFailure(status, body) {
        try {
            const parsed = JSON.parse(body);
            if (parsed?.detail) return `${status} — ${parsed.detail}`;
            if (parsed?.error) return `${status} — ${parsed.error}`;
        } catch {
            // Fall through to the bare status.
        }
        return `HTTP ${status}`;
    }

    programs({ date, state, withoutResult = false, limit = 100 } = {}) {
        const query = new URLSearchParams();
        if (date) {
            query.set('from', date);
            query.set('to', date);
        }
        if (state) query.set('state', state);
        if (withoutResult) query.set('withoutResult', 'true');
        query.set('limit', String(limit));
        return this.get(`/api/v2/programs?${query}`);
    }

    lanes() {
        return this.get('/api/v2/live');
    }

    health() {
        return this.get('/api/v2/health');
    }

    /**
     * Opens the live feed. The browser WebSocket API cannot set headers, so the
     * token travels as a query parameter — same value, same validation path.
     */
    openLive({ onMessage, onStateChange }) {
        const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
        const url = `${protocol}//${location.host}/api/v2/live?token=${encodeURIComponent(this.token)}`;

        let socket = null;
        let retryDelay = INITIAL_RETRY_MS;
        let closed = false;

        const connect = () => {
            if (closed) return;
            onStateChange('connecting');
            socket = new WebSocket(url);

            socket.onopen = () => {
                retryDelay = INITIAL_RETRY_MS;
                onStateChange('connected');
            };

            socket.onmessage = (event) => {
                try {
                    onMessage(JSON.parse(event.data));
                } catch {
                    // Ignore a malformed frame rather than tearing down the feed.
                }
            };

            socket.onclose = () => {
                if (closed) return;
                onStateChange('offline');

                // Reconnect for as long as the page is open: a wall display has to survive the
                // API restarting or the network blinking without anyone walking over to it. The
                // server pushes current state on connect, so the view recovers by itself.
                setTimeout(connect, retryDelay);
                retryDelay = nextRetryDelay(retryDelay);
            };

            socket.onerror = () => socket?.close();
        };

        connect();
        return { close: () => { closed = true; socket?.close(); } };
    }
}
