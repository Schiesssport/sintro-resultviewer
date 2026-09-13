import { test, describe } from 'node:test';
import assert from 'node:assert/strict';

import { TRANSLATIONS, DEFAULT_LANGUAGE, translate } from '../core/i18n.js';

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
        // A missing key silently renders as the raw key in the UI, so this is
        // the check that keeps the two languages honest.
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
        // A Sintro program is one pass at the target; a Stich is the competition
        // a participant registers for. Mixing the words would confuse both tools.
        const german = Object.values(TRANSLATIONS.de).join(' ');
        assert.ok(german.includes('Passe'), 'expected the German UI to say "Passe"');
        assert.ok(!/Stich/.test(german), 'the German UI must not say "Stich"');
    });
});
