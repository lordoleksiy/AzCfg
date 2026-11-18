using AzCfg.Ui.Application.State;
using AzCfg.Ui.Application.Workspace;
using static AzCfg.Ui.Tests.State.StateFixtures;

namespace AzCfg.Ui.Tests.State;

public sealed class WorkbenchNavigatorTests
{
    private readonly TabsState _tabs = new();
    private readonly ProjectSessions _sessions = new();
    private readonly WorkspaceState _workspace = new();

    private WorkbenchNavigator Navigator() => new(_tabs, _sessions, _workspace);

    [Fact]
    public void OpenProject_AppliesEnvironmentModeAndKey()
    {
        var node = Node("Crm");
        _workspace.SetTree(new WorkspaceTree("C:/src", [], [node]));
        Navigator().OpenProject(node.Id, "test", ProjectMode.Compare, "Db:Host");
        var tab = Assert.Single(_tabs.Tabs);
        Assert.Equal((new TabDescriptor(TabKind.Project, node.Id), "Crm", tab.Id), (tab.Descriptor, tab.Title, _tabs.ActiveId!.Value));
        Assert.True(_sessions.TryGet(node.Id, out var session));
        Assert.Equal(("test", ProjectMode.Compare, "Db:Host"), (session.EnvironmentName, session.Mode, session.SelectedKey));
    }

    [Fact]
    public void OpenProject_WithoutOptions_KeepsSessionState()
    {
        var navigator = Navigator();
        navigator.OpenProject("C:/src/A/A.csproj", "dev", ProjectMode.Sync, "K");
        navigator.OpenProject("C:/src/A/A.csproj");
        Assert.Single(_tabs.Tabs);
        Assert.Equal("A", _tabs.Tabs[0].Title);
        var session = _sessions.GetOrCreate("C:/src/A/A.csproj");
        Assert.Equal(("dev", ProjectMode.Sync, "K"), (session.EnvironmentName, session.Mode, session.SelectedKey));
    }

    [Fact]
    public void OpenSettingsAndWelcome_OpenDedupedTabs()
    {
        var navigator = Navigator();
        navigator.OpenSettings();
        navigator.OpenWelcome();
        navigator.OpenSettings();
        Assert.Equal([TabKind.Settings, TabKind.Welcome], _tabs.Tabs.Select(t => t.Descriptor.Kind));
        Assert.Equal(_tabs.Tabs[0].Id, _tabs.ActiveId);
    }
}
