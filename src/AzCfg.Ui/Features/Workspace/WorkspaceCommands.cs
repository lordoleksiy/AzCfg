using AzCfg.Ui.Application.State;
using AzCfg.Ui.Features.Platform;
using AzCfg.Ui.Shared;

namespace AzCfg.Ui.Features.Workspace;

/// <summary>
/// <c>workspace.openFolder</c>, <c>workspace.refresh</c>, <c>project.bind</c> (bind or re-bind the active project) and
/// <c>project.settings</c> (the Settings tab, which lists the active project's settings).
/// </summary>
public sealed class WorkspaceCommands(WorkspaceActions actions, WorkspaceState workspace, TabsState tabs, IBindingFlow binding, WorkbenchNavigator navigator) : ICommandContributor
{
    private const string Category = "Workspace";

    public IEnumerable<UiCommand> Commands =>
    [
        new(CommandIds.WorkspaceOpenFolder, "Open folder…", Category, KeyBindings.ShortcutOf(CommandIds.WorkspaceOpenFolder), actions.OpenFolderAsync),
        new(CommandIds.WorkspaceRefresh, "Refresh workspace", Category, KeyBindings.ShortcutOf(CommandIds.WorkspaceRefresh), actions.RefreshAsync, () => workspace.Tree is not null),
        new(CommandIds.ProjectBind, "Bind project…", Category, KeyBindings.ShortcutOf(CommandIds.ProjectBind), BindAsync, () => Bindable() is not null),
        new(CommandIds.ProjectSettings, "Project settings", Category, KeyBindings.ShortcutOf(CommandIds.ProjectSettings), () =>
        {
            navigator.OpenSettings();
            return Task.CompletedTask;
        }, () => Bindable() is not null),
    ];

    private string? Bindable() =>
        ActiveProject.IdOf(tabs, workspace) is { } id && workspace.FindProject(id) is { Project.Project.IsLibrary: false } ? id : null;

    private Task BindAsync() => Bindable() is { } id ? binding.BindAsync(id) : Task.CompletedTask;
}
