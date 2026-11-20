using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Ui.Application.Sync;

namespace AzCfg.Ui.Tests.Features.Sync.Fakes;

/// <summary>Previews from configurable plans; Select/Check delegate to the real service (pure Core rules); writes are recorded.</summary>
internal sealed class FakeSyncWorkflowService : ISyncWorkflowService
{
    private readonly SyncWorkflowService _rules = new(null!, null!, null!);

    public Func<ProjectBinding, ConfigEnvironment, SyncPlan> PushPlan { get; set; } = (b, e) => SyncFixtures.Plan(b, e);

    public Func<RemoteDraft, SyncPlan>? DraftPlan { get; set; }

    public PullPreview? PullPreview { get; set; }

    public ApplyResult ApplyResult { get; set; } = new(1, []);

    public TaskCompletionSource? ApplyGate { get; set; }

    public IReadOnlyList<string> Unmanaged { get; set; } = [];

    public Queue<PullResult> PullResults { get; } = new();

    public IReadOnlyList<AzureResource> Vaults { get; set; } = [];

    public List<ProjectBinding> PushPreviews { get; } = [];

    public List<IReadOnlySet<string>> Selections { get; } = [];

    public List<(PushPreview Preview, ApplyConfirmation Confirmation)> Applied { get; } = [];

    public List<(string Key, ConflictChoice Choice)> Resolved { get; } = [];

    public List<(ConfigEnvironment Env, bool Force)> Pulls { get; } = [];

    public List<IReadOnlyCollection<string>> PulledSelected { get; } = [];

    public Task<PushPreview> PreviewPushAsync(ProjectBinding binding, ConfigEnvironment env, bool includeDeletes, CancellationToken ct)
    {
        PushPreviews.Add(binding);
        return Task.FromResult(PlanRows.Preview(PushPlan(binding, env), PushOrigin.LocalFiles));
    }

    public Task<PushPreview> PreviewDraftAsync(RemoteDraft draft, CancellationToken ct) =>
        Task.FromResult(PlanRows.Preview((DraftPlan ?? (d => PushPlan(d.Binding, d.Environment)))(draft), PushOrigin.Draft));

    public PushPreview Select(PushPreview preview, IReadOnlySet<string> keys)
    {
        Selections.Add(new HashSet<string>(keys));
        return _rules.Select(preview, keys);
    }

    public IReadOnlyList<ApplyBlock> Check(PushPreview selected, ApplyConfirmation confirmation) => _rules.Check(selected, confirmation);

    public async Task<ApplyResult> ApplyAsync(PushPreview selected, ApplyConfirmation confirmation, CancellationToken ct)
    {
        Applied.Add((selected, confirmation));
        if (ApplyGate is { } gate)
        {
            await gate.Task;
        }

        return ApplyResult;
    }

    public Task<IReadOnlyList<AzureResource>> ListVaultsAsync(CancellationToken ct) => Task.FromResult(Vaults);

    public Task<PullPreview> PreviewPullAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct) =>
        Task.FromResult(PullPreview ?? new PullPreview(binding, env, [], [], []));

    public Task<IReadOnlyList<string>> UnmanagedLocalKeysAsync(ProjectBinding binding, CancellationToken ct) => Task.FromResult(Unmanaged);

    public Task<PullResult> PullAsync(ProjectBinding binding, ConfigEnvironment env, bool force, CancellationToken ct)
    {
        Pulls.Add((env, force));
        return Task.FromResult(PullResults.Count > 0 ? PullResults.Dequeue() : new PullResult(env.Name, 2, 1, false, []));
    }

    public Task<PullResult> PullSelectedAsync(ProjectBinding binding, ConfigEnvironment env, IReadOnlyCollection<string> keys, CancellationToken ct)
    {
        PulledSelected.Add(keys);
        return Task.FromResult(new PullResult(env.Name, keys.Count, 0, false, []));
    }

    public Task ResolveConflictAsync(ProjectBinding binding, ConfigEnvironment env, string key, ConflictChoice choice, CancellationToken ct)
    {
        Resolved.Add((key, choice));
        return Task.CompletedTask;
    }
}
