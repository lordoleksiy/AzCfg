using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Ui.Application.Operations;
using AzCfg.Ui.Application.Security;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Application.Sync;
using AzCfg.Ui.Application.Workspace;
using AzCfg.Ui.Components.Common;
using AzCfg.Ui.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;

namespace AzCfg.Ui.Features.Sync;

/// <summary>
/// Push of local files (active environment only) or of the pending Azure edits (any environment): conflicts with Keep local /
/// Keep remote, selectable changes (<see cref="ISyncWorkflowService.Select"/>), warnings, the vault picker and the apply flow
/// (summary + global checkbox, typed confirmation for a protected environment, <see cref="ISyncWorkflowService.Check"/>).
/// </summary>
public sealed partial class PushView : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);
    private PushPreview? _preview;
    private PushPreview? _selection;
    private bool _includeDeletes;
    private bool _busy;
    private string? _loadError;
    private string? _applyError;
    private string? _selectedKey;
    private IDisposable? _command;

    [Inject] private ISyncWorkflowService Workflow { get; set; } = default!;

    [Inject] private IDraftService Drafts { get; set; } = default!;

    [Inject] private IWorkspaceService WorkspaceService { get; set; } = default!;

    [Inject] private WorkspaceState Workspace { get; set; } = default!;

    [Inject] private TabsState Tabs { get; set; } = default!;

    [Inject] private IOperationRunner Runner { get; set; } = default!;

    [Inject] private IDialogService Dialogs { get; set; } = default!;

    [Inject] private INotificationService Notifications { get; set; } = default!;

    [Inject] private SyncCommandTargets Targets { get; set; } = default!;

    [Parameter, EditorRequired] public ProjectSession Session { get; set; } = default!;

    [Parameter, EditorRequired] public ProjectBinding Binding { get; set; } = default!;

    [Parameter, EditorRequired] public ConfigEnvironment Environment { get; set; } = default!;

    /// <summary>Local files (Sync › Push) or the session's draft of <see cref="Environment"/> (Sync › Pending edits).</summary>
    [Parameter, EditorRequired] public PushOrigin Origin { get; set; }

    private ProjectBinding CurrentBinding => Workspace.FindProject(Session.ProjectId)?.Binding ?? Binding;

    private bool Busy => _busy || Runner.Running.Any(o => o.Kind == OperationKind.Write);

    private bool AllSelected => _preview is { } p && p.Changes.Where(c => !c.AwaitsVault).All(c => _selected.Contains(c.Key));

    /// <summary>Blocks that disable Apply; the confirmations are asked for by the apply flow itself.</summary>
    private IReadOnlyList<ApplyBlock> HardBlocks =>
        _selection is null ? [ApplyBlock.NothingSelected] : Workflow.Check(_selection, new ApplyConfirmation(Environment.Name, true));

    private bool CanApply => !Busy && HardBlocks.Count == 0;

    private string? BlockedText =>
        _selection is null ? null
        : string.Join("; ", (HardBlocks.Count > 0 ? HardBlocks : Workflow.Check(_selection, new ApplyConfirmation(null, false))).Select(SyncText.Block)) is { Length: > 0 } text ? text : null;

    private string ApplyText => Origin == PushOrigin.Draft ? $"Apply edits to {Environment.Name}" : $"Apply to {Environment.Name}";

    private string Summary
    {
        get
        {
            if (_preview is null || _selection is null)
            {
                return string.Empty;
            }

            var secrets = _selection.Changes.Count(c => c.IsSecret);
            List<string> parts = [$"{_selection.Changes.Count} of {_preview.Changes.Count} changes"];
            if (secrets > 0)
            {
                parts.Add($"{secrets} {(secrets == 1 ? "secret" : "secrets")}");
            }

            if (_selection.Plan.TouchesGlobal)
            {
                parts.Add("touches global");
            }

            return string.Join(" · ", parts);
        }
    }

    protected override async Task OnInitializedAsync()
    {
        Runner.Changed += OnRunnerChanged;
        _command = Targets.Register(Session.ProjectId,
            new UiCommand(CommandIds.SyncApply, ApplyText, "Sync", null, () => InvokeAsync(ApplyAsync), () => CanApply));
        await LoadAsync();
    }

    private static string Shown(bool isSecret, string? value) => isSecret ? SecretPolicy.Mask : value ?? "—";

    private async Task LoadAsync()
    {
        _loadError = null;
        var result = Origin == PushOrigin.Draft
            ? Session.DraftFor(Environment.Name) is { } draft
                ? await Runner.RunAsync($"Review edits for {Environment.Name}", OperationKind.Read, ct => Workflow.PreviewDraftAsync(draft, ct), _cts.Token)
                : null
            : await Runner.RunAsync($"Preview push to {Environment.Name}", OperationKind.Read,
                ct => Workflow.PreviewPushAsync(CurrentBinding, Environment, _includeDeletes, ct), _cts.Token);
        switch (result)
        {
            case OperationResult<PushPreview>.Success s:
                _preview = s.Value;
                _selected.Clear();
                _selected.UnionWith(s.Value.Changes.Where(c => !c.AwaitsVault).Select(c => c.Key));
                Reselect();
                break;
            case OperationResult<PushPreview>.Failed f:
                _loadError = f.Message;
                break;
        }
    }

    private void Reselect() => _selection = _preview is null ? null : Workflow.Select(_preview, _selected);

    private void Toggle(string key, ChangeEventArgs e)
    {
        if (e.Value is true)
        {
            _selected.Add(key);
        }
        else
        {
            _selected.Remove(key);
        }

        Reselect();
    }

    private void ToggleAll(ChangeEventArgs e)
    {
        _selected.Clear();
        if (e.Value is true && _preview is not null)
        {
            _selected.UnionWith(_preview.Changes.Where(c => !c.AwaitsVault).Select(c => c.Key));
        }

        Reselect();
    }

    private async Task ToggleDeletesAsync(ChangeEventArgs e)
    {
        _includeDeletes = e.Value is true;
        await LoadAsync();
    }

    private async Task RefreshPlanAsync()
    {
        _applyError = null;
        await LoadAsync();
    }

    private async Task ResolveAsync(string key, ConflictChoice choice)
    {
        await RunBusyAsync(async () =>
        {
            if (Origin == PushOrigin.Draft)
            {
                if (Session.DraftFor(Environment.Name) is not { } draft)
                {
                    return;
                }

                var result = await Runner.RunAsync($"Resolve conflict of {key}", OperationKind.Read, ct => Drafts.ResolveConflictAsync(draft, key, choice, ct), _cts.Token);
                if (result is OperationResult<RemoteDraft>.Success { Value: var resolved })
                {
                    Session.SetDraft(Environment.Name, resolved, Drafts.PendingKeys(resolved).Count);
                }
                else if (result is OperationResult<RemoteDraft>.Failed f)
                {
                    await Notifications.ToastAsync(f.Message, ToastIntent.Error);
                }
            }
            else
            {
                var result = await Runner.RunAsync($"Resolve conflict of {key}", OperationKind.Write,
                    ct => Workflow.ResolveConflictAsync(CurrentBinding, Environment, key, choice, ct), CancellationToken.None);
                Session.Invalidate();
                if (result is OperationResult<bool>.Failed f)
                {
                    await Notifications.ToastAsync(f.Message, ToastIntent.Error);
                }
            }
        });
        await LoadAsync();
    }

    private async Task SetVaultAsync(AzureResource vault)
    {
        var updated = false;
        await RunBusyAsync(async () =>
        {
            var result = await Runner.RunAsync($"Set vault for {Environment.Name}", OperationKind.Write,
                ct => WorkspaceService.SetVaultAsync(CurrentBinding, Environment.Name, vault.Endpoint, ct), CancellationToken.None);
            if (result is OperationResult<ProjectBinding>.Success { Value: var binding })
            {
                Workspace.UpdateBinding(Session.ProjectId, binding);
                if (Origin == PushOrigin.Draft && Session.DraftFor(Environment.Name) is { } draft)
                {
                    Session.SetDraft(Environment.Name, draft with { Binding = binding }, Session.PendingCount(Environment.Name));
                }

                updated = true;
            }
            else if (result is OperationResult<ProjectBinding>.Failed f)
            {
                await Notifications.ToastAsync(f.Message, ToastIntent.Error);
            }
        });
        if (updated)
        {
            await LoadAsync();
        }
    }

    private async Task ApplyAsync()
    {
        if (_selection is not { } selection || !CanApply)
        {
            return;
        }

        var dialog = await Dialogs.ShowDialogAsync<ApplyDialog>(o =>
        {
            o.Header.Title = $"Apply to {Environment.Name}";
            o.Parameters.Add(nameof(ApplyDialog.Environment), Environment.Name);
            o.Parameters.Add(nameof(ApplyDialog.Changes), selection.Changes);
            o.Parameters.Add(nameof(ApplyDialog.TouchesGlobal), selection.Plan.TouchesGlobal);
        });
        if (dialog.Cancelled)
        {
            return;
        }

        var count = selection.Changes.Count;
        if (Environment.IsProtected
            && !await Dialogs.ConfirmTypedAsync($"Apply to {Environment.Name}", $"Apply {count} changes to {Environment.Name}?", Environment.Name))
        {
            return;
        }

        var confirmation = new ApplyConfirmation(Environment.IsProtected ? Environment.Name : null, dialog.GetValue<bool>());
        if (Workflow.Check(selection, confirmation).Count > 0)
        {
            return;
        }

        OperationResult<ApplyResult>? result = null;
        await RunBusyAsync(async () =>
        {
            _applyError = null;
            result = await Runner.RunAsync($"Apply {count} changes to {Environment.Name}", OperationKind.Write,
                ct => Workflow.ApplyAsync(selection, confirmation, ct), CancellationToken.None);
        });
        Session.Invalidate();
        switch (result)
        {
            case OperationResult<ApplyResult>.Success { Value.Failed.Count: 0 } ok:
                await Notifications.ToastAsync($"Applied {ok.Value.Applied} changes to {Environment.Name}", ToastIntent.Success);
                if (Origin == PushOrigin.Draft)
                {
                    ClearDraft();
                }
                else
                {
                    await LoadAsync();
                }

                break;
            case OperationResult<ApplyResult>.Success { Value: var partial }:
                _applyError = $"{partial.Failed.Count} {(partial.Failed.Count == 1 ? "change" : "changes")} failed: {string.Join(", ", partial.Failed)} changed in Azure (412)";
                break;
            case OperationResult<ApplyResult>.Failed f:
                _applyError = f.Message;
                break;
        }
    }

    private void ClearDraft()
    {
        Session.SetDraft(Environment.Name, null, 0);
        if (Tabs.Tabs.FirstOrDefault(t => t.Descriptor.ProjectId == Session.ProjectId) is { } tab)
        {
            Tabs.SetDirty(tab.Id, Session.TotalPending > 0);
        }
    }

    private async Task RunBusyAsync(Func<Task> work)
    {
        _busy = true;
        StateHasChanged();
        try
        {
            await work();
        }
        finally
        {
            _busy = false;
        }
    }

    private void OnRunnerChanged() => InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        Runner.Changed -= OnRunnerChanged;
        _command?.Dispose();
        _cts.Cancel();
        _cts.Dispose();
    }
}
