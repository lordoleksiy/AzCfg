namespace AzCfg.Core.Sync;

/// <summary>
/// Outcome of <c>SyncService.PullAsync</c>. <see cref="Blocked"/> is <c>true</c> when the pull refused to
/// overwrite unpushed local changes (see decision: pull overwrites local targets) — in that case nothing
/// was written, <see cref="ValuesWritten"/> and <see cref="SecretsWritten"/> are <c>0</c>, and
/// <see cref="LocalChanges"/> lists the keys that would have been lost. <see cref="Warnings"/> carries non-fatal
/// conditions (e.g. duplicate labels, ADR 0008).
/// </summary>
public sealed record PullResult(
    string Environment,
    int ValuesWritten,
    int SecretsWritten,
    bool Blocked,
    IReadOnlyList<string> LocalChanges)
{
    public IReadOnlyList<SyncWarning> Warnings { get; init; } = [];
}