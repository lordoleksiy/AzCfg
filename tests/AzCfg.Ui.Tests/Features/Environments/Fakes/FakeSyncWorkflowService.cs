using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Ui.Application.Sync;

namespace AzCfg.Ui.Tests.Features.Environments.Fakes;

/// <summary>Only <see cref="ListVaultsAsync"/> is used by the Environments feature.</summary>
internal sealed class FakeSyncWorkflowService : ISyncWorkflowService
{
    public IReadOnlyList<AzureResource> Vaults { get; set; } = [];

    public Task<IReadOnlyList<AzureResource>> ListVaultsAsync(CancellationToken ct) => Task.FromResult(Vaults);

    public Task<PushPreview> PreviewPushAsync(ProjectBinding binding, ConfigEnvironment env, bool includeDeletes, CancellationToken ct) => throw new NotSupportedException();

    public Task<PushPreview> PreviewDraftAsync(RemoteDraft draft, CancellationToken ct) => throw new NotSupportedException();

    public PushPreview Select(PushPreview preview, IReadOnlySet<string> keys) => throw new NotSupportedException();

    public IReadOnlyList<ApplyBlock> Check(PushPreview selected, ApplyConfirmation confirmation) => throw new NotSupportedException();

    public Task<ApplyResult> ApplyAsync(PushPreview selected, ApplyConfirmation confirmation, CancellationToken ct) => throw new NotSupportedException();

    public Task<PullPreview> PreviewPullAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct) => throw new NotSupportedException();

    public Task<IReadOnlyList<string>> UnmanagedLocalKeysAsync(ProjectBinding binding, CancellationToken ct) => throw new NotSupportedException();

    public Task<PullResult> PullAsync(ProjectBinding binding, ConfigEnvironment env, bool force, CancellationToken ct) => throw new NotSupportedException();

    public Task<PullResult> PullSelectedAsync(ProjectBinding binding, ConfigEnvironment env, IReadOnlyCollection<string> keys, CancellationToken ct) => throw new NotSupportedException();

    public Task ResolveConflictAsync(ProjectBinding binding, ConfigEnvironment env, string key, ConflictChoice choice, CancellationToken ct) => throw new NotSupportedException();
}
