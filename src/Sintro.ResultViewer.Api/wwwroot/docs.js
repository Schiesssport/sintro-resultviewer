// =============================================================================
// API browser — renders the generated OpenAPI spec and calls endpoints in place.
// Plain HTML on purpose: no vendored spec-viewer bundle to keep up to date.
// =============================================================================

import { TRANSLATIONS, DEFAULT_LANGUAGE, translate } from './core/i18n.js';
import { escapeHtml } from './core/format.js';
import { typeOf, groupByTag } from './core/openapi.js';
import { SintroApi } from './api.js';

const api = new SintroApi(window.SINTRO_TOKEN);
const language = DEFAULT_LANGUAGE;
const t = (key, params) => translate(TRANSLATIONS[language], key, params);

/** Blob URLs opened for the JSON viewer, revoked when the page goes away. */
const openedBlobs = [];

const parameterTable = (parameters) => {
    if (!parameters?.length) return `<p class="endpoint-note">${t('docs.noParameters')}</p>`;

    const rows = parameters.map((parameter) => `
        <tr>
            <td class="mono">${escapeHtml(parameter.name)}</td>
            <td class="mono">${escapeHtml(typeOf(parameter.schema))}</td>
            <td>${parameter.in === 'path' ? t('docs.inPath') : ''}</td>
            <td>${escapeHtml(parameter.description ?? '')}</td>
        </tr>`).join('');

    return `
        <table class="param-table">
            <thead><tr><th>${t('docs.name')}</th><th>${t('docs.type')}</th><th></th><th></th></tr></thead>
            <tbody>${rows}</tbody>
        </table>`;
};

const hasPathParameter = (path) => path.includes('{');

/**
 * The try-URL keeps its {placeholder} rather than stripping it. Stripping produced
 * "/api/v2/programs/", which quietly behaves like the list endpoint and leaves a
 * newcomer wondering why the detail call returns the same thing.
 */
const endpointCard = (path, method, operation, index) => `
    <section class="endpoint">
        <div class="endpoint-head" data-toggle="${index}">
            <span class="endpoint-method">${method.toUpperCase()}</span>
            <span class="endpoint-path">${escapeHtml(path)}</span>
            <span class="endpoint-summary">${escapeHtml(operation.summary ?? '')}</span>
        </div>
        <div class="endpoint-body hidden" data-body="${index}">
            ${operation.description
                ? `<p class="endpoint-description">${escapeHtml(operation.description)}</p>`
                : ''}
            <h4 class="endpoint-heading">${t('docs.parameters')}</h4>
            ${parameterTable(operation.parameters)}
            <h4 class="endpoint-heading">${t('docs.try')}</h4>
            ${hasPathParameter(path)
                ? `<p class="endpoint-note">${escapeHtml(t('docs.replacePlaceholder'))}</p>`
                : ''}
            <div class="try-row">
                <input type="text" data-url="${index}" value="${escapeHtml(path)}" spellcheck="false">
                <button class="btn-action" data-send="${index}">${t('docs.send')}</button>
                <button class="btn-secondary hidden" data-open="${index}">${t('docs.openInBrowser')}</button>
            </div>
            <div class="response-status hidden" data-status="${index}"></div>
            <pre class="response hidden" data-response="${index}"></pre>
        </div>
    </section>`;

const showResponse = (index, result) => {
    const status = document.querySelector(`[data-status="${index}"]`);
    const output = document.querySelector(`[data-response="${index}"]`);
    const openButton = document.querySelector(`[data-open="${index}"]`);

    const ok = result.status >= 200 && result.status < 300;
    status.className = `response-status ${ok ? 'is-ok' : 'is-error'}`;
    status.textContent = `${result.status} ${result.statusText}`;

    output.classList.remove('hidden');
    output.textContent = result.body;

    openButton.classList.toggle('hidden', !ok);
    openButton.dataset.payload = result.body;
};

/** Hands the response to the browser's own JSON viewer in a new tab. */
const openInBrowser = (payload) => {
    const url = URL.createObjectURL(new Blob([payload], { type: 'application/json' }));
    openedBlobs.push(url);
    window.open(url, '_blank', 'noopener');
};

const render = (spec) => {
    const container = document.getElementById('endpoints');
    let index = 0;

    container.innerHTML = groupByTag(spec).map(([tag, operations]) => `
        <section class="endpoint-group">
            <h3 class="endpoint-group-title">${escapeHtml(tag)}</h3>
            ${operations.map(({ path, method, operation }) =>
                endpointCard(path, method, operation, index++)).join('')}
        </section>`).join('');

    container.addEventListener('click', async (event) => {
        const head = event.target.closest('[data-toggle]');
        if (head) {
            document.querySelector(`[data-body="${head.dataset.toggle}"]`)?.classList.toggle('hidden');
            return;
        }

        const open = event.target.closest('[data-open]');
        if (open) {
            openInBrowser(open.dataset.payload ?? '');
            return;
        }

        const send = event.target.closest('[data-send]');
        if (!send) return;

        const key = send.dataset.send;
        const url = document.querySelector(`[data-url="${key}"]`).value;
        const output = document.querySelector(`[data-response="${key}"]`);

        output.classList.remove('hidden');
        output.textContent = '…';

        showResponse(key, await api.probe(url));
    });
};

const start = async () => {
    document.title = `${t('docs.title')} — ${t('app.title')}`;
    for (const node of document.querySelectorAll('[data-i18n]')) {
        node.textContent = t(node.dataset.i18n);
    }

    window.addEventListener('pagehide', () => {
        for (const url of openedBlobs) URL.revokeObjectURL(url);
    });

    try {
        render(await api.get('/openapi/v2.json'));
    } catch (error) {
        document.getElementById('endpoints').innerHTML =
            `<p class="message">${escapeHtml(t('docs.loadFailed', { detail: error.message }))}</p>`;
    }
};

start();
