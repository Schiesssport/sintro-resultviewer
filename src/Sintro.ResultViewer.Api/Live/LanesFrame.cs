using Sintro.ResultViewer.Domain;

namespace Sintro.ResultViewer.Live;

public sealed record LanesFrame(IReadOnlyList<LaneStatus> Lanes)
{
    public string Type => "lanes";
}
