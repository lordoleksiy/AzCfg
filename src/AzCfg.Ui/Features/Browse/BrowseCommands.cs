using AzCfg.Ui.Application.State;
using AzCfg.Ui.Features.Platform;
using AzCfg.Ui.Features.Project;
using AzCfg.Ui.Shared;

namespace AzCfg.Ui.Features.Browse;

/// <summary>
/// <c>browse.find</c>, <c>browse.edit</c>, <c>browse.delete</c> and <c>browse.addKey</c> run the handler of the active project
/// tab's Browse view (enabled only in Browse mode, §2.10).
/// </summary>
public sealed class BrowseCommands(ActiveProjectSession active, ProjectCommandTargets targets) : ICommandContributor
{
    private const string Category = "Browse";

    public IEnumerable<UiCommand> Commands =>
    [
        Forward(CommandIds.BrowseFind, "Find keys"),
        Forward(CommandIds.BrowseEdit, "Edit selected value"),
        Forward(CommandIds.BrowseDelete, "Delete selected key"),
        Forward(CommandIds.BrowseAddKey, "Add key"),
    ];

    private UiCommand Forward(string id, string title) =>
        new(id, title, Category, KeyBindings.ShortcutOf(id), () => Target(id)?.Execute() ?? Task.CompletedTask,
            () => Target(id) is { } target && target.IsEnabled?.Invoke() != false);

    private UiCommand? Target(string id) =>
        active.Session is { Mode: ProjectMode.Browse } session ? targets.Find(session.ProjectId, id) : null;
}
