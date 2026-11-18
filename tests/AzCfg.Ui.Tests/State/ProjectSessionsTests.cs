using AzCfg.Ui.Application.State;
using static AzCfg.Ui.Tests.State.StateFixtures;

namespace AzCfg.Ui.Tests.State;

public sealed class ProjectSessionsTests
{
    [Fact]
    public void GetOrCreate_SameId_ReturnsSameSession()
    {
        var sessions = new ProjectSessions();
        Assert.Same(sessions.GetOrCreate("p"), sessions.GetOrCreate("p"));
    }

    [Fact]
    public void TryGet_Missing_ReturnsFalse() =>
        Assert.False(new ProjectSessions().TryGet("p", out _));

    [Fact]
    public void Close_RemovesSessionAndRaisesChanged()
    {
        var sessions = new ProjectSessions();
        sessions.GetOrCreate("p");
        var raised = 0;
        sessions.Changed += () => raised++;
        sessions.Close("p");
        Assert.False(sessions.TryGet("p", out _));
        Assert.Equal(1, raised);
    }

    [Fact]
    public void InvalidateAll_ClearsEverySessionCache()
    {
        var sessions = new ProjectSessions();
        var a = sessions.GetOrCreate("a");
        var b = sessions.GetOrCreate("b");
        a.SetView(View());
        b.SetView(View());
        b.SetDraft("dev", Draft(), 1);
        sessions.InvalidateAll();
        Assert.Null(a.View);
        Assert.Null(b.View);
        Assert.Equal(1, b.TotalPending);
    }
}
