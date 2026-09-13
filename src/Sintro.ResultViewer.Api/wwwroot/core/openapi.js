// =============================================================================
// Reading the generated OpenAPI document for the docs page. Pure — no DOM.
// =============================================================================

/** A short type label for a parameter schema: "integer", "string date", "Shot[]", "Club". */
export const typeOf = (schema) => {
    if (!schema) return '';
    if (schema.type === 'array') return `${typeOf(schema.items)}[]`;
    if (schema.$ref) return schema.$ref.split('/').pop();
    return [schema.type, schema.format].filter(Boolean).join(' ');
};

/**
 * Groups endpoints by their first OpenAPI tag, in tag order.
 *
 * Tags are numbered ("1 · Live", "2 · Resultate") so the reading order is the one the API
 * author declared. The sort is numeric-aware: a plain string compare would file "10 · …"
 * before "2 · …" the day a tenth group appears.
 */
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

/**
 * Whether the docs page may call a URL with the session token attached.
 *
 * The try box sends the bearer token with whatever is typed. Only this origin's API and
 * schema may receive it; a pasted external address must not carry the token off the LAN.
 */
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
