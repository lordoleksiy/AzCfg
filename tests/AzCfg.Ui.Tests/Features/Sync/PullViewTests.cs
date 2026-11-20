using AzCfg.Core.Sync;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Application.Sync;
using Bunit;
using static AzCfg.Ui.Tests.Features.Sync.SyncFixtures;

namespace AzCfg.Ui.Tests.Features.Sync;

public sealed class PullViewTests : SyncContext
{
    [Fact]
    public void Render_Preview_ShowsIncomingConflictsAndLocalChangesWithoutSecretValues()
    {
        Workflow.PullPreview = Pull(Binding(), Dev);
        var cut = RenderSync(Seed(Binding(), view: SyncView.Pull));
        Assert.Contains("3 of 3 incoming", cut.Markup);
        Assert.Contains("Local changes Pull all would overwrite (1)", cut.Markup);
        Assert.Contains("Keep remote", cut.Markup);
        Assert.Contains("••••••", cut.Find("tr[data-key='S']").TextContent);
        Assert.DoesNotContain(Sentinel, cut.Markup);
    }

    [Fact]
    public void PullSelected_PartialSelection_PullsTheChosenKeys()
    {
        Workflow.PullPreview = Pull(Binding(), Dev);
        var session = Seed(Binding(), view: SyncView.Pull);
        var cut = RenderSync(session);
        cut.Find("tr[data-key='B'] input.az-sync-check").Change(false);
        Click(cut, "Pull selected");
        cut.WaitForAssertion(() => Assert.Single(Workflow.PulledSelected));
        Assert.Equal(["A", "S"], Workflow.PulledSelected[0].Order(StringComparer.Ordinal));
    }

    [Fact]
    public void PullSelected_NothingSelected_IsDisabled()
    {
        Workflow.PullPreview = Pull(Binding(), Dev);
        var cut = RenderSync(Seed(Binding(), view: SyncView.Pull));
        cut.Find("input.az-sync-all").Change(false);
        Assert.True(ButtonStartingWith(cut, "Pull selected").HasAttribute("disabled"));
    }

    [Fact]
    public void KeepRemote_ResolvesTheLocalConflict()
    {
        Workflow.PullPreview = Pull(Binding(), Dev);
        var cut = RenderSync(Seed(Binding(), view: SyncView.Pull));
        Click(cut, "Keep remote");
        Assert.Equal([("C", ConflictChoice.KeepRemote)], Workflow.Resolved);
    }

    [Fact]
    public void PullSelected_Protected_WarnsAndCancelDoesNotPull()
    {
        var providers = Providers;
        Workflow.PullPreview = Pull(Binding("prod"), Prod);
        var cut = RenderSync(Seed(Binding("prod"), env: "prod", view: SyncView.Pull));
        Click(cut, "Pull selected");
        providers.WaitForAssertion(() => Assert.Contains("Secrets of prod will be written to local files.", providers.Markup));
        Click(providers, "Cancel");
        Assert.Empty(Workflow.PulledSelected);
    }

    [Fact]
    public void PullAll_Blocked_CancelNeverForces()
    {
        Workflow.PullResults.Enqueue(new PullResult("dev", 0, 0, true, ["L"]));
        var providers = Providers;
        var cut = RenderSync(Seed(Binding(), view: SyncView.Pull));
        Click(cut, "Pull all…");
        providers.WaitForAssertion(() => Assert.Contains("would be overwritten", providers.Markup));
        Click(providers, "Cancel");
        cut.WaitForAssertion(() => Assert.Equal([(Dev, false)], Workflow.Pulls));
    }

    [Fact]
    public void PullAll_BlockedAndDiscard_PullsWithForce()
    {
        Workflow.PullResults.Enqueue(new PullResult("dev", 0, 0, true, ["L"]));
        var providers = Providers;
        var cut = RenderSync(Seed(Binding(), view: SyncView.Pull));
        Click(cut, "Pull all…");
        Click(providers, "Discard local changes");
        cut.WaitForAssertion(() => Assert.Equal([(Dev, false), (Dev, true)], Workflow.Pulls));
    }

    [Fact]
    public void PullAll_BlockedAndPushFirst_OpensSyncPush()
    {
        Workflow.PullResults.Enqueue(new PullResult("dev", 0, 0, true, ["L"]));
        var providers = Providers;
        var session = Seed(Binding(), view: SyncView.Pull);
        var cut = RenderSync(session);
        Click(cut, "Pull all…");
        Click(providers, "Push first");
        cut.WaitForAssertion(() => Assert.Equal(SyncView.Push, session.SyncView));
        Assert.Equal([(Dev, false)], Workflow.Pulls);
        Assert.Equal(ProjectMode.Sync, session.Mode);
    }
}
