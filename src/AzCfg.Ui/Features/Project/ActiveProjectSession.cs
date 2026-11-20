using AzCfg.Core.Model;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Features.Sync;

namespace AzCfg.Ui.Features.Project;

/// <summary>The session (and selected environment) of the active project tab, for commands; null when another tab is active.</summary>
public sealed class ActiveProjectSession(TabsState tabs, ProjectSessions sessions, WorkspaceState workspace)
{
    public ProjectSession? Session =>
        tabs.Tabs.FirstOrDefault(t => t.Id == tabs.ActiveId)?.Descriptor.ProjectId is { } id && sessions.TryGet(id, out var session) ? session : null;

    public ConfigEnvironment? Environment =>
        Session is { } session ? SyncSelection.EnvironmentOf(workspace, session, workspace.FindProject(session.ProjectId)?.Binding) : null;
}
