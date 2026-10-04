// DOM helpers shared by the three pages. Nothing here is pure; core/ stays DOM-free.
import { localIsoDate } from './core/format.js';

const ATTRIBUTE_TARGETS = [
    ['data-i18n', 'i18n', (node, text) => { node.textContent = text; }],
    ['data-i18n-placeholder', 'i18nPlaceholder', (node, text) => { node.placeholder = text; }],
    ['data-i18n-title', 'i18nTitle', (node, text) => { node.title = text; }],
    ['data-i18n-aria-label', 'i18nAriaLabel', (node, text) => node.setAttribute('aria-label', text)],
];

export const applyTranslations = (t, root = document) => {
    for (const [attribute, dataKey, apply] of ATTRIBUTE_TARGETS) {
        for (const node of root.querySelectorAll(`[${attribute}]`)) apply(node, t(node.dataset[dataKey]));
    }
};

export const readToday = async (api) => {
    try {
        return (await api.health()).today;
    } catch {
        return localIsoDate(new Date());
    }
};
