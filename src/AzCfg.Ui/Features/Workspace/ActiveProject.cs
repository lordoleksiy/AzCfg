using AzCfg.Ui.Application.State;

namespace AzCfg.Ui.Features.Workspace;

/// <summary>The project the sidebar and the commands act on: the project of the active tab, else the selected node of the workspace tree.</summary>
public static class ActiveProject
{
    public static string? IdOf(TabsState tabs, WorkspaceState workspace) =>
        tabs.Tabs.FirstOrDefault(t => t.Id == tabs.ActiveId)?.Descriptor.ProjectId ?? workspace.SelectedProjectId;
}
