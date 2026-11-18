namespace AzCfg.Ui.Application.State;

/// <summary>Opens workbench tabs and positions project sessions (palette, links, cross-feature navigation).</summary>
public sealed class WorkbenchNavigator(TabsState tabs, ProjectSessions sessions, WorkspaceState workspace)
{
    /// <summary>
    /// Opens/activates the project tab (title = project name, else the .csproj file name) and applies the optional env, mode
    /// and selected key to its session.
    /// </summary>
    public void OpenProject(string projectId, string? environment = null, ProjectMode? mode = null, string? key = null)
    {
        var title = workspace.FindProject(projectId)?.Project.Project.Name ?? Path.GetFileNameWithoutExtension(projectId);
        tabs.Open(new TabDescriptor(TabKind.Project, projectId), title);
        var session = sessions.GetOrCreate(projectId);
        if (environment is not null)
        {
            session.SetEnvironment(environment);
        }

        if (mode is { } m)
        {
            session.SetMode(m);
        }

        if (key is not null)
        {
            session.Select(key);
        }
    }

    public void OpenSettings() => tabs.Open(new TabDescriptor(TabKind.Settings), "Settings");

    public void OpenWelcome() => tabs.Open(new TabDescriptor(TabKind.Welcome), "Welcome");
}
