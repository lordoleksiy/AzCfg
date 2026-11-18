using AzCfg.Ui.Application.State;

namespace AzCfg.Ui.Tests.State;

public sealed class TabsStateTests
{
    private static TabDescriptor Project(int i) => new(TabKind.Project, $"C:/src/P{i}/P{i}.csproj");

    [Fact]
    public void Open_NewDescriptor_AddsLoadedActiveTab()
    {
        var tabs = new TabsState();
        var id = tabs.Open(new TabDescriptor(TabKind.Welcome), "Welcome");
        var tab = Assert.Single(tabs.Tabs);
        Assert.Equal((id, "Welcome", false, true), (tab.Id, tab.Title, tab.IsDirty, tab.IsLoaded));
        Assert.Equal(id, tabs.ActiveId);
    }

    [Fact]
    public void Open_ExistingDescriptor_ActivatesItAndUpdatesTitle()
    {
        var tabs = new TabsState();
        var first = tabs.Open(Project(1), "P1");
        tabs.Open(Project(2), "P2");
        var again = tabs.Open(Project(1), "P1 renamed");
        Assert.Equal(first, again);
        Assert.Equal(2, tabs.Tabs.Count);
        Assert.Equal(first, tabs.ActiveId);
        Assert.Equal("P1 renamed", tabs.Tabs[0].Title);
    }

    [Fact]
    public void Open_RaisesChanged()
    {
        var tabs = new TabsState();
        var raised = 0;
        tabs.Changed += () => raised++;
        tabs.Open(Project(1), "P1");
        Assert.True(raised > 0);
    }

    [Fact]
    public void RequestClose_CleanTab_ClosesAndActivatesNeighbour()
    {
        var tabs = new TabsState();
        var a = tabs.Open(Project(1), "P1");
        var b = tabs.Open(Project(2), "P2");
        Assert.True(tabs.RequestClose(b));
        Assert.Equal(a, Assert.Single(tabs.Tabs).Id);
        Assert.Equal(a, tabs.ActiveId);
    }

    [Fact]
    public void RequestClose_DirtyTab_ReturnsFalseAndKeepsIt()
    {
        var tabs = new TabsState();
        var a = tabs.Open(Project(1), "P1");
        tabs.SetDirty(a, true);
        Assert.False(tabs.RequestClose(a));
        Assert.True(Assert.Single(tabs.Tabs).IsDirty);
    }

    [Fact]
    public void ForceClose_DirtyTab_Closes()
    {
        var tabs = new TabsState();
        var a = tabs.Open(Project(1), "P1");
        tabs.SetDirty(a, true);
        tabs.ForceClose(a);
        Assert.Empty(tabs.Tabs);
        Assert.Null(tabs.ActiveId);
    }

    [Fact]
    public void Activate_MoreThanMaxLoaded_UnloadsOldestInactiveNonDirty()
    {
        var tabs = new TabsState();
        var ids = Enumerable.Range(0, TabsState.MaxLoaded).Select(i => tabs.Open(Project(i), $"P{i}")).ToList();
        tabs.SetDirty(ids[0], true);
        tabs.Open(Project(99), "P99");
        Assert.Equal(TabsState.MaxLoaded, tabs.Tabs.Count(t => t.IsLoaded));
        Assert.True(tabs.Tabs.Single(t => t.Id == ids[0]).IsLoaded);
        Assert.False(tabs.Tabs.Single(t => t.Id == ids[1]).IsLoaded);
    }

    [Fact]
    public void Activate_UnloadedTab_LoadsIt()
    {
        var tabs = new TabsState();
        var ids = Enumerable.Range(0, TabsState.MaxLoaded + 1).Select(i => tabs.Open(Project(i), $"P{i}")).ToList();
        Assert.False(tabs.Tabs.Single(t => t.Id == ids[0]).IsLoaded);
        tabs.Activate(ids[0]);
        Assert.True(tabs.Tabs.Single(t => t.Id == ids[0]).IsLoaded);
        Assert.Equal(ids[0], tabs.ActiveId);
        Assert.Equal(TabsState.MaxLoaded, tabs.Tabs.Count(t => t.IsLoaded));
    }
}
