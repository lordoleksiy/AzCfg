using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Core.UseCases;

public interface ISyncService
{
    Task<PullResult> PullAsync(ProjectBinding binding, ConfigEnvironment env, IProgress<SyncProgress>? progress, CancellationToken ct);
    Task<string> ExportJsonAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct);
    /// <summary>Edit JSON of service-scope keys in <paramref name="from"/> missing in <paramref name="to"/>; secrets are <c>{"@kv":"name","value":null}</c>. Global keys are excluded.</summary>
    Task<string> ExportPromoteJsonAsync(ProjectBinding binding, ConfigEnvironment from, ConfigEnvironment to, CancellationToken ct);

    Task<SyncPlan> PlanPushAsync(ProjectBinding binding, ConfigEnvironment env, PushSource source, CancellationToken ct);

    /// <summary>The normalized local/remote/base comparison a future UI uses to show a diff (decision 3).</summary>
    Task<ConfigDiff> DiffAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct);

    Task<ApplyResult> ApplyAsync(SyncPlan plan, ApplyOptions options, CancellationToken ct);
}
