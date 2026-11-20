using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;

namespace AzCfg.Ui.Application.Configuration;

/// <summary>Picks the <see cref="ILocalTarget"/> of a binding like Core does and reads both targets of a project.</summary>
internal sealed class LocalTargetAccess(IEnumerable<ILocalTarget> targets)
{
    public ILocalTarget For(LocalTargetBinding target) => targets.First(t => t.Kind == target.Kind);

    /// <summary>Values target first, then the secrets target; a shared target is read once.</summary>
    public async Task<IReadOnlyList<SourcedLocalValue>> ReadAllAsync(ProjectBinding binding, CancellationToken ct)
    {
        var values = await For(binding.ValuesTarget).ReadAsync(binding.ValuesTarget, ct);
        var secrets = binding.ValuesTarget == binding.SecretsTarget ? [] : await For(binding.SecretsTarget).ReadAsync(binding.SecretsTarget, ct);
        return [.. values.Select(v => new SourcedLocalValue(v, binding.ValuesTarget)), .. secrets.Select(v => new SourcedLocalValue(v, binding.SecretsTarget))];
    }

    public static Dictionary<string, SourcedLocalValue> ByKey(IEnumerable<SourcedLocalValue> values)
    {
        var map = new Dictionary<string, SourcedLocalValue>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            map.TryAdd(value.Value.Key, value);
        }

        return map;
    }
}
