using AzCfg.Core.Mapping;
using AzCfg.Core.Model;

namespace AzCfg.Core.Sync;

/// <summary>Compares the effective configurations of two environments without resolving secrets (G27).</summary>
public static class EnvironmentComparer
{
    /// <summary>
    /// Matches by Key (ordinal), ordered by Key. Plain↔Plain: ordinal value equality. KeyVaultRef↔KeyVaultRef (or
    /// ResolvedSecret, value ignored): equal <see cref="KeyMapper.SecretNameOf"/> (vault hosts may differ).
    /// Plain↔reference: Different. Scope and label do not affect Kind. A duplicate key within one side →
    /// <see cref="ArgumentException"/> naming the key.
    /// </summary>
    public static IReadOnlyList<ComparisonItem> Compare(IReadOnlyList<ConfigEntry> left, IReadOnlyList<ConfigEntry> right)
    {
        var l = ByKey(left, nameof(left));
        var r = ByKey(right, nameof(right));
        return [.. l.Keys.Union(r.Keys).Order(StringComparer.Ordinal).Select(key =>
        {
            var a = l.GetValueOrDefault(key);
            var b = r.GetValueOrDefault(key);
            var kind = (a, b) switch
            {
                (null, _) => ComparisonKind.OnlyRight,
                (_, null) => ComparisonKind.OnlyLeft,
                _ => Equal(a.Value, b.Value) ? ComparisonKind.Same : ComparisonKind.Different,
            };
            return new ComparisonItem(key, kind, a, b);
        })];
    }

    private static Dictionary<string, ConfigEntry> ByKey(IReadOnlyList<ConfigEntry> entries, string paramName)
    {
        ArgumentNullException.ThrowIfNull(entries, paramName);
        var byKey = new Dictionary<string, ConfigEntry>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!byKey.TryAdd(entry.Key, entry))
            {
                throw new ArgumentException($"Duplicate key '{entry.Key}'.", paramName);
            }
        }

        return byKey;
    }

    private static bool Equal(ConfigValue a, ConfigValue b) => (a, b) switch
    {
        (ConfigValue.Plain x, ConfigValue.Plain y) => string.Equals(x.Value, y.Value, StringComparison.Ordinal),
        _ when SecretUri(a) is { } x && SecretUri(b) is { } y =>
            string.Equals(KeyMapper.SecretNameOf(x), KeyMapper.SecretNameOf(y), StringComparison.Ordinal),
        _ => false,
    };

    private static Uri? SecretUri(ConfigValue value) => value switch
    {
        ConfigValue.KeyVaultRef r => r.SecretUri,
        ConfigValue.ResolvedSecret r => r.SecretUri,
        _ => null,
    };
}
