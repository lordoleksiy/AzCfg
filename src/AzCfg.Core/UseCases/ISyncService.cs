using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Core.UseCases;

public interface ISyncService
{
    Task PullAsync(ProjectBinding binding, ConfigEnvironment env, IProgress<SyncProgress>? progress, CancellationToken ct);
    Task<string> ExportJsonAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct);
    Task<SyncPlan> PlanPushAsync(ProjectBinding binding, ConfigEnvironment env, PushSource source, CancellationToken ct);

    /// <summary>The normalized local/remote/base comparison a future UI uses to show a diff (decision 3).</summary>
    Task<ConfigDiff> DiffAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct);

    Task<ApplyResult> ApplyAsync(SyncPlan plan, ApplyOptions options, CancellationToken ct);
}
