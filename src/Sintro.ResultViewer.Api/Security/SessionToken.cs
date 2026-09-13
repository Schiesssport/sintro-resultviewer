using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace Sintro.ResultViewer.Security;

/// <summary>The viewer's token, minted per process start and held in memory only; anyone who can load the viewer gets it by design.</summary>
public sealed class SessionToken
{
    public string Value { get; } = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
}
