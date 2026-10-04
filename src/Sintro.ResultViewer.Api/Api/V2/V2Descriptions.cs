namespace Sintro.ResultViewer.Api.V2;

internal static class V2Descriptions
{
    public const string CollectionHelp = """
        Collection endpoints are cursor-paged, never offset-paged: the device inserts while you
        read and prunes old rows from the other end, so an offset would skip or repeat records.

        Paging — pass `nextCursor` back as `cursor`; `hasMore` says whether a further page exists
        right now. There is deliberately no total: counting the whole set costs a second scan per
        request and grows with the table, and paging never needs it. A cursor this API did not
        issue, or one issued under another order or state filter, is answered with 400
        `invalid_cursor` rather than silently restarting from page one.

        Syncing results — request `state=finished&order=asc` with `from` and `to` set to the
        event's shooting days (the range is also used for training), page until `hasMore` is
        false and store the last `nextCursor`. Passing it again later with the same window
        returns exactly the passes that finished since, whenever they started. Without a window a
        cursor request covers every day, not just today.

        Errors — every non-2xx answer carries `{"error": "<stable code>", "detail": "<text>"}`.
        """;

    public const string Live = """
        Returns {"lanes":[...]}: every lane the installation reports, in lane-number order,
        each with the program currently on it. A lane with nobody shooting has
        currentProgram: null, and lanes are always all listed so a display can show the
        full firing line.

        The same URL upgrades to a WebSocket. On connect it pushes the current lane state
        immediately, then the same {"lanes":[...]} afresh whenever a lane changes
        or a shot is fired. Browsers cannot set headers on a handshake, so the WebSocket
        upgrade — and only the upgrade — accepts the token as ?token=<token>.
        """;

    public const string Programs = $"""
        One program is one shooter shooting one program on one lane at one time — a
        "Passe". This is the main result endpoint.

        Defaults to today only; pass from and/or to for any other window. Programs with no
        counting shots (aborted or cleared runs) are omitted unless withoutResult=true.
        Use /live for what is being shot right now.

        state is one of: active (on a line, still being shot), finished (the device wrote
        an end total), abandoned (neither — started and dropped, or displaced when the
        line was reassigned; these almost always carry no shots). With state=finished the
        list is in finishing order; otherwise in starting order. order is asc or desc.
        An unrecognised value for either is answered with 400 rather than ignored, so a
        typo cannot quietly widen what you receive.

        targetCode, matchCode and license each take one value or a comma-separated list;
        a pass matches when any value fits. matchCode is looked up on the pass's shots.

        Each series carries a targetType such as A10 or B4 — the target letter plus the
        ring scale, the same notation used in program names. totals has one entry per
        targetType: the sum of its series and the fine values of its shots in firing order.
        A pass shot on one scale has exactly one entry; a pass that changed scale has
        several, because a 5er series added to a 10er one is meaningless. Shots carry
        matchCode, the event match the operator entered for the pass (null outside events),
        innerTen (the device's centre-hit flag, a Mouche) and hitSector: 1 is twelve o'clock
        and the numbers run clockwise in 45 degree steps, 0 means no direction (an inner ten,
        or a shot the device could not place), and null means the device reported no sector.

        {CollectionHelp}
        """;

    public const string Program = """
        Replace {id} in the path with a program id from /programs, for example
        /api/v2/programs/2000.

        totals carries one sum per target and ring scale with the fine values of its shots;
        an entry with valuation null means the device recorded no scale for those series.
        The usual tie-breaker, the best fine value of a series, is each series' bestFineValue
        (or the maximum over its shots). sighting lists the Probe series, one per stage the
        device recorded them in; they never count towards any total.
        """;

    public const string Shooters = $"""
        Registering a shooter is optional, so most programs have none. q matches surname,
        first name or licence number. With from and/or to (YYYY-MM-DD, inclusive) only
        shooters with a pass started inside that window are listed.

        {CollectionHelp}
        """;

    public const string Shooter = """
        Replace {license} in the path with a licence number from /shooters, for example
        /api/v2/shooters/123456. Leading zeros are optional — 12345 and 012345 resolve
        identically.

        Returns every shooter carrying the licence. The device schema places no unique
        constraint on it, so a collision is possible; each result carries
        duplicateLicense rather than one being silently chosen.

        programs is a cursor page like /programs, newest first, and includes passes with
        no shots. Page it the same way, with cursor and limit.
        """;

    public const string Clubs = $"q matches club name or club number.\n\n{CollectionHelp}";

    public const string Catalog = """
        Not a lookup table and not paged, so items only. The operator renames programs freely, so one
        targetCode can appear under several targetTitle names. Filter /programs by
        targetCode and/or targetTitle using the pairs listed here. timesShot is how many
        passes were shot under the pair, lastStartedAt when the last of them started.
        """;

    public const string Health = """
        The only data endpoint that needs no token, so monitoring can reach it. today is
        the date the today-only default resolves to. publicExposure lists any non-private
        network ranges the server is configured to accept. Answers 503 with the same body
        shape as every other error when the database cannot be reached.
        """;
}
