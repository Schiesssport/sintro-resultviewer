namespace Sintro.ResultViewer;

/// <summary>Everything configurable, bound from the "Sintro" section; appsettings.jsonc explains each setting.</summary>
public sealed class SintroOptions
{
    public const string SectionName = "Sintro";
    public const int MinimumTokenLength = 16;
    public const int RecommendedTokenLength = 128;

    public string[]? ApiReadTokens { get; set; }

    /// <summary>Reserved: no endpoint writes yet. A write token also reads.</summary>
    public string[]? ApiWriteTokens { get; set; }

    /// <summary>Pins "today" so an old export still shows results. Development only.</summary>
    public string? ReferenceDate { get; set; }

    /// <summary>The device records wall-clock time without a zone; unset means the host's zone.</summary>
    public string? TimeZone { get; set; }

    /// <summary>Proxies whose X-Forwarded-For is believed; a list, not a switch, so a stranger cannot claim a LAN address.</summary>
    public string[]? TrustedProxies { get; set; }

    public int DefaultPageSize { get; set; } = 200;
    public int MaxPageSize { get; set; } = 2000;

    public NetworkOptions Network { get; set; } = new();
    public LiveOptions Live { get; set; } = new();

    public IReadOnlyList<string> ReadTokens => ApiReadTokens ?? [];
    public IReadOnlyList<string> WriteTokens => ApiWriteTokens ?? [];
    public IEnumerable<string> AllTokens => ReadTokens.Concat(WriteTokens);
}

/// <summary>Per-surface allowlists. No code default on purpose: the policy must be visible in the operator's file.</summary>
public sealed class NetworkOptions
{
    public string[]? Api { get; set; }
    public string[]? Web { get; set; }

    /// <summary>What "private" means for the public-exposure warning. Never used as an allowlist.</summary>
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
    public int PollMilliseconds { get; set; } = 1000;
}
