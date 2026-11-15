namespace AzCfg.Core.Model;

/// <summary>A discovered App Configuration store with its guessed environment; <see cref="Name"/> is <c>null</c> when undetermined and the client decides.</summary>
public sealed record EnvironmentCandidate(string? Name, AzureResource Store, bool IsProtected);
