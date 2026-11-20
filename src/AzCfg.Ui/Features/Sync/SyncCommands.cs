using AzCfg.Core.Environments;
using AzCfg.Core.Model;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Features.Platform;
using AzCfg.Ui.Shared;

namespace AzCfg.Ui.Features.Sync;

/// <summary>
/// <c>sync.apply</c> and <c>sync.pullSelected</c> run the handler of the active project tab's Sync view (enabled only in Sync
/// mode); <c>env.makeActive</c> makes the active project tab's environment active when it is not.
/// </summary>
public sealed class SyncCommands(TabsState tabs, ProjectSessions sessions, WorkspaceState workspace, SyncCommandTargets targets, IActivationFlow activation)
    : ICommandContributor
{
    private const string Category = "Sync";

    public IEnumerable<UiCommand> Commands =>
    [
        Forward(CommandIds.SyncApply, "Apply selected changes"),
        Forward(CommandIds.SyncPullSelected, "Pull selected keys"),
        new(CommandIds.EnvMakeActive, "Make environment active", Category, KeyBindings.ShortcutOf(CommandIds.EnvMakeActive), MakeActiveAsync, () => Inactive() is not null),
    ];

    private UiCommand Forward(string id, string title) =>
        new(id, title, Category, KeyBindings.ShortcutOf(id), () => Target(id)?.Execute() ?? Task.CompletedTask,
            () => Target(id) is { } target && target.IsEnabled?.Invoke() != false);

    private UiCommand? Target(string id) =>
        ActiveSession() is { Mode: ProjectMode.Sync } session ? targets.Find(session.ProjectId, id) : null;

    private ProjectSession? ActiveSession() =>
        tabs.Tabs.FirstOrDefault(t => t.Id == tabs.ActiveId)?.Descriptor.ProjectId is { } id && sessions.TryGet(id, out var session) ? session : null;

    private (string ProjectId, ConfigEnvironment Env)? Inactive() =>
        ActiveSession() is { } session
        && workspace.FindProject(session.ProjectId)?.Binding is { } binding
        && SyncSelection.EnvironmentOf(workspace, session, binding) is { } env
        && !EnvironmentRules.IsActive(binding, env)
            ? (session.ProjectId, env)
            : null;

    private Task MakeActiveAsync() => Inactive() is { } target ? activation.MakeActiveAsync(target.ProjectId, target.Env) : Task.CompletedTask;
}
