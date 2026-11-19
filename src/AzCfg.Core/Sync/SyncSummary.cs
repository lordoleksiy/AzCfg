namespace AzCfg.Core.Sync;

/// <summary>
/// Where <see cref="Environment"/> stands against the local files (<c>ISyncService.StatusAsync</c>, ADR 0013): keys and
/// counters only, never values. For a non-active environment <see cref="IsActive"/> is <c>false</c> and everything else is empty/0.
/// <see cref="LocalChanges"/> / <see cref="RemoteChanges"/> count keys changed only locally / only remotely since the last
/// snapshot; <see cref="Conflicts"/> lists keys changed on both sides. <see cref="BlockedKeys"/> are the keys with local changes
/// (including conflicts) that make a plain pull refuse to overwrite the local files. <see cref="IgnoredCount"/> is the number of
/// keys skipped by the project's ignore patterns. Lists are in ordinal key order.
/// </summary>
public sealed record SyncSummary(
    string Environment,
    bool IsActive,
    int LocalChanges,
    int RemoteChanges,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<string> BlockedKeys,
    IReadOnlyList<SyncWarning> Warnings,
    int IgnoredCount);
