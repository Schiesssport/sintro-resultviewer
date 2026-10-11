using Sintro.ResultViewer.Data;
using Sintro.ResultViewer.Domain;

namespace Sintro.ResultViewer.Api.V2;

/// <summary>Each Parse* returns the 400 body for an unrecognised value and null when accepted: ?state=finishd returning everything would be the opposite of what was asked for.</summary>
internal static class V2Query
{
    public static int ClampLimit(int? limit, SintroOptions settings) =>
        Math.Clamp(limit ?? settings.DefaultPageSize, 1, settings.MaxPageSize);

    public static ApiError? ParseState(string? state, out ProgramState? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(state)) return null;

        parsed = state.Trim().ToLowerInvariant() switch
        {
            "active" => ProgramState.Active,
            "finished" => ProgramState.Finished,
            "abandoned" => ProgramState.Abandoned,
            _ => null,
        };

        return parsed is null
            ? new ApiError("invalid_state", $"Unknown state '{state}'. Expected one of: active, finished, abandoned.")
            : null;
    }

    public static List<string> SplitList(string? raw) =>
        (raw ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();

    public static ApiError? ParseIntList(string name, string? raw, out List<int> values)
    {
        values = [];
        foreach (var part in SplitList(raw))
        {
            if (!int.TryParse(part, out var value))
                return new ApiError("invalid_filter", $"{name} must be a number or a comma-separated list of numbers, got '{part}'.");
            values.Add(value);
        }
        return null;
    }

    // "today" is resolved here so a simple consumer never has to know which day the API calls today.
    public static ApiError? ParseDateList(string? raw, DateOnly? from, DateOnly? to, DateOnly today, out List<DateOnly> dates)
    {
        dates = [];
        foreach (var part in SplitList(raw))
        {
            if (part.Equals("today", StringComparison.OrdinalIgnoreCase))
                dates.Add(today);
            else if (DateOnly.TryParseExact(part, "yyyy-MM-dd", out var date))
                dates.Add(date);
            else
                return new ApiError("invalid_filter", $"dates must be YYYY-MM-DD, today, or a comma-separated list of such days, got '{part}'.");
        }

        return dates.Count > 0 && (from is not null || to is not null)
            ? new ApiError("invalid_filter", "dates cannot be combined with from or to; pass either individual days or a window.")
            : null;
    }

    public static ApiError? ParseOrder(string? order, out bool ascending)
    {
        ascending = false;
        if (string.IsNullOrWhiteSpace(order)) return null;

        switch (order.Trim().ToLowerInvariant())
        {
            case "asc": ascending = true; return null;
            case "desc": return null;
            default: return new ApiError("invalid_order", $"Unknown order '{order}'. Expected 'asc' or 'desc'.");
        }
    }
}
