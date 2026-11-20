using AzCfg.Core.Model;
using AzCfg.Core.UseCases;
using AzCfg.Ui.Application.Environments;
using AzCfg.Ui.Application.Operations;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Application.Workspace;
using AzCfg.Ui.Components.Common;
using AzCfg.Ui.Features.Workspace;
using AzCfg.Ui.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;

namespace AzCfg.Ui.Features.Environments;

/// <summary>
/// Settings tab (FR-E2): the environments of the profile (mode, endpoints, labels, protected, default label, git-ignore of
/// local files; no rename — G15) and the Core project settings of the selected project (read-only, ADR 0014). Saving is a Write
/// that replaces <see cref="WorkspaceState.Environments"/> and invalidates the project sessions; removing an environment that
/// is the active one of a project in the workspace asks first (G30).
/// </summary>
public sealed partial class SettingsTab : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private EnvironmentsDraft _draft = new();
    private UserProfile? _loadedProfile;
    private IReadOnlyList<EnvironmentIssue> _issues = [];
    private IReadOnlyList<UserSetting> _projectSettings = [];
    private ProjectNode? _project;
    private string? _loadError;
    private string? _saveError;
    private string? _projectError;
    private bool _edited;
    private bool _saving;

    [Inject] private WorkspaceState Workspace { get; set; } = default!;

    [Inject] private EnvironmentLoader Loader { get; set; } = default!;

    [Inject] private IOperationRunner Runner { get; set; } = default!;

    [Inject] private IProjectSettingsService ProjectSettings { get; set; } = default!;

    [Inject] private IBindingFlow Binding { get; set; } = default!;

    [Inject] private ICommandRegistry Commands { get; set; } = default!;

    [Inject] private IDialogService Dialogs { get; set; } = default!;

    [Inject] private INotificationService Notifications { get; set; } = default!;

    [Parameter, EditorRequired] public WorkbenchTab Tab { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        Workspace.Changed += OnWorkspaceChanged;
        Adopt();
        await LoadProjectAsync();
        if (Loader.LoadIfNeededAsync(_cts.Token) is { } loading)
        {
            await ReportLoadAsync(await loading);
        }
    }

    private async Task ReloadAsync() => await ReportLoadAsync(await Loader.LoadAsync(_cts.Token));

    private Task ReportLoadAsync(OperationResult<EnvironmentsSnapshot> result)
    {
        _loadError = (result as OperationResult<EnvironmentsSnapshot>.Failed)?.Message;
        return Task.CompletedTask;
    }

    /// <summary>Starts a fresh draft from the saved profile (only while the user has not edited the current one).</summary>
    private void Adopt()
    {
        var profile = Workspace.Environments?.Profile;
        if (_edited || ReferenceEquals(profile, _loadedProfile) && Workspace.Environments is not null)
        {
            return;
        }

        _loadedProfile = profile;
        _draft = EnvironmentsDraft.From(profile);
        _issues = [];
    }

    private void OnWorkspaceChanged() => InvokeAsync(async () =>
    {
        Adopt();
        var node = SelectedNode();
        if (node?.Id != _project?.Id || node?.Binding != _project?.Binding)
        {
            await LoadProjectAsync();
        }

        StateHasChanged();
    });

    private ProjectNode? SelectedNode() => Workspace.SelectedProjectId is { } id ? Workspace.FindProject(id) : null;

    private void Edit(Action change)
    {
        change();
        OnEdited();
    }

    private void OnEdited()
    {
        _edited = true;
        _issues = _issues.Count == 0 ? [] : _draft.Validate();
    }

    private void AddEnvironment()
    {
        _draft.Rows.Add(new EnvironmentRow { IsNew = true, Endpoint = _draft.Mode == EnvironmentMode.LabelPerEnvironment ? _draft.Rows.FirstOrDefault()?.Endpoint ?? string.Empty : string.Empty });
        _edited = true;
    }

    private void Discard()
    {
        _edited = false;
        _loadedProfile = null;
        _saveError = null;
        Adopt();
    }

    private async Task SaveAsync()
    {
        _issues = _draft.Validate();
        if (_issues.Count > 0)
        {
            return;
        }

        if (await ConfirmRemovalAsync() is false)
        {
            return;
        }

        _saving = true;
        _saveError = null;
        var result = await Loader.SaveAsync(_draft.ToProfile());
        _saving = false;
        switch (result)
        {
            case OperationResult<EnvironmentsSnapshot>.Success:
                _edited = false;
                _loadedProfile = null;
                Adopt();
                await Notifications.ToastAsync("Environments saved", ToastIntent.Success);
                break;
            case OperationResult<EnvironmentsSnapshot>.Failed failed:
                _saveError = failed.Message;
                break;
        }
    }

    /// <summary>G30: removing an environment that is the active one of a workspace project warns that those projects lose their active environment.</summary>
    private async Task<bool> ConfirmRemovalAsync()
    {
        var removed = _draft.RemovedFrom(_loadedProfile);
        var affected = (Workspace.Tree?.Solutions.SelectMany(s => s.Projects).Concat(Workspace.Tree.LooseProjects) ?? [])
            .Where(p => p.Binding?.ActiveEnvironment is { } active && removed.Contains(active, StringComparer.OrdinalIgnoreCase))
            .DistinctBy(p => p.Id)
            .ToList();
        if (affected.Count == 0)
        {
            return true;
        }

        var names = string.Join(", ", affected.Select(p => $"{p.Project.Project.Name} ({p.Binding!.ActiveEnvironment})"));
        return await Dialogs.ConfirmAsync(
            "Remove active environment",
            $"These projects have an environment you are removing as their active one: {names}. Their local files stay as they are; make another environment active afterwards.",
            "Remove");
    }

    private async Task LoadProjectAsync()
    {
        _project = SelectedNode();
        _projectError = null;
        _projectSettings = [];
        if (_project?.Binding is not { } binding)
        {
            return;
        }

        switch (await Runner.RunAsync("Read project settings", OperationKind.Read, ct => ProjectSettings.ListAsync(binding.RepoPath, ct), _cts.Token))
        {
            case OperationResult<IReadOnlyList<UserSetting>>.Success success:
                _projectSettings = success.Value;
                break;
            case OperationResult<IReadOnlyList<UserSetting>>.Failed failed:
                _projectError = failed.Message;
                break;
        }
    }

    private async Task EditBindingAsync()
    {
        if (_project is not null && await Binding.BindAsync(_project.Id))
        {
            await LoadProjectAsync();
        }
    }

    public void Dispose()
    {
        Workspace.Changed -= OnWorkspaceChanged;
        _cts.Cancel();
        _cts.Dispose();
    }
}
