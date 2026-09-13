// Reading the generated OpenAPI document for the docs page. Pure — no DOM.

// A short type label for a parameter schema: "integer", "string date", "Shot[]", "Club".
export const typeOf = (schema) => {
    if (!schema) return '';
    if (schema.type === 'array') return `${typeOf(schema.items)}[]`;
    if (schema.$ref) return schema.$ref.split('/').pop();
    return [schema.type, schema.format].filter(Boolean).join(' ');
};

// Tags are numbered ("1 · Live"); a plain string compare would file "10 · …" before "2 · …".
export const groupByTag = (spec) => {
    const groups = new Map();

    for (const [path, methods] of Object.entries(spec?.paths ?? {})) {
        for (const [method, operation] of Object.entries(methods)) {
            const tag = operation.tags?.[0] ?? '';
            if (!groups.has(tag)) groups.set(tag, []);
            groups.get(tag).push({ path, method, operation });
        }
    }

    return [...groups.entries()]
        .sort(([left], [right]) => left.localeCompare(right, undefined, { numeric: true }));
};

// The try box sends the bearer token with whatever is typed; it must not leave this origin's API.
export const isProbeAllowed = (input, origin) => {
    let url;
    try {
        url = new URL(String(input ?? ''), origin);
    } catch {
        return false;
    }

    if (url.origin !== origin) return false;
    return url.pathname.startsWith('/api/') || url.pathname.startsWith('/openapi/');
};
