// UI strings: German (default) and French. Code and keys are English.

export const TRANSLATIONS = {
    de: {
        'app.title':                'Sintro Resultate',
        'nav.api':                  'API',

        'live.connected':           'Live',
        'live.connecting':          'Verbinde…',
        'live.offline':             'Keine Verbindung',

        'toolbar.date':             'Datum',
        'toolbar.reload':           'Aktualisieren',
        'toolbar.fullscreen':       'Vollbild',
        'fullscreen.title':         'Vollbild-Ansicht',
        'fullscreen.intro':         'Ansicht hier öffnen, oder die Adresse kopieren und auf dem anzeigenden Gerät aufrufen.',
        'fullscreen.hint':          'Die Adressen lassen sich als Lesezeichen speichern; die Einstellungen sind darin enthalten.',
        'fullscreen.tickerSeconds': 'Lesezeit pro Eintrag (s)',
        'fullscreen.tickerCount':   'Resultate in der Laufschrift',
        'fullscreen.show':          'Anzeigen',
        'fullscreen.copy':          'Link kopieren',
        'fullscreen.copied':        'Kopiert',
        'fullscreen.copyFailed':    'Bitte manuell kopieren',
        'fullscreen.mode.live+results': 'Linien und Resultate',
        'fullscreen.mode.live':     'Nur Linien',
        'fullscreen.mode.results':  'Nur Resultate',
        'toolbar.filterPlaceholder': 'Suchen…',
        'aria.language':            'Sprache',
        'aria.exitFullscreen':      'Vollbild beenden',
        'aria.close':               'Schliessen',

        'col.club':                 'Verein',
        'col.program':              'Programm',
        'col.shooter':              'Schütze',
        'col.total':                'Resultat',
        'col.shots':                'Schüsse',

        'msg.empty':                'Keine Passen an diesem Datum.',
        'msg.error':                'Resultate konnten nicht geladen werden: {detail}',
        'msg.count':                '{shown} Passen',
        'msg.noLanes':              'Keine Linien gemeldet.',
        'section.lanes':            'Linien',
        'section.results':          'Letzte Resultate',
        'lane.available':           'frei',

        'series.subtotal':          'Total',
        'series.bestFine':          'Bester Zehntelwert',
        'series.lastFine':          'Zehntelwert des letzten Schusses',

        'total.mixedValuation':     'Wertung wechselt – kein Gesamttotal',
        'total.unknownValuation':   'Wertung unbekannt – kein Gesamttotal',

        'shooter.unidentified':     'Linie {lane} · {time}',
        'shooter.licenseOnly':      'Lizenz {license}',
        'badge.duplicateLicense':   'Lizenz doppelt',

        'warn.publicAccess':        'ACHTUNG: Öffentlicher Zugriff erlaubt ({ranges}). Schützennamen sind Personendaten.',

        'footer.note':              'Nur-Lese-Ansicht der Sintro-Anlage. Zeiten in lokaler Zeit der Anlage.',

        'docs.title':               'API-Dokumentation',
        'docs.intro':               'Alle Endpunkte sind nur lesend. Datenabfragen benötigen den API-Token; die OpenAPI-Spezifikation und diese Seite nicht. Standardmässig wird nur der heutige Tag geliefert, mit from/to lässt sich der Zeitraum erweitern. Diese Seite verwendet den internen Sitzungstoken, damit du direkt ausprobieren kannst.',
        'docs.parameters':          'Parameter',
        'docs.try':                 'Ausprobieren',
        'docs.send':                'Senden',
        'docs.noParameters':        'Keine Parameter.',
        'docs.specLink':            'OpenAPI-Spezifikation',
        'docs.inPath':              'im Pfad',
        'docs.type':                'Typ',
        'docs.name':                'Name',
        'docs.replacePlaceholder':  'Platzhalter in geschweiften Klammern vor dem Senden durch einen echten Wert ersetzen, z.B. /api/v2/programs/2000 oder /api/v2/shooters/123456.',
        'docs.openInBrowser':       'Im Browser öffnen',
        'docs.pagingTitle':         'Sammlungen, Paginierung und Sync',
        'docs.pagingBody':          'Listen werden per Cursor geblättert, nicht per Offset: die Anlage schreibt laufend neue Datensätze und löscht alte, ein Offset würde deshalb Einträge überspringen oder doppelt liefern. Sende nextCursor als cursor wieder mit; hasMore sagt, ob es weitergeht. Für den Abgleich mit einer Wettkampf-Software: state=finished&order=asc mit from/to auf die Schiesstage setzen, den letzten nextCursor speichern und beim nächsten Mal wieder mitgeben – so kommen genau die seither beendeten Passen dieser Tage.',
        'docs.loadFailed':          'Spezifikation konnte nicht geladen werden: {detail}',
    },

    fr: {
        'app.title':                'Résultats Sintro',
        'nav.api':                  'API',

        'live.connected':           'Direct',
        'live.connecting':          'Connexion…',
        'live.offline':             'Pas de connexion',

        'toolbar.date':             'Date',
        'toolbar.reload':           'Actualiser',
        'toolbar.fullscreen':       'Plein écran',
        'fullscreen.title':         'Affichage plein écran',
        'fullscreen.intro':         'Ouvrir la vue ici, ou copier l’adresse et l’ouvrir sur l’appareil d’affichage.',
        'fullscreen.hint':          'Les adresses peuvent être mises en favori ; les réglages y sont inclus.',
        'fullscreen.tickerSeconds': 'Temps de lecture par entrée (s)',
        'fullscreen.tickerCount':   'Résultats dans le défilement',
        'fullscreen.show':          'Afficher',
        'fullscreen.copy':          'Copier le lien',
        'fullscreen.copied':        'Copié',
        'fullscreen.copyFailed':    'Copier manuellement',
        'fullscreen.mode.live+results': 'Lignes et résultats',
        'fullscreen.mode.live':     'Lignes seulement',
        'fullscreen.mode.results':  'Résultats seulement',
        'toolbar.filterPlaceholder': 'Rechercher…',
        'aria.language':            'Langue',
        'aria.exitFullscreen':      'Quitter le plein écran',
        'aria.close':               'Fermer',

        'col.club':                 'Société',
        'col.program':              'Programme',
        'col.shooter':              'Tireur',
        'col.total':                'Résultat',
        'col.shots':                'Coups',

        'msg.empty':                'Aucune passe à cette date.',
        'msg.error':                'Impossible de charger les résultats : {detail}',
        'msg.count':                '{shown} passes',
        'msg.noLanes':              'Aucune ligne signalée.',
        'section.lanes':            'Lignes',
        'section.results':          'Derniers résultats',
        'lane.available':           'libre',

        'series.subtotal':          'Total',
        'series.bestFine':          'Meilleure valeur au dixième',
        'series.lastFine':          'Valeur au dixième du dernier tir',

        'total.mixedValuation':     'Cotation variable – pas de total général',
        'total.unknownValuation':   'Cotation inconnue – pas de total général',

        'shooter.unidentified':     'Ligne {lane} · {time}',
        'shooter.licenseOnly':      'Licence {license}',
        'badge.duplicateLicense':   'Licence en double',

        'warn.publicAccess':        'ATTENTION : accès public autorisé ({ranges}). Les noms des tireurs sont des données personnelles.',

        'footer.note':              'Vue en lecture seule de l’installation Sintro. Heures en heure locale de l’installation.',

        'docs.title':               'Documentation de l’API',
        'docs.intro':               'Tous les points d’accès sont en lecture seule. Les requêtes de données exigent le jeton d’API ; la spécification OpenAPI et cette page non. Par défaut, seule la journée en cours est renvoyée, utilisez from/to pour élargir la période. Cette page utilise le jeton de session interne pour permettre les essais directs.',
        'docs.parameters':          'Paramètres',
        'docs.try':                 'Essayer',
        'docs.send':                'Envoyer',
        'docs.noParameters':        'Aucun paramètre.',
        'docs.specLink':            'Spécification OpenAPI',
        'docs.inPath':              'dans le chemin',
        'docs.type':                'Type',
        'docs.name':                'Nom',
        'docs.replacePlaceholder':  'Remplacez le paramètre entre accolades par une valeur réelle avant d’envoyer, p. ex. /api/v2/programs/2000 ou /api/v2/shooters/123456.',
        'docs.openInBrowser':       'Ouvrir dans le navigateur',
        'docs.pagingTitle':         'Collections, pagination et synchronisation',
        'docs.pagingBody':          'Les listes se parcourent par curseur et non par décalage : l’installation ajoute des enregistrements en continu et supprime les anciens, un décalage sauterait donc des entrées ou les livrerait en double. Renvoyez nextCursor comme cursor ; hasMore indique s’il reste des pages. Pour synchroniser avec un logiciel de concours : utilisez state=finished&order=asc avec from/to sur les jours de tir, conservez le dernier nextCursor et renvoyez-le plus tard – vous recevrez exactement les passes de ces jours terminées depuis.',
        'docs.loadFailed':          'Impossible de charger la spécification : {detail}',
    },
};

export const DEFAULT_LANGUAGE = 'de';

export const translate = (dictionary, key, params = {}) => {
    const template = dictionary[key] ?? key;
    return template.replace(/\{(\w+)\}/g, (_, name) =>
        Object.hasOwn(params, name) ? String(params[name]) : `{${name}}`);
};
