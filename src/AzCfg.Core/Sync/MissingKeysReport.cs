namespace AzCfg.Core.Sync;

/// <summary>Finds keys that exist in some environments but not in all of them. Pure; keys only, never values.</summary>
public static class MissingKeysReport
{
    /// <summary>Keys (ordinal order) missing from at least one of <paramref name="configs"/>; environment names keep the input order.</summary>
    public static IReadOnlyList<MissingKey> Compute(IReadOnlyList<RemoteConfig> configs)
    {
        ArgumentNullException.ThrowIfNull(configs);
        var keysByEnv = configs.Select(c => (Name: c.Environment.Name, Keys: c.Entries.Select(e => e.Key).ToHashSet(StringComparer.Ordinal))).ToList();
        return [.. keysByEnv.SelectMany(e => e.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(key => new MissingKey(
                key,
                [.. keysByEnv.Where(e => e.Keys.Contains(key)).Select(e => e.Name)],
                [.. keysByEnv.Where(e => !e.Keys.Contains(key)).Select(e => e.Name)]))
            .Where(m => m.MissingIn.Count > 0)];
    }
}
