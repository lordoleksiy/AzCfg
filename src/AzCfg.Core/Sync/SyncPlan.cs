using AzCfg.Core.Model;

namespace AzCfg.Core.Sync;

public sealed record SyncPlan(
    ProjectBinding Binding,
    ConfigEnvironment Environment,
    IReadOnlyList<Change> Changes,
    IReadOnlyList<Conflict> Conflicts,
    bool RequiresVaultChoice,
    bool TouchesGlobal);
