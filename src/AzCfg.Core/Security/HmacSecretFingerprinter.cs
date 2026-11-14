using System.Security.Cryptography;
using System.Text;
using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;

namespace AzCfg.Core.Security;

/// <summary>
/// Fingerprints secret values with HMAC-SHA256 under a per-user key (see
/// <see cref="Abstractions.IUserStateStore.GetOrCreateFingerprintKeyAsync"/>), so snapshots can detect a
/// secret change without ever storing the secret itself. Plain SHA-256 is avoided: many secrets are low
/// entropy, so an unkeyed hash would be brute-forceable; HMAC requires the per-user key to invert.
/// Output is lowercase hex.
/// </summary>
public sealed class HmacSecretFingerprinter : ISecretFingerprinter
{
    private const int MinKeyLengthBytes = 32;

    private readonly byte[] _key;

    public HmacSecretFingerprinter(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length < MinKeyLengthBytes)
        {
            throw new ArgumentException($"Key must be at least {MinKeyLengthBytes} bytes.", nameof(key));
        }

        _key = (byte[])key.Clone();
    }

    public string Fingerprint(SecretString secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        var input = Encoding.UTF8.GetBytes(secret.Reveal());
        var hash = HMACSHA256.HashData(_key, input);
        return Convert.ToHexStringLower(hash);
    }
}
