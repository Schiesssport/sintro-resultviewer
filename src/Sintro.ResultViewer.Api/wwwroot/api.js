// Thin API client shared by the viewer and the docs page. The token comes from the page.

import { INITIAL_RETRY_MS, nextRetryDelay } from './core/reconnect.js';
import { isProbeAllowed } from './core/openapi.js';

const openSocket = (url, { onOpen, onMessage, onClose }) => {
    const ws = new WebSocket(url);
    ws.onopen = onOpen;
    ws.onmessage = (event) => {
        try {
            onMessage(JSON.parse(event.data));
        } catch {
            // A malformed frame must not tear down the feed.
        }
    };
    ws.onclose = () => onClose(ws);
    ws.onerror = () => ws.close();
    return ws;
};

const prettyJson = (text) => {
    try {
        return JSON.stringify(JSON.parse(text), null, 2);
    } catch {
        return text;   // not JSON (an error page, say)
    }
};

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

    // Raw status and body for the docs page. Same-origin only: the token must not leave the LAN.
    async probe(path) {
        if (!isProbeAllowed(path, location.origin)) {
            return { status: 0, statusText: 'blocked', body: `Only /api/… and /openapi/… on ${location.origin} can be called from here.` };
        }

        const response = await fetch(path, { headers: this.headers });
        return { status: response.status, statusText: response.statusText, body: prettyJson(await response.text()) };
    }

    describeFailure(status, body) {
        try {
            const parsed = JSON.parse(body);
            if (parsed?.detail) return `${status} — ${parsed.detail}`;
            if (parsed?.error) return `${status} — ${parsed.error}`;
        } catch {
            // Not an ApiError body; fall through to the bare status.
        }
        return `HTTP ${status}`;
    }

    programs({ date, state, limit = 100 }) {
        const query = new URLSearchParams();
        if (date) {
            query.set('from', date);
            query.set('to', date);
        }
        if (state) query.set('state', state);
        query.set('limit', String(limit));
        return this.get(`/api/v2/programs?${query}`);
    }

    lanes() {
        return this.get('/api/v2/live');
    }

    health() {
        return this.get('/api/v2/health');
    }

    // The browser WebSocket API cannot set headers, so the token travels as a query parameter.
    openLive({ onMessage, onStateChange }) {
        const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
        const url = `${protocol}//${location.host}/api/v2/live?token=${encodeURIComponent(this.token)}`;
        let socket = null;
        let retryDelay = INITIAL_RETRY_MS;

        const connect = () => {
            onStateChange('connecting');
            socket = openSocket(url, {
                onOpen: () => {
                    retryDelay = INITIAL_RETRY_MS;
                    onStateChange('connected');
                },
                onMessage,
                // Reconnect for as long as the page is open; the server pushes current state on connect.
                onClose: (ws) => {
                    if (ws !== socket) return;   // a late event from a superseded socket
                    onStateChange('offline');
                    setTimeout(connect, retryDelay);
                    retryDelay = nextRetryDelay(retryDelay);
                },
            });
        };

        connect();
    }
}
