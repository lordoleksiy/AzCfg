using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Core.UseCases;

namespace AzCfg.Ui.Application.Sync;

/// <summary>
/// Push/pull workflow of the UI over the Core use cases (AD-8: push and pull only for the active environment). Calls Core
/// for every rule; rows and previews hold plain values only.
/// </summary>
public sealed class SyncWorkflowService(
    ISyncService sync,
    IDraftService drafts,
    IResourceDiscovery discovery) : ISyncWorkflowService
{
    public async Task<PushPreview> PreviewPushAsync(ProjectBinding binding, ConfigEnvironment env, bool includeDeletes, CancellationToken ct)
    {
        EnsureActive(binding, env);
        return PlanRows.Preview(await sync.PlanPushAsync(binding, env, new PushSource.LocalFiles(includeDeletes), ct), PushOrigin.LocalFiles);
    }

    public async Task<PushPreview> PreviewDraftAsync(RemoteDraft draft, CancellationToken ct) =>
        PlanRows.Preview(await sync.PlanPushAsync(draft.Binding, draft.Environment, drafts.ToPushSource(draft), ct), PushOrigin.Draft);

    public PushPreview Select(PushPreview preview, IReadOnlySet<string> keys) =>
        PlanRows.Preview(PlanSelection.Select(preview.Plan, keys), preview.Origin);

    public IReadOnlyList<ApplyBlock> Check(PushPreview selected, ApplyConfirmation confirmation) =>
    [
        .. selected.Plan.Changes.Count == 0 ? [ApplyBlock.NothingSelected] : Array.Empty<ApplyBlock>(),
        .. ApplyGuard.Check(selected.Plan, ToOptions(confirmation)).Select(b => b switch
        {
            ApplyBlocker.Conflicts => ApplyBlock.Conflicts,
            ApplyBlocker.VaultChoiceRequired => ApplyBlock.VaultChoiceRequired,
            ApplyBlocker.EnvironmentNotConfirmed => ApplyBlock.EnvironmentNotConfirmed,
            _ => ApplyBlock.GlobalNotConfirmed,
        }),
    ];

    /// <summary>Throws <see cref="InvalidOperationException"/> before any write when <see cref="Check"/> reports a block.</summary>
    public Task<ApplyResult> ApplyAsync(PushPreview selected, ApplyConfirmation confirmation, CancellationToken ct) =>
        Check(selected, confirmation) is [var block, ..]
            ? throw new InvalidOperationException($"Cannot apply: {block}.")
            : sync.ApplyAsync(selected.Plan, ToOptions(confirmation), ct);

    public async Task<IReadOnlyList<AzureResource>> ListVaultsAsync(CancellationToken ct) =>
        [.. (await discovery.FindKeyVaultsAsync(ct)).OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)];

    public async Task<PullPreview> PreviewPullAsync(ProjectBinding binding, ConfigEnvironment env, CancellationToken ct)
    {
        EnsureActive(binding, env);
        return PlanRows.Pull(binding, env, await sync.DiffAsync(binding, env, ct));
    }

    public Task<IReadOnlyList<string>> UnmanagedLocalKeysAsync(ProjectBinding binding, CancellationToken ct) =>
        sync.UnmanagedLocalKeysAsync(binding, ct);

    public Task<PullResult> PullAsync(ProjectBinding binding, ConfigEnvironment env, bool force, CancellationToken ct) =>
        sync.PullAsync(binding, env, null, force, ct);

    public Task<PullResult> PullSelectedAsync(ProjectBinding binding, ConfigEnvironment env, IReadOnlyCollection<string> keys, CancellationToken ct) =>
        sync.PullSelectedAsync(binding, env, keys, ct);

    public Task ResolveConflictAsync(ProjectBinding binding, ConfigEnvironment env, string key, ConflictChoice choice, CancellationToken ct) =>
        sync.ResolveConflictAsync(binding, env, key, choice == ConflictChoice.KeepLocal ? ConflictResolution.KeepLocal : ConflictResolution.KeepRemote, ct);

    private static ApplyOptions ToOptions(ApplyConfirmation confirmation) => new(confirmation.TypedEnvironmentName, confirmation.GlobalConfirmed);

    private static void EnsureActive(ProjectBinding binding, ConfigEnvironment env)
    {
        if (!string.Equals(binding.ActiveEnvironment, env.Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Environment '{env.Name}' is not the active environment of the project; make it active first.");
        }
    }
}
