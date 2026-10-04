using Sintro.ResultViewer.Domain;

namespace Sintro.ResultViewer.Live;

/// <summary>The firing line now: what GET /live answers and what the WebSocket pushes on every change.</summary>
public sealed record LanesFrame(IReadOnlyList<LaneStatus> Lanes);
