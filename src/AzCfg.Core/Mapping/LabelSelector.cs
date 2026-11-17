using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Core.Mapping;

/// <summary>
/// Reduces entries that share <c>(Scope, Key)</c> under different labels to one copy (ADR 0008). Preference:
/// the environment's read label, then <see cref="ConfigEnvironment.WriteLabel"/>, then no label, then the
/// ordinal-first label. Duplicates are reported as <see cref="SyncWarningKind.DuplicateLabels"/>, except the
/// label-mode pair "unlabeled + environment label" (a normal override). Order of first occurrence is kept.
/// </summary>
public static class LabelSelector
{
    public static (IReadOnlyList<ConfigEntry> Entries, IReadOnlyList<SyncWarning> Warnings) Select(IEnumerable<ConfigEntry> entries, ConfigEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(env);

        List<ConfigEntry> selected = [];
        List<SyncWarning> warnings = [];
        foreach (var group in entries.GroupBy(e => (e.Scope, e.Key)))
        {
            var copies = group.ToList();
            var chosen = copies.Count == 1 ? copies[0] : copies
                .OrderBy(e => Rank(e.Label, env))
                .ThenBy(e => e.Label, StringComparer.Ordinal)
                .First();
            selected.Add(chosen);

            var expectedOverride = env.Label is not null && copies.All(e => e.Label is null || e.Label == env.Label) && copies.Count == 2;
            if (copies.Count > 1 && !expectedOverride)
            {
                var labels = string.Join(", ", copies.Select(e => Display(e.Label)));
                warnings.Add(new(SyncWarningKind.DuplicateLabels, group.Key.Key, $"'{group.Key.Key}' exists under labels {labels}; using {Display(chosen.Label)}."));
            }
        }

        return (selected, warnings.Count == 0 ? Array.Empty<SyncWarning>() : warnings);
    }

    private static int Rank(string? label, ConfigEnvironment env) =>
        env.Label is not null && label == env.Label ? 0
        : env.WriteLabel is not null && label == env.WriteLabel ? 1
        : label is null ? 2
        : 3;

    private static string Display(string? label) => label is null ? "(no label)" : $"'{label}'";
}