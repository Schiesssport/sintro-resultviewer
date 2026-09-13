using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace Sintro.ResultViewer.Security;

/// <summary>
/// A token minted per process start for the bundled viewer, held in memory only — never written
/// to disk, never logged. Restarting the API invalidates it. Anyone who can load the viewer
/// obtains a working token by design; the network gate is the real trust boundary and the API
/// is read-only regardless.
/// </summary>
public sealed class SessionToken
{
    public string Value { get; } = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
}
