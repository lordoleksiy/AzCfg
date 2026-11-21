using AzCfg.Core.Sync;
using AzCfg.Ui.Application.Configuration;
using AzCfg.Ui.Application.State;
using static AzCfg.Ui.Tests.State.StateFixtures;

namespace AzCfg.Ui.Tests.State;

public sealed class ProjectSessionTests
{
    [Fact]
    public void Ctor_Defaults_BrowsePushEmptyFilter()
    {
        var session = new ProjectSession("p");
        Assert.Equal(("p", ProjectMode.Browse, SyncView.Push, new ConfigFilter()), (session.ProjectId, session.Mode, session.SyncView, session.Filter));
        Assert.Null(session.EnvironmentName);
        Assert.Empty(session.Drafts);
    }

    [Fact]
    public void SetEnvironment_DifferentName_ClearsCachesKeepsDrafts()
    {
        var session = new ProjectSession("p");
        session.SetEnvironment("dev");
        session.SetView(View());
        session.SetComparison(new CompareSide.Local(), new CompareSide.Environment(Dev), Comparison());
        session.SetDraft("dev", Draft(), 2);
        session.SetEnvironment("test");
        Assert.Equal("test", session.EnvironmentName);
        Assert.Null(session.View);
        Assert.Null(session.Comparison);
        Assert.NotNull(session.DraftFor("dev"));
        Assert.Equal(2, session.PendingCount("dev"));
    }

    [Fact]
    public void SetEnvironment_SameNameOtherCase_KeepsCaches()
    {
        var session = new ProjectSession("p");
        session.SetEnvironment("dev");
        session.SetView(View());
        session.SetEnvironment("DEV");
        Assert.NotNull(session.View);
    }

    [Fact]
    public void Invalidate_ClearsViewAndComparisonKeepsDrafts()
    {
        var session = new ProjectSession("p");
        session.SetView(View());
        session.SetComparison(new CompareSide.Local(), new CompareSide.Environment(Dev), Comparison());
        session.SetDraft("dev", Draft(), 1);
        session.Invalidate();
        Assert.Null(session.View);
        Assert.Null(session.Comparison);
        Assert.Equal(1, session.TotalPending);
    }

    [Fact]
    public void SetSummary_StoresItAndRaisesChanged()
    {
        var session = new ProjectSession("p");
        var raised = 0;
        session.Changed += () => raised++;
        var summary = new SyncSummary("dev", true, 1, 2, [], [], [], 0);
        session.SetSummary(summary);
        Assert.Same(summary, session.Summary);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Invalidate_ClearsSummary()
    {
        var session = new ProjectSession("p");
        session.SetSummary(new SyncSummary("dev", true, 1, 2, [], [], [], 0));
        session.Invalidate();
        Assert.Null(session.Summary);
    }

    [Fact]
    public void SetDraft_CountsPerEnvironmentIgnoreCase()
    {
        var session = new ProjectSession("p");
        session.SetDraft("dev", Draft(), 2);
        session.SetDraft("prod", Draft(), 3);
        Assert.Equal(2, session.PendingCount("DEV"));
        Assert.Same(session.DraftFor("dev"), session.Drafts["Dev"]);
        Assert.Equal(5, session.TotalPending);
    }

    [Fact]
    public void SetDraft_Null_RemovesDraftAndCount()
    {
        var session = new ProjectSession("p");
        session.SetDraft("dev", Draft(), 2);
        session.SetDraft("dev", null, 0);
        Assert.Null(session.DraftFor("dev"));
        Assert.Equal(0, session.PendingCount("dev"));
        Assert.Equal(0, session.TotalPending);
    }

    [Fact]
    public void Setters_UpdateStateAndRaiseChanged()
    {
        var session = new ProjectSession("p");
        var raised = 0;
        session.Changed += () => raised++;
        session.SetMode(ProjectMode.Sync);
        session.SetSyncView(SyncView.Draft);
        session.SetFilter(new ConfigFilter(Text: "db"));
        session.Select("Db:Host");
        session.SetJsonView(true);
        Assert.Equal((ProjectMode.Sync, SyncView.Draft, "db", "Db:Host", true),
            (session.Mode, session.SyncView, session.Filter.Text, session.SelectedKey, session.JsonView));
        Assert.Equal(5, raised);
    }

    [Fact]
    public void SetComparison_StoresSides()
    {
        var session = new ProjectSession("p");
        var right = new CompareSide.Environment(Dev);
        session.SetComparison(new CompareSide.Local(), right, null);
        Assert.Equal(new CompareSide.Local(), session.CompareLeft);
        Assert.Equal(right, session.CompareRight);
        Assert.Null(session.Comparison);
    }
}
