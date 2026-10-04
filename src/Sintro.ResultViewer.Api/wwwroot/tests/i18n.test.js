import { test, describe } from 'node:test';
import assert from 'node:assert/strict';

import { readFileSync, readdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

import { TRANSLATIONS, DEFAULT_LANGUAGE, DOCS_LANGUAGES, translate } from '../core/i18n.js';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const source = (file) => readFileSync(join(root, file), 'utf8');

// Every key the markup and the DOM layer name literally; dynamic keys are listed by hand.
const keysInFiles = (files) => {
    const keys = new Set();

    for (const file of files) {
        const text = source(file);
        for (const match of text.matchAll(/\bt\('([a-zA-Z0-9_.+-]+)'/g)) keys.add(match[1]);
        for (const match of text.matchAll(/data-i18n(?:-[a-z-]+)?="([a-zA-Z0-9_.+-]+)"/g)) keys.add(match[1]);
    }

    return keys;
};

const pageFiles = () => [
    ...readdirSync(root).filter((f) => /\.(js|html)$/.test(f)),
    ...readdirSync(join(root, 'core')).map((f) => `core/${f}`),
];

const keysInUse = () => {
    const keys = keysInFiles(pageFiles());

    // Built from state: t(`live.${state}`), t(display.reasonKey), t(`fullscreen.mode.${target}`).
    for (const key of [
        'live.connected', 'live.connecting', 'live.offline',
        'total.mixedValuation', 'total.unknownValuation',
        'fullscreen.mode.live+results', 'fullscreen.mode.live', 'fullscreen.mode.results',
    ]) keys.add(key);

    return keys;
};

describe('translate', () => {
    test('substitutes named placeholders', () => {
        assert.equal(
            translate(TRANSLATIONS.de, 'shooter.unidentified', { lane: 3, time: '19:05' }),
            'Linie 3 · 19:05');
    });

    test('returns the key itself when it is missing, so gaps are visible', () => {
        assert.equal(translate(TRANSLATIONS.de, 'nope.missing'), 'nope.missing');
    });

    test('leaves a placeholder in place when no value was supplied', () => {
        assert.equal(translate(TRANSLATIONS.de, 'msg.error'), 'Resultate konnten nicht geladen werden: {detail}');
    });

    test('substitutes every occurrence and coerces numbers', () => {
        assert.equal(translate({ k: '{n}/{n}' }, 'k', { n: 4 }), '4/4');
    });
});

describe('dictionaries', () => {
    test('German is the default', () => {
        assert.equal(DEFAULT_LANGUAGE, 'de');
        assert.ok(TRANSLATIONS[DEFAULT_LANGUAGE]);
    });

    test('French covers exactly the same keys as German', () => {
        // A missing key silently renders as the raw key in the UI.
        const de = Object.keys(TRANSLATIONS.de).sort();
        const fr = Object.keys(TRANSLATIONS.fr).sort();
        assert.deepEqual(fr, de);
    });

    test('English covers exactly the keys the docs page uses', () => {
        const docsKeys = [...keysInFiles(['docs.html', 'docs.js'])].sort();
        assert.deepEqual(Object.keys(TRANSLATIONS.en).sort(), docsKeys);
        assert.deepEqual(DOCS_LANGUAGES, ['de', 'fr', 'en']);
    });

    test('no translation is left empty', () => {
        for (const [language, dictionary] of Object.entries(TRANSLATIONS)) {
            for (const [key, value] of Object.entries(dictionary)) {
                assert.ok(value.trim().length > 0, `${language}.${key} is empty`);
            }
        }
    });

    test('placeholders match between languages', () => {
        const placeholders = (text) => (text.match(/\{(\w+)\}/g) ?? []).sort();

        for (const [key, german] of Object.entries(TRANSLATIONS.de)) {
            for (const language of ['fr', 'en']) {
                if (!(key in TRANSLATIONS[language])) continue;
                assert.deepEqual(
                    placeholders(TRANSLATIONS[language][key]), placeholders(german),
                    `placeholders differ for ${language}.${key}`);
            }
        }
    });

    test('a program is a "Stich" and a series a "Passe" in German', () => {
        // Range vocabulary: the Stich is what a shooter shoots once, its series are Passen.
        const german = Object.values(TRANSLATIONS.de).join(' ');
        assert.ok(/Stich/.test(german), 'expected the German UI to say "Stich"');
        assert.ok(german.includes('Passe'), 'expected the German UI to say "Passe"');
    });

    test('every key the UI names exists, and every key that exists is named somewhere', () => {
        // A missing key renders as itself on screen; an orphaned one drifts out of date unseen.
        const used = keysInUse();
        const defined = new Set(Object.keys(TRANSLATIONS.de));

        assert.deepEqual([...used].filter((key) => !defined.has(key)), [], 'used but not defined');
        assert.deepEqual([...defined].filter((key) => !used.has(key)), [], 'defined but never used');
    });
});

describe('translation attributes', () => {
    const ATTRIBUTES = ['data-i18n', 'data-i18n-placeholder', 'data-i18n-title', 'data-i18n-aria-label'];

    test('every page applies translations through the shared helper', () => {
        for (const file of ['app.js', 'browse.js', 'docs.js']) {
            assert.match(source(file), /import \{[^}]*\bapplyTranslations\b[^}]*\} from '\.\/dom\.js'/, `${file} must use dom.js`);
            assert.doesNotMatch(source(file), /data-i18n/, `${file} must not walk data-i18n itself`);
        }
    });

    test('the markup uses only attributes the helper handles', () => {
        for (const file of ['index.html', 'browse.html', 'docs.html']) {
            for (const [attribute] of source(file).matchAll(/data-i18n(?:-[a-z-]+)?(?==)/g)) {
                assert.ok(ATTRIBUTES.includes(attribute), `${file}: ${attribute}`);
            }
        }
    });
});
