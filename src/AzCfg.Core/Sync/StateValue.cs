namespace AzCfg.Core.Sync;

/// <summary>Normalized value of a <see cref="StateItem"/> (decision 4).</summary>
public abstract record StateValue
{
    public sealed record Plain(string Value) : StateValue;

    /// <summary>
    /// A secret value. Never carries the secret itself — only a <see cref="Fingerprint"/> used to detect
    /// changes. <see cref="SecretUri"/> is <c>null</c> when the secret exists only locally and is not yet
    /// referenced remotely.
    /// </summary>
    public sealed record Secret(Uri? SecretUri, string Fingerprint) : StateValue;
}
