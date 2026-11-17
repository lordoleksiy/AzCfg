using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;

namespace AzCfg.Core.Environments;

/// <summary>
/// <see cref="EnvironmentMode.LabelPerEnvironment"/> strategy (ADR 0008): environments share a store and are
/// separated by label. The environment's label (its name when the label is blank) is both the read selector and
/// <see cref="ConfigEnvironment.WriteLabel"/>, so writes always carry a label.
/// </summary>
public sealed class LabelResolver : IEnvironmentResolver
{
    public IReadOnlyList<ConfigEnvironment> Resolve(UserProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return profile.Environments
            .Select(env => string.IsNullOrWhiteSpace(env.Label) ? env.Name : env.Label)
            .Zip(profile.Environments, (label, env) => env with { Label = label, WriteLabel = label })
            .ToList();
    }
}