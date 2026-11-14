namespace AzCfg.Core.Model;

/// <summary>The user's saved set of environments (per-user state, decision 5), resolved via <see cref="Abstractions.IEnvironmentResolver"/>.</summary>
public sealed record UserProfile(IReadOnlyList<ConfigEnvironment> Environments);
