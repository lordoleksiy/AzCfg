namespace AzCfg.Core.Model;

/// <summary>
/// The user's saved environments and global settings (per-user state, decision 5; ADR 0008), resolved via
/// <see cref="Abstractions.IEnvironmentResolver"/>. <see cref="Mode"/> chooses the resolver strategy;
/// <see cref="DefaultLabel"/> is the label for new keys in <see cref="EnvironmentMode.StorePerEnvironment"/> when an
/// environment has no label of its own.
/// </summary>
public sealed record UserProfile(
    IReadOnlyList<ConfigEnvironment> Environments,
    EnvironmentMode Mode = EnvironmentMode.StorePerEnvironment,
    string? DefaultLabel = null);
