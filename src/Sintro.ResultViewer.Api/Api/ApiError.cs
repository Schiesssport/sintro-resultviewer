namespace Sintro.ResultViewer.Api;

/// <summary>Every non-2xx body, middlewares included: a stable code a client can switch on plus human-readable detail.</summary>
public sealed record ApiError(string Error, string Detail);
