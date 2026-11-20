using AzCfg.Core.Sync;
using AzCfg.Ui.Application.Configuration;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Features.Sync;
using AzCfg.Ui.Shared;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using static AzCfg.Ui.Tests.Features.Sync.SyncFixtures;

namespace AzCfg.Ui.Tests.Features.Sync;

public sealed class ActivationFlowTests : SyncContext
{
    private IActivationFlow Flow => Services.GetRequiredService<IActivationFlow>();

    [Fact]
    public void Resolve_IActivationFlow_IsTheSyncFeatureFlow() => Assert.IsType<ActivationFlow>(Flow);

    [Fact]
    public async Task MakeActive_Succeeds_UpdatesBindingAndInvalidatesViews()
    {
        var session = Seed(Binding("dev"));
        session.SetView(new ConfigurationView(Binding(), Dev, false, [], [], DateTimeOffset.UnixEpoch));
        Assert.True(await Flow.MakeActiveAsync(ProjectId, Prod with { IsProtected = false }));
        Assert.Equal("prod", CurrentBinding!.ActiveEnvironment);
        Assert.Null(session.View);
        Assert.Equal([(Prod with { IsProtected = false }, false)], Workflow.Pulls);
    }

    [Fact]
    public async Task MakeActive_FirstPullWithLocalKeys_AsksAndCancelWritesNothing()
    {
        Workflow.Unmanaged = ["Db:Host", "Api:Url"];
        Seed(Binding(active: null));
        var providers = Providers;
        var task = Flow.MakeActiveAsync(ProjectId, Dev);
        providers.WaitForAssertion(() => Assert.Contains("2 local keys will be replaced by dev.", providers.Markup));
        Click(providers, "Cancel");
        Assert.False(await task);
        Assert.Empty(Workflow.Pulls);
        Assert.Null(CurrentBinding!.ActiveEnvironment);
    }

    [Fact]
    public async Task MakeActive_FirstPullConfirmed_Pulls()
    {
        Workflow.Unmanaged = ["Db:Host"];
        Seed(Binding(active: null));
        var providers = Providers;
        var task = Flow.MakeActiveAsync(ProjectId, Dev);
        Click(providers, "Replace");
        Assert.True(await task);
        Assert.Equal("dev", CurrentBinding!.ActiveEnvironment);
    }

    [Fact]
    public async Task MakeActive_Protected_WarnsBeforePulling()
    {
        Seed(Binding("dev"));
        var providers = Providers;
        var task = Flow.MakeActiveAsync(ProjectId, Prod);
        providers.WaitForAssertion(() => Assert.Contains("Secrets of prod will be written to local files.", providers.Markup));
        Click(providers, "Cancel");
        Assert.False(await task);
        Assert.Empty(Workflow.Pulls);
    }

    [Fact]
    public async Task MakeActive_Blocked_NeverOverwritesWithoutDiscard()
    {
        Workflow.PullResults.Enqueue(new PullResult("stage", 0, 0, true, ["Db:Host"]));
        Seed(Binding("dev"));
        var providers = Providers;
        var task = Flow.MakeActiveAsync(ProjectId, Dev with { Name = "stage" });
        providers.WaitForAssertion(() => Assert.Contains("Db:Host", providers.Markup));
        Click(providers, "Discard local changes");
        Assert.True(await task);
        Assert.Equal([false, true], Workflow.Pulls.Select(p => p.Force));
        Assert.Equal("stage", CurrentBinding!.ActiveEnvironment);
    }

    [Fact]
    public async Task MakeActive_UnboundProject_DoesNotPull()
    {
        Seed(null);
        Assert.False(await Flow.MakeActiveAsync(ProjectId, Dev));
        Assert.Empty(Workflow.Pulls);
    }

    [Fact]
    public void Click_MakeActiveInSyncMode_ActivatesTheSelectedEnvironment()
    {
        var cut = RenderSync(Seed(Binding("prod"), env: "dev"));
        Click(cut, "Make dev active");
        cut.WaitForAssertion(() => Assert.Equal("dev", CurrentBinding!.ActiveEnvironment));
        cut.WaitForAssertion(() => Assert.Contains("of 2 changes", cut.Markup));
    }

    [Fact]
    public async Task EnvMakeActiveCommand_ActiveProjectTabOnInactiveEnv_MakesItActive()
    {
        Seed(Binding("prod"), env: "dev");
        Services.GetRequiredService<TabsState>().Open(new TabDescriptor(TabKind.Project, ProjectId), "Orders");
        var registry = Services.GetRequiredService<ICommandRegistry>();
        Assert.True(await registry.ExecuteAsync(CommandIds.EnvMakeActive));
        Assert.Equal("dev", CurrentBinding!.ActiveEnvironment);
        Assert.False(await registry.ExecuteAsync(CommandIds.EnvMakeActive));
    }

    [Fact]
    public async Task SyncApplyCommand_InSyncMode_OpensTheApplyDialog()
    {
        var providers = Providers;
        var session = Seed(Binding());
        Services.GetRequiredService<TabsState>().Open(new TabDescriptor(TabKind.Project, ProjectId), "Orders");
        RenderSync(session);
        var registry = Services.GetRequiredService<ICommandRegistry>();
        var running = registry.ExecuteAsync(CommandIds.SyncApply);
        providers.WaitForAssertion(() => Assert.Contains("Apply 2 changes to", providers.Markup));
        Click(providers, "Cancel");
        Assert.True(await running);
        session.SetMode(ProjectMode.Browse);
        Assert.False(await registry.ExecuteAsync(CommandIds.SyncApply));
    }
}
