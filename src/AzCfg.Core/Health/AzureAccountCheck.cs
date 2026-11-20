namespace AzCfg.Core.Health;

/// <summary>Result of the Azure sign-in check: the signed-in account name (never a token) or a short error.</summary>
public sealed record AzureAccountCheck(bool SignedIn, string? Account, string? Error);
