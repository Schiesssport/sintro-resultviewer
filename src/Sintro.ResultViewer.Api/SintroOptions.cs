namespace Sintro.ResultViewer;

/// <summary>Everything configurable, bound from the "Sintro" section.</summary>
public sealed class SintroOptions
{
    public const string SectionName = "Sintro";

    /// <summary>
    /// Tokens that may read. Several are allowed so each consumer — the club's event software,
    /// a scoreboard, a visiting organiser — gets its own and can be revoked alone.
    /// </summary>
    public string[]? ApiReadTokens { get; set; }

    /// <summary>
    /// Tokens that may write. A write token also reads, so a consumer never needs two.
    ///
    /// PLANNED: no endpoint writes yet, and the device owns its database. The scope exists now so
    /// that adding one later is a routing change rather than a breaking change to how every
    /// deployment is configured.
    /// </summary>
    public string[]? ApiWriteTokens { get; set; }

    /// <summary>
    /// Pins "today" to a fixed date so the today-only default returns data when
    /// working against an old export. Leave unset in production.
    /// </summary>
    public string? ReferenceDate { get; set; }

    /// <summary>
    /// The device writes local wall-clock time with no zone, so the API supplies the offset.
    /// Normally left unset: the host's own timezone is the range's timezone. Set it only when
    /// the machine's clock is configured for somewhere the range is not.
    /// </summary>
    public string? TimeZone { get; set; }

    /// <summary>
    /// Addresses or CIDR ranges of reverse proxies whose <c>X-Forwarded-For</c> may be believed.
    ///
    /// A boolean would not be enough: trusting the header unconditionally lets anyone who can
    /// reach the port claim any source address and walk straight through the network gate. Only
    /// hops in this list are unwound, and the first address that is not one of them is taken as
    /// the client. Empty — the default — means the header is ignored completely.
    /// </summary>
    public string[]? TrustedProxies { get; set; }

    public int DefaultPageSize { get; set; } = 200;
    public int MaxPageSize { get; set; } = 2000;

    public NetworkOptions Network { get; set; } = new();
    public LiveOptions Live { get; set; } = new();

    public const int MinimumTokenLength = 16;
    public const int RecommendedTokenLength = 128;

    public IReadOnlyList<string> ReadTokens => ApiReadTokens ?? [];
    public IReadOnlyList<string> WriteTokens => ApiWriteTokens ?? [];

    /// <summary>Every configured token, in either scope. A write token reads as well.</summary>
    public IEnumerable<string> AllTokens => ReadTokens.Concat(WriteTokens);
}

/// <summary>
/// Per-surface source-address allowlists.
///
/// There is deliberately no implicit fallback: who may reach a range's results is the operator's
/// decision, and a default applied invisibly in code is one they cannot see, review or turn off.
/// The shipped appsettings.jsonc spells the ranges out, and startup refuses an empty list rather
/// than guessing in either direction.
/// </summary>
public sealed class NetworkOptions
{
    public string[]? Api { get; set; }
    public string[]? Web { get; set; }

    /// <summary>
    /// What "private" means when deciding whether a *configured* range deserves a public-exposure
    /// warning. This is a fact about IP addressing, not a policy choice, so it stays in code —
    /// it is never used as an allowlist.
    /// </summary>
    public static readonly string[] PrivateSpace =
    [
        "127.0.0.0/8",     // loopback
        "10.0.0.0/8",      // RFC1918
        "172.16.0.0/12",   // RFC1918 (includes Docker's default bridge)
        "192.168.0.0/16",  // RFC1918
        "169.254.0.0/16",  // link-local
        "::1/128",         // IPv6 loopback
        "fc00::/7",        // IPv6 unique-local
        "fe80::/10",       // IPv6 link-local
    ];
}

public sealed class LiveOptions
{
    /// <summary>How often the lane watcher polls. A handful of lines makes this negligible load.</summary>
    public int PollMilliseconds { get; set; } = 1000;
}
