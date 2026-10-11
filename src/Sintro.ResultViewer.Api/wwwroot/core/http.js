// Every non-2xx body is ApiError {error, detail}; anything else is named by its status.
export const describeFailure = (status, body) => {
    try {
        const parsed = JSON.parse(body);
        if (parsed?.detail) return `${status} — ${parsed.detail}`;
        if (parsed?.error) return `${status} — ${parsed.error}`;
    } catch {
    }
    return `HTTP ${status}`;
};

export const prettyJson = (text) => {
    try {
        return JSON.stringify(JSON.parse(text), null, 2);
    } catch {
        return text;
    }
};
