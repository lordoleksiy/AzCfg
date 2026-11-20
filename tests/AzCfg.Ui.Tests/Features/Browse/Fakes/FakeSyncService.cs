using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Core.UseCases;

namespace AzCfg.Ui.Tests.Features.Browse.Fakes;

/// <summary>Only <c>ExportJsonAsync</c> (draft start) and <c>StatusAsync</c> (project header summary) are used by Browse and the project tab.</summary>
internal sealed class FakeSyncService : ISyncService
{
    public string ExportJson { get; set; } = "{}";

    public SyncSummary? Summary { get; set; }

    public int StatusCalls { get; private set; }

    public Task<string> ExportJsonAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct) => Task.FromResult(ExportJson);

    public Task<SyncSummary> StatusAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct)
    {
        StatusCalls++;
        return Task.FromResult(Summary ?? new SyncSummary(env.Name, true, 0, 0, [], [], [], 0));
    }

    public Task<PullResult> PullAsync(ProjectBinding binding, ConfigEnvironment env, IProgress<SyncProgress>? progress, bool force, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<string> ExportPromoteJsonAsync(ProjectBinding binding, ConfigEnvironment from, ConfigEnvironment to, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<SyncPlan> PlanPushAsync(ProjectBinding binding, ConfigEnvironment env, PushSource source, CancellationToken ct) => throw new NotSupportedException();

    public Task<ConfigDiff> DiffAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct) => throw new NotSupportedException();

    public Task<ApplyResult> ApplyAsync(SyncPlan plan, ApplyOptions options, CancellationToken ct) => throw new NotSupportedException();

    public Task<RemoteConfig> ReadRemoteConfigAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct) => throw new NotSupportedException();

    public Task<PullResult> PullSelectedAsync(ProjectBinding binding, ConfigEnvironment env, IReadOnlyCollection<string> keys, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task ResolveConflictAsync(ProjectBinding binding, ConfigEnvironment env, string key, ConflictResolution resolution, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<string>> UnmanagedLocalKeysAsync(ProjectBinding binding, CancellationToken ct) => throw new NotSupportedException();
}
