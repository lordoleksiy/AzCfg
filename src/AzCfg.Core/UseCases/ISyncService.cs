using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Core.UseCases;

public interface ISyncService
{
    /// <summary>Pulls <paramref name="env"/>; without <paramref name="force"/> it writes nothing and returns a blocked result when local files have unpushed changes.</summary>
    Task<PullResult> PullAsync(ProjectBinding binding, ConfigEnvironment env, IProgress<SyncProgress>? progress, bool force, CancellationToken ct);
    /// <summary>Edit JSON of the remote service and global keys (global under <c>@global</c>); secrets only as <c>{"@kv": name}</c>, their values are never read.</summary>
    Task<string> ExportJsonAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct);
    /// <summary>Edit JSON of service-scope keys in <paramref name="from"/> missing in <paramref name="to"/>; secrets are <c>{"@kv":"name","value":null}</c>. Global keys are excluded.</summary>
    Task<string> ExportPromoteJsonAsync(ProjectBinding binding, ConfigEnvironment from, ConfigEnvironment to, CancellationToken ct);

    Task<SyncPlan> PlanPushAsync(ProjectBinding binding, ConfigEnvironment env, PushSource source, CancellationToken ct);

    /// <summary>The normalized local/remote/base comparison a future UI uses to show a diff (decision 3).</summary>
    Task<ConfigDiff> DiffAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct);

    Task<ApplyResult> ApplyAsync(SyncPlan plan, ApplyOptions options, CancellationToken ct);

    /// <summary>Reads the remote configuration of <c>binding.Service</c> in <paramref name="env"/>. Never calls <c>ISecretStore</c>.</summary>
    Task<RemoteConfig> ReadRemoteConfigAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct);

    /// <summary>
    /// Pulls only <paramref name="keys"/> of <paramref name="env"/>, keeping every other local value. <paramref name="env"/> must
    /// equal <c>binding.ActiveEnvironment</c> (OrdinalIgnoreCase), else <see cref="InvalidOperationException"/> (also when there
    /// is no active environment). Before any write: the same gitignore and secrets-target safety steps as <see cref="PullAsync"/>.
    /// A selected key: remote plain → ValuesTarget (removed from SecretsTarget); KV reference → secret value to SecretsTarget
    /// (removed from ValuesTarget); missing remotely → removed locally; the scope marker follows the remote entry. Ignored
    /// keys are skipped (listed in Ignored). The snapshot is updated only for <paramref name="keys"/> (secrets as fingerprint).
    /// Returns counts; Blocked = false, LocalChanges = [].
    /// </summary>
    Task<PullResult> PullSelectedAsync(ProjectBinding binding, ConfigEnvironment env, IReadOnlyCollection<string> keys, CancellationToken ct);

    /// <summary>
    /// Resolves a local-files conflict of <paramref name="key"/> (active env only, same rule as <see cref="PullSelectedAsync"/>).
    /// KeepRemote = <c>PullSelectedAsync([key])</c>. KeepLocal = the snapshot item of <paramref name="key"/> becomes the current
    /// remote item (removed when missing remotely); local files are untouched, so the next <c>PlanPushAsync(LocalFiles)</c>
    /// plans the local value (Add/Update; a local deletion is a Delete only with <c>LocalFiles.Delete</c>, else DeleteSkipped).
    /// </summary>
    Task ResolveConflictAsync(ProjectBinding binding, ConfigEnvironment env, string key, ConflictResolution resolution, CancellationToken ct);

    /// <summary>
    /// Keys found in the local targets (sorted, distinct) while <c>binding.ActiveEnvironment</c> is <c>null</c> — nothing has
    /// been pulled yet, so a first pull would overwrite them (G20). Empty (and nothing is read) when an environment is active.
    /// Keys only, never values.
    /// </summary>
    Task<IReadOnlyList<string>> UnmanagedLocalKeysAsync(ProjectBinding binding, CancellationToken ct);
}
