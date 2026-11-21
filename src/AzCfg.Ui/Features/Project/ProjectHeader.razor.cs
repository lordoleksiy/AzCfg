using AzCfg.Core.Environments;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Core.UseCases;
using AzCfg.Ui.Application.Operations;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Application.Workspace;
using AzCfg.Ui.Components.Common;
using AzCfg.Ui.Shared;
using Microsoft.AspNetCore.Components;

namespace AzCfg.Ui.Features.Project;

/// <summary>
/// Header of a project tab (§2.4, FR-S7): project and service, environment pills (● = active, highlighted = selected), the mode
/// switch with the pending count of Sync, the summary of the selected environment when it is active (from
/// <see cref="ISyncService.StatusAsync"/>, cached in <see cref="ProjectSession.Summary"/>, reloaded after <see cref="ProjectSession.Invalidate"/>), Refresh, the lock button of a
/// protected environment and the overflow menu (Binding settings, Make active).
/// </summary>
public sealed partial class ProjectHeader : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private bool _loading;
    private bool _menu;

    [Inject] private WorkspaceState Workspace { get; set; } = default!;

    [Inject] private ISyncService Sync { get; set; } = default!;

    [Inject] private IOperationRunner Runner { get; set; } = default!;

    [Inject] private ICommandRegistry Commands { get; set; } = default!;

    [Inject] private ProtectionFlow Protection { get; set; } = default!;

    [Inject] private IActivationFlow Activation { get; set; } = default!;

    [Inject] private IBindingFlow BindingFlow { get; set; } = default!;

    [Parameter, EditorRequired] public ProjectSession Session { get; set; } = default!;

    [Parameter, EditorRequired] public ProjectNode Node { get; set; } = default!;

    /// <summary>The selected environment; null without environments.</summary>
    [Parameter] public ConfigEnvironment? Environment { get; set; }

    private IReadOnlyList<EnvPillModel> Pills =>
        [.. (Workspace.Environments?.Environments ?? []).Select(e => new EnvPillModel(e.Name, EnvironmentRules.KindOf(e), e.IsProtected, Node.Binding is { } b && EnvironmentRules.IsActive(b, e)))];

    private int PendingItems =>
        (Session.Summary is { IsActive: true } s ? s.LocalChanges + s.RemoteChanges + s.Conflicts.Count : 0)
        + (Environment is { } env ? Session.PendingCount(env.Name) : 0);

    private string Selected(ProjectMode mode) => IsMode(mode) == "true" ? "selected" : string.Empty;

    private string IsMode(ProjectMode mode) => Session.Mode == mode ? "true" : "false";

    protected override async Task OnParametersSetAsync() => await LoadSummaryAsync(force: false);

    protected override void OnInitialized() => Session.Changed += OnSessionChanged;

    private void OnSessionChanged() => _ = InvokeAsync(async () =>
    {
        await LoadSummaryAsync(force: false);
        StateHasChanged();
    });

    private async Task LoadSummaryAsync(bool force)
    {
        if (_loading || Node.Binding is not { } binding || Environment is not { } environment || !EnvironmentRules.IsActive(binding, environment))
        {
            return;
        }

        if (!force && Session.Summary is { } cached && string.Equals(cached.Environment, environment.Name, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _loading = true;
        try
        {
            var result = await Runner.RunAsync($"Check status of {environment.Name}", OperationKind.Read, ct => Sync.StatusAsync(binding, environment, ct), _cts.Token);
            Session.SetSummary(result is OperationResult<SyncSummary>.Success success ? success.Value : null);
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task RefreshAsync()
    {
        Session.SetSummary(null);
        await Commands.ExecuteAsync(CommandIds.ProjectRefresh);
    }

    private async Task ToggleLockAsync(ConfigEnvironment env)
    {
        if (Protection.IsLocked(env))
        {
            await Protection.UnlockAsync(env);
        }
        else
        {
            Protection.Lock(env);
        }
    }

    private async Task BindAsync()
    {
        _menu = false;
        await BindingFlow.BindAsync(Session.ProjectId);
    }

    private async Task MakeActiveAsync(ConfigEnvironment env)
    {
        _menu = false;
        await Activation.MakeActiveAsync(Session.ProjectId, env);
    }

    public void Dispose()
    {
        Session.Changed -= OnSessionChanged;
        _cts.Cancel();
        _cts.Dispose();
    }
}
