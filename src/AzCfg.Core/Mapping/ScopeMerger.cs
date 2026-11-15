using AzCfg.Core.Model;

namespace AzCfg.Core.Mapping;

/// <summary>
/// Merges global and service entries (ADR 0004): service entries override global ones with the same key
/// (ordinal), the last entry wins within the same scope, and the result is ordered by key (ordinal).
/// </summary>
public static class ScopeMerger
{
    public static IReadOnlyList<ConfigEntry> Merge(IEnumerable<ConfigEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var merged = new Dictionary<string, ConfigEntry>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (entry is null)
            {
                throw new ArgumentException("Entries must not contain null.", nameof(entries));
            }

            if (merged.TryGetValue(entry.Key, out var existing)
                && existing.Scope == ConfigScope.Service
                && entry.Scope == ConfigScope.Global)
            {
                continue;
            }

            merged[entry.Key] = entry;
        }

        return merged.Values
            .OrderBy(e => e.Key, StringComparer.Ordinal)
            .ToList();
    }
}
