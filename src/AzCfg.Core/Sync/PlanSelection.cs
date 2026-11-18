using AzCfg.Core.Model;

namespace AzCfg.Core.Sync;

/// <summary>Restricts a push plan to the keys a user selected (G7).</summary>
public static class PlanSelection
{
    /// <summary>
    /// Restricts <paramref name="plan"/> to <paramref name="keys"/> (ordinal): Changes whose <c>(After ?? Before).Key</c> is
    /// in keys, Conflicts and Warnings whose Key is in keys. A <see cref="ChangeKind.CreateSecret"/> with
    /// <c>Before == null</c> and <c>After == null</c> (awaiting a vault, its key is unknown) is kept regardless of keys.
    /// Recomputes <see cref="SyncPlan.TouchesGlobal"/> (a selected change other than Delete whose After or Before has scope
    /// Global) and <see cref="SyncPlan.RequiresVaultChoice"/> (a selected CreateSecret with <c>After == null</c>).
    /// Binding and Environment are unchanged. Applying the result updates the snapshot only for the selected keys.
    /// </summary>
    public static SyncPlan Select(SyncPlan plan, IReadOnlySet<string> keys)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(keys);

        var selected = new HashSet<string>(keys, StringComparer.Ordinal);
        Change[] changes = [.. plan.Changes.Where(c => (c.After ?? c.Before) is { } entry
            ? selected.Contains(entry.Key)
            : c.Kind == ChangeKind.CreateSecret)];
        return plan with
        {
            Changes = changes,
            Conflicts = [.. plan.Conflicts.Where(c => selected.Contains(c.Key))],
            Warnings = [.. plan.Warnings.Where(w => selected.Contains(w.Key))],
            RequiresVaultChoice = changes.Any(c => c is { Kind: ChangeKind.CreateSecret, After: null }),
            TouchesGlobal = changes.Any(c => c.Kind != ChangeKind.Delete
                && (c.After?.Scope == ConfigScope.Global || c.Before?.Scope == ConfigScope.Global)),
        };
    }
}
