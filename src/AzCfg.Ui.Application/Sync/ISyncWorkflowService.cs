using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Ui.Application.Sync;

public interface ISyncWorkflowService
{
    /// <summary>Active env only (AD-8), else <see cref="InvalidOperationException"/>. <c>PlanPushAsync(LocalFiles(includeDeletes))</c>.</summary>
    Task<PushPreview> PreviewPushAsync(ProjectBinding binding, ConfigEnvironment env, bool includeDeletes, CancellationToken ct);

    /// <summary><c>PlanPushAsync(IDraftService.ToPushSource(draft))</c>; any env.</summary>
    Task<PushPreview> PreviewDraftAsync(RemoteDraft draft, CancellationToken ct);

    /// <summary>Core <c>PlanSelection.Select</c>; rows filtered accordingly.</summary>
    PushPreview Select(PushPreview preview, IReadOnlySet<string> keys);

    /// <summary><see cref="ApplyBlock.NothingSelected"/> (no changes) + Core <c>ApplyGuard</c> mapped 1:1.</summary>
    IReadOnlyList<ApplyBlock> Check(PushPreview selected, ApplyConfirmation confirmation);

    Task<ApplyResult> ApplyAsync(PushPreview selected, ApplyConfirmation confirmation, CancellationToken ct);

    /// <summary>Ordered by name.</summary>
    Task<IReadOnlyList<AzureResource>> ListVaultsAsync(CancellationToken ct);

    /// <summary>Active env only. From <c>DiffAsync</c>.</summary>
    Task<PullPreview> PreviewPullAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct);

    /// <summary>Keys of the local targets when <c>binding.ActiveEnvironment</c> is null (a first pull replaces them, G20); else empty.</summary>
    Task<IReadOnlyList<string>> UnmanagedLocalKeysAsync(ProjectBinding binding, CancellationToken ct);

    Task<PullResult> PullAsync(ProjectBinding binding, ConfigEnvironment env, bool force, CancellationToken ct);

    Task<PullResult> PullSelectedAsync(ProjectBinding binding, ConfigEnvironment env, IReadOnlyCollection<string> keys, CancellationToken ct);

    Task ResolveConflictAsync(ProjectBinding binding, ConfigEnvironment env, string key, ConflictChoice choice, CancellationToken ct);
}
