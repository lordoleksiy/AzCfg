using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Ui.Application.Configuration;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Features.Sync;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using static AzCfg.Ui.Tests.Features.Sync.SyncFixtures;

namespace AzCfg.Ui.Tests.Features.Sync;

public sealed class SyncStatusItemTests : SyncContext
{
    [Fact]
    public void Render_NoSelectedProject_RendersNothing()
    {
        Seed(Binding());
        Assert.Empty(Render<SyncStatusItem>().Markup.Trim());
    }

    [Fact]
    public void Render_SelectedProjectWithLoadedView_ShowsSummary()
    {
        var session = Seed(Binding());
        var cut = Render<SyncStatusItem>();
        cut.InvokeAsync(() =>
        {
            Workspace.SelectProject(ProjectId);
            session.SetView(new ConfigurationView(Binding(), Dev, false, [Row("A", DiffKind.LocalAdded), Row("B", DiffKind.RemoteModified), Row("C", DiffKind.Conflict)], [], DateTimeOffset.UnixEpoch));
        });
        cut.WaitForAssertion(() => Assert.Contains("↑1", cut.Markup));
        Assert.Contains("Orders", cut.Markup);
        Assert.Contains("· dev", cut.Markup);
        Assert.Contains("↓1", cut.Markup);
        Assert.Contains("!1", cut.Markup);
    }

    [Fact]
    public void Click_Summary_OpensSyncMode()
    {
        var session = Seed(Binding());
        session.SetMode(ProjectMode.Browse);
        Workspace.SelectProject(ProjectId);
        var cut = Render<SyncStatusItem>();
        cut.Find("button.az-sync-status").Click();
        Assert.Equal(ProjectMode.Sync, session.Mode);
        Assert.Single(Services.GetRequiredService<TabsState>().Tabs);
    }

    private static ConfigRow Row(string key, DiffKind kind) => new(key, kind, false, false, null, null);
}
