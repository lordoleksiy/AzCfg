using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace AzCfg.Ui.Hosting;

/// <summary>
/// The one-time token of this run (AD-1): 32 random bytes, base64url. It only ever appears in the console login URL
/// and in the browser address bar; <see cref="ToString"/> never prints it.
/// </summary>
public sealed class AccessToken
{
    public AccessToken() => Value = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public string Value { get; }

    /// <summary>Constant-time comparison; null or empty never matches.</summary>
    public bool Matches(string? candidate) =>
        !string.IsNullOrEmpty(candidate)
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(candidate), Encoding.UTF8.GetBytes(Value));

    public override string ToString() => nameof(AccessToken);
}
