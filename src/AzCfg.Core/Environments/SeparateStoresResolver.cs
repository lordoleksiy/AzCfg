using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;

namespace AzCfg.Core.Environments;

/// <summary>
/// Default <see cref="IEnvironmentResolver"/> strategy (ADR 0002): one App Configuration store per
/// environment. Returns the profile's environments as-is, except <see cref="ConfigEnvironment.Label"/> is
/// always cleared — this strategy never uses labels. A future <c>LabelResolver</c> will resolve one shared
/// store into per-environment labels instead.
/// </summary>
public sealed class SeparateStoresResolver : IEnvironmentResolver
{
    public IReadOnlyList<ConfigEnvironment> Resolve(UserProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return profile.Environments.Select(env => env with { Label = null }).ToList();
    }
}
