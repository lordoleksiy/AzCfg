using AzCfg.Core.Model;
using AzCfg.Ui.Application.Configuration;
using AzCfg.Ui.Application.Environments;
using AzCfg.Ui.Application.Operations;
using AzCfg.Ui.Features.Environments;
using AzCfg.Ui.Tests.Features.Workspace;
using Microsoft.Extensions.DependencyInjection;
using static AzCfg.Ui.Tests.Features.Workspace.ShellFixtures;

namespace AzCfg.Ui.Tests.Features.Environments;

public sealed class EnvironmentLoaderTests : ShellContext
{
    private EnvironmentLoader Loader => Services.GetRequiredService<EnvironmentLoader>();

    [Fact]
    public async Task Load_ReadsTheProfileThroughAReadOperationIntoTheWorkspaceState()
    {
        var result = await Loader.LoadAsync(CancellationToken.None);
        Assert.IsType<OperationResult<EnvironmentsSnapshot>.Success>(result);
        Assert.Equal(["dev", "prod"], Workspace.Environments!.Environments.Select(e => e.Name));
        Assert.Contains(("Load environments", OperationKind.Read), Runner.Runs);
    }

    [Fact]
    public async Task LoadIfNeeded_AlreadyLoaded_RunsNothing()
    {
        SeedEnvironments();
        Assert.Null(Loader.LoadIfNeededAsync(CancellationToken.None));
        Assert.Equal(0, EnvService.Loads);
        Workspace.SetEnvironments(null);
        await Loader.LoadIfNeededAsync(CancellationToken.None)!;
        Assert.Equal(1, EnvService.Loads);
    }

    [Fact]
    public async Task Save_Succeeds_PublishesTheSnapshotAndInvalidatesTheSessionsKeepingDrafts()
    {
        var session = Sessions.GetOrCreate(IdOf("Orders.Api"));
        session.SetView(new ConfigurationView(Binding("Orders.Api"), Dev, false, [], [], DateTimeOffset.UnixEpoch));
        session.SetDraft("dev", new AzCfg.Ui.Application.Sync.RemoteDraft(Binding("Orders.Api"), Dev, "{}", "{\"A\":\"1\"}", new Dictionary<AzCfg.Ui.Application.Sync.DraftKey, AzCfg.Ui.Application.Sync.DraftSecret>()), 1);
        var profile = Profile(Dev, Stage);
        var result = await Loader.SaveAsync(profile);
        Assert.IsType<OperationResult<EnvironmentsSnapshot>.Success>(result);
        Assert.Equal(["dev", "stage"], Workspace.Environments!.Environments.Select(e => e.Name));
        Assert.Null(session.View);
        Assert.Equal(1, session.PendingCount("dev"));
        Assert.Contains(("Save environments", OperationKind.Write), Runner.Runs);
    }

    [Fact]
    public async Task Save_Fails_KeepsTheStateAndTheViews()
    {
        SeedEnvironments();
        var session = Sessions.GetOrCreate(IdOf("Orders.Api"));
        session.SetView(new ConfigurationView(Binding("Orders.Api"), Dev, false, [], [], DateTimeOffset.UnixEpoch));
        EnvService.SaveFailure = new ArgumentException("Duplicate environment name 'dev'.");
        var result = await Loader.SaveAsync(Profile(Dev, Dev));
        var failed = Assert.IsType<OperationResult<EnvironmentsSnapshot>.Failed>(result);
        Assert.Contains("Duplicate", failed.Message);
        Assert.Equal(["dev", "prod"], Workspace.Environments!.Environments.Select(e => e.Name));
        Assert.NotNull(session.View);
    }

    [Fact]
    public async Task Check_ProbesTheEnvironmentWithTheServiceThroughARead()
    {
        await Loader.CheckAsync(Dev, "orders", CancellationToken.None);
        Assert.Equal([("dev", (string?)"orders")], Status.EnvironmentChecks);
        Assert.Contains(("Check dev", OperationKind.Read), Runner.Runs);
    }
}
