using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;

namespace AzCfg.Core.Environments;

/// <summary>Resolves environments with the strategy chosen by <see cref="UserProfile.Mode"/> (ADR 0008).</summary>
public sealed class ProfileEnvironmentResolver : IEnvironmentResolver
{
    private readonly SeparateStoresResolver _stores = new();
    private readonly LabelResolver _labels = new();

    public IReadOnlyList<ConfigEnvironment> Resolve(UserProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return profile.Mode == EnvironmentMode.LabelPerEnvironment ? _labels.Resolve(profile) : _stores.Resolve(profile);
    }
}
