using AzCfg.Core.Mapping;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Ui.Application.Security;

namespace AzCfg.Ui.Application.Sync;

/// <summary>Maps Core plans and diffs to view rows. Rows carry plain values only; secrets are never read into them.</summary>
public static class PlanRows
{
    public static IReadOnlyList<ChangeRow> Changes(SyncPlan plan) => [.. plan.Changes.Select(c => Change(plan.Binding, c))];

    public static IReadOnlyList<ConflictRow> Conflicts(SyncPlan plan) => [.. plan.Conflicts.Select(Conflict)];

    public static PushPreview Preview(SyncPlan plan, PushOrigin origin) =>
        new(plan.Binding, plan.Environment, origin, plan, Changes(plan), Conflicts(plan), plan.Warnings);

    /// <summary>Rows of the changes the pull would bring in (<c>Remote*</c>) and of the conflicts, in key order.</summary>
    public static PullPreview Pull(ProjectBinding binding, ConfigEnvironment env, ConfigDiff diff) => new(
        binding,
        env,
        [.. diff.Items.Where(i => i.Kind is DiffKind.RemoteAdded or DiffKind.RemoteModified or DiffKind.RemoteDeleted).Select(i => PullRow(binding, i))],
        [.. diff.Items.Where(i => i.Kind == DiffKind.Conflict).Select(i => PullRow(binding, i))],
        [.. diff.Items.Where(i => i.Kind is DiffKind.LocalAdded or DiffKind.LocalModified or DiffKind.LocalDeleted).Select(i => i.Key)]);

    private static ChangeRow Change(ProjectBinding binding, Change change)
    {
        var entry = change.After ?? change.Before;
        var isSecret = change.Kind == ChangeKind.CreateSecret || SecretPolicy.IsSecret(null, entry, null, binding);
        return new(
            entry?.Key ?? change.SecretName ?? string.Empty,
            change.Kind,
            entry?.Scope ?? ConfigScope.Service,
            isSecret,
            PlainOf(change.Before),
            PlainOf(change.After),
            isSecret ? change.SecretName ?? SecretNameOf(entry) : null,
            change.Kind == ChangeKind.CreateSecret && change.After is null);
    }

    private static ConflictRow Conflict(Conflict conflict)
    {
        var item = conflict.Local ?? conflict.Remote ?? conflict.Base;
        return new(
            conflict.Key,
            item?.Scope ?? ConfigScope.Service,
            new[] { conflict.Base, conflict.Local, conflict.Remote }.Any(i => i?.Value is StateValue.Secret),
            PlainOf(conflict.Base),
            PlainOf(conflict.Local),
            PlainOf(conflict.Remote));
    }

    private static PullRow PullRow(ProjectBinding binding, DiffItem item)
    {
        var isSecret = SecretPolicy.IsSecret(item, null, null, binding);
        return new(item.Key, item.Kind, isSecret, isSecret ? null : PlainOf(item.Local), isSecret ? null : PlainOf(item.Remote));
    }

    private static string? PlainOf(ConfigEntry? entry) => (entry?.Value as ConfigValue.Plain)?.Value;

    private static string? PlainOf(StateItem? item) => (item?.Value as StateValue.Plain)?.Value;

    private static string? SecretNameOf(ConfigEntry? entry) => entry?.Value switch
    {
        ConfigValue.KeyVaultRef r => KeyMapper.SecretNameOf(r.SecretUri),
        ConfigValue.ResolvedSecret r => KeyMapper.SecretNameOf(r.SecretUri),
        _ => null,
    };
}
