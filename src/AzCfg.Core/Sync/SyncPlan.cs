using AzCfg.Core.Model;

namespace AzCfg.Core.Sync;

/// <summary>A planned push; <see cref="Warnings"/> carries non-fatal conditions (labels, skipped deletes, ADR 0008).</summary>
public sealed record SyncPlan(
    ProjectBinding Binding,
    ConfigEnvironment Environment,
    IReadOnlyList<Change> Changes,
    IReadOnlyList<Conflict> Conflicts,
    bool RequiresVaultChoice,
    bool TouchesGlobal)
{
    public IReadOnlyList<SyncWarning> Warnings { get; init; } = [];
}