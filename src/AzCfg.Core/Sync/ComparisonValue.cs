namespace AzCfg.Core.Sync;

/// <summary>A compared value as shown to the user: the text of a plain key, or just "is a secret" (never the secret value).</summary>
public sealed record ComparisonValue(bool IsSecret, string? Plain);
