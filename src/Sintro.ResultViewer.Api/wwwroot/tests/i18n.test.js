import { test, describe } from 'node:test';
import assert from 'node:assert/strict';

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

import { TRANSLATIONS, DEFAULT_LANGUAGE, translate } from '../core/i18n.js';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const source = (file) => readFileSync(join(root, file), 'utf8');

// Every key the markup and the DOM layer name literally; dynamic keys are listed by hand.
const keysInUse = () => {
    const keys = new Set();
    const files = ['app.js', 'docs.js', 'core/format.js', 'index.html', 'docs.html'];

    for (const file of files) {
        const text = source(file);
        for (const match of text.matchAll(/\bt\('([a-zA-Z0-9_.+-]+)'/g)) keys.add(match[1]);
        for (const match of text.matchAll(/data-i18n(?:-[a-z-]+)?="([a-zA-Z0-9_.+-]+)"/g)) keys.add(match[1]);
    }

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
            assert.deepEqual(
                placeholders(TRANSLATIONS.fr[key]), placeholders(german),
                `placeholders differ for ${key}`);
        }
    });

    test('uses "Passe" vocabulary, not OpenRangeOffice\'s "Stich"', () => {
        // A Stich is the competition a participant registers for, not one pass at the target.
        const german = Object.values(TRANSLATIONS.de).join(' ');
        assert.ok(german.includes('Passe'), 'expected the German UI to say "Passe"');
        assert.ok(!/Stich/.test(german), 'the German UI must not say "Stich"');
    });

    test('every key the UI names exists, and every key that exists is named somewhere', () => {
        // A missing key renders as itself on screen; an orphaned one drifts out of date unseen.
        const used = keysInUse();
        const defined = new Set(Object.keys(TRANSLATIONS.de));

        assert.deepEqual([...used].filter((key) => !defined.has(key)), [], 'used but not defined');
        assert.deepEqual([...defined].filter((key) => !used.has(key)), [], 'defined but never used');
    });
});
