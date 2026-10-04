using System.Text.Json.Serialization;
using Sintro.ResultViewer.Domain;

namespace Sintro.ResultViewer.Live;

public sealed record LanesFrame(IReadOnlyList<LaneStatus> Lanes)
{
    [JsonPropertyOrder(-1)]
    public string Type => "lanes";
}
