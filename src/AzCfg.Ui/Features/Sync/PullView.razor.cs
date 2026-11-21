using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Ui.Application.Operations;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Application.Sync;
using AzCfg.Ui.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;

namespace AzCfg.Ui.Features.Sync;

/// <summary>
/// Pull of the active environment: incoming remote changes (selectable → Pull selected), conflicts with Keep local / Keep
/// remote, the local changes Pull all would overwrite, and Pull all (the guarded <see cref="ActivationFlow"/>).
/// </summary>
public sealed partial class PullView : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);
    private readonly List<IDisposable> _commands = [];
    private PullPreview? _preview;
    private bool _busy;
    private string? _loadError;

    [Inject] private ISyncWorkflowService Workflow { get; set; } = default!;

    [Inject] private WorkspaceState Workspace { get; set; } = default!;

    [Inject] private ActivationFlow Activation { get; set; } = default!;

    [Inject] private IOperationRunner Runner { get; set; } = default!;

    [Inject] private INotificationService Notifications { get; set; } = default!;

    [Inject] private SyncCommandTargets Targets { get; set; } = default!;

    [Parameter, EditorRequired] public ProjectSession Session { get; set; } = default!;

    [Parameter, EditorRequired] public ProjectBinding Binding { get; set; } = default!;

    [Parameter, EditorRequired] public ConfigEnvironment Environment { get; set; } = default!;

    private ProjectBinding CurrentBinding => Workspace.FindProject(Session.ProjectId)?.Binding ?? Binding;

    private bool Busy => _busy || Runner.Running.Any(o => o.Kind == OperationKind.Write);

    private bool CanPull => !Busy && _selected.Count > 0;

    private bool AllSelected => _preview is { } p && p.Incoming.All(r => _selected.Contains(r.Key));

    protected override async Task OnInitializedAsync()
    {
        Runner.Changed += OnRunnerChanged;
        foreach (var id in new[] { CommandIds.SyncApply, CommandIds.SyncPullSelected })
        {
            _commands.Add(Targets.Register(Session.ProjectId, new UiCommand(id, "Pull selected", "Sync", null, () => InvokeAsync(PullSelectedAsync), () => CanPull)));
        }

        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loadError = null;
        switch (await Runner.RunAsync($"Preview pull from {Environment.Name}", OperationKind.Read,
                    ct => Workflow.PreviewPullAsync(CurrentBinding, Environment, ct), _cts.Token))
        {
            case OperationResult<PullPreview>.Success s:
                _preview = s.Value;
                _selected.Clear();
                _selected.UnionWith(s.Value.Incoming.Select(r => r.Key));
                break;
            case OperationResult<PullPreview>.Failed f:
                _loadError = f.Message;
                break;
        }
    }

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
    }

    private void ToggleAll(ChangeEventArgs e)
    {
        _selected.Clear();
        if (e.Value is true && _preview is not null)
        {
            _selected.UnionWith(_preview.Incoming.Select(r => r.Key));
        }
    }

    private async Task PullSelectedAsync()
    {
        if (!CanPull || !await Activation.ConfirmProtectedPullAsync(Environment))
        {
            return;
        }

        string[] keys = [.. _selected];
        OperationResult<PullResult>? result = null;
        await RunBusyAsync(async () => result = await Runner.RunAsync($"Pull {keys.Length} {(keys.Length == 1 ? "key" : "keys")} from {Environment.Name}", OperationKind.Write,
            ct => Workflow.PullSelectedAsync(CurrentBinding, Environment, keys, ct), CancellationToken.None));
        Session.Invalidate();
        switch (result)
        {
            case OperationResult<PullResult>.Success { Value: var pulled }:
                await Notifications.ToastAsync($"Pulled {pulled.ValuesWritten + pulled.SecretsWritten} {(pulled.ValuesWritten + pulled.SecretsWritten == 1 ? "key" : "keys")} from {Environment.Name}", ToastIntent.Success);
                break;
            case OperationResult<PullResult>.Failed f:
                await Notifications.ToastAsync($"Pull failed: {f.Message}", ToastIntent.Error);
                break;
        }

        await LoadAsync();
    }

    private async Task PullAllAsync()
    {
        var pulled = false;
        await RunBusyAsync(async () => pulled = await Activation.PullAllAsync(Session.ProjectId, Environment));
        if (pulled)
        {
            await LoadAsync();
        }
    }

    private async Task ResolveAsync(string key, ConflictChoice choice)
    {
        OperationResult<bool>? result = null;
        await RunBusyAsync(async () => result = await Runner.RunAsync($"Resolve conflict of {key}", OperationKind.Write,
            ct => Workflow.ResolveConflictAsync(CurrentBinding, Environment, key, choice, ct), CancellationToken.None));
        Session.Invalidate();
        if (result is OperationResult<bool>.Failed f)
        {
            await Notifications.ToastAsync(f.Message, ToastIntent.Error);
        }

        await LoadAsync();
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
        _commands.ForEach(c => c.Dispose());
        _cts.Cancel();
        _cts.Dispose();
    }
}
