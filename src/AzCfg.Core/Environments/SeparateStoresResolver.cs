using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;

namespace AzCfg.Core.Environments;

/// <summary>
/// <see cref="EnvironmentMode.StorePerEnvironment"/> strategy (ADR 0002, ADR 0008): one App Configuration store per
/// environment. <see cref="ConfigEnvironment.Label"/> is cleared, so every label is read (labels never select the
/// environment); the configured label (else <see cref="UserProfile.DefaultLabel"/>) becomes
/// <see cref="ConfigEnvironment.WriteLabel"/> for new keys.
/// </summary>
public sealed class SeparateStoresResolver : IEnvironmentResolver
{
    public IReadOnlyList<ConfigEnvironment> Resolve(UserProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return profile.Environments
            .Select(env => env with { Label = null, WriteLabel = Blank(env.Label) ?? Blank(profile.DefaultLabel) })
            .ToList();
    }

    private static string? Blank(string? label) => string.IsNullOrWhiteSpace(label) ? null : label;
}
