namespace AzCfg.Core.Model;

/// <summary>
/// The user's saved environments and global settings (per-user state, decision 5; ADR 0008), resolved via
/// <see cref="Abstractions.IEnvironmentResolver"/>. <see cref="Mode"/> chooses the resolver strategy;
/// <see cref="DefaultLabel"/> is the label for new keys in <see cref="EnvironmentMode.StorePerEnvironment"/> when an
/// environment has no label of its own. <see cref="GitIgnoreLocalFiles"/> turns on the "configs in project files" mode
/// (ADR 0009): project-file targets are added to <c>.gitignore</c> on bind/pull and are offered as safe for secrets.
/// </summary>
public sealed record UserProfile(
    IReadOnlyList<ConfigEnvironment> Environments,
    EnvironmentMode Mode = EnvironmentMode.StorePerEnvironment,
    string? DefaultLabel = null,
    bool GitIgnoreLocalFiles = false);
