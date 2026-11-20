using AzCfg.Core.Model;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Application.Sync;
using AzCfg.Ui.Shared;
using AzCfg.Ui.Tests.Features.Browse;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using static AzCfg.Ui.Tests.Features.Browse.BrowseFixtures;

namespace AzCfg.Ui.Tests.Features.Project;

public sealed class ProjectCommandsTests : BrowseContext
{
    private ICommandRegistry Commands => Services.GetRequiredService<ICommandRegistry>();

    private ProjectSession OpenActive(string env = "dev", string? active = "dev")
    {
        var session = Seed([Plain("Db:Host", "h1", "h1")], active, env);
        Tabs.Open(new TabDescriptor(TabKind.Project, ProjectId), "Orders");
        return session;
    }

    private bool Enabled(string id) => Commands.All.First(c => c.Id == id).IsEnabled?.Invoke() != false;

    [Fact]
    public async Task ModeCommands_SwitchTheModeOfTheActiveProject()
    {
        var session = OpenActive();
        Assert.True(await Commands.ExecuteAsync(CommandIds.ModeCompare));
        Assert.Equal(ProjectMode.Compare, session.Mode);
        Assert.True(await Commands.ExecuteAsync(CommandIds.ModeSync));
        Assert.Equal(ProjectMode.Sync, session.Mode);
        Assert.True(await Commands.ExecuteAsync(CommandIds.ModeBrowse));
        Assert.Equal(ProjectMode.Browse, session.Mode);
    }

    [Fact]
    public async Task ModeCommands_AreDisabledWithoutAProjectTab()
    {
        Seed([]);
        Tabs.Open(new TabDescriptor(TabKind.Welcome), "Welcome");
        Assert.False(Enabled(CommandIds.ModeBrowse));
        Assert.False(await Commands.ExecuteAsync(CommandIds.ProjectRefresh));
    }

    [Fact]
    public async Task Refresh_WithoutAViewHandler_InvalidatesTheSession()
    {
        var session = OpenActive();
        session.SetView(new AzCfg.Ui.Application.Configuration.ConfigurationView(BrowseFixtures.Binding(), Dev, false, [], [], DateTimeOffset.UnixEpoch));
        Assert.True(await Commands.ExecuteAsync(CommandIds.ProjectRefresh));
        Assert.Null(session.View);
    }

    [Fact]
    public async Task UnlockAndLock_AreEnabledByStateAndUnlockAsksFirst()
    {
        OpenActive("prod", "prod");
        Assert.True(Enabled(CommandIds.ProjectUnlock));
        Assert.False(Enabled(CommandIds.ProjectLock));
        var unlocking = Commands.ExecuteAsync(CommandIds.ProjectUnlock);
        Providers.WaitForAssertion(() => Providers.FindAll("fluent-button").First(b => b.TextContent.Trim() == "Unlock").Click());
        await unlocking;
        Assert.True(Unlock.IsUnlocked("prod"));
        Assert.False(Enabled(CommandIds.ProjectUnlock));
        Assert.True(await Commands.ExecuteAsync(CommandIds.ProjectLock));
        Assert.False(Unlock.IsUnlocked("prod"));
    }

    [Fact]
    public void UnlockAndLock_AreDisabledForAnUnprotectedEnvironment()
    {
        OpenActive();
        Assert.False(Enabled(CommandIds.ProjectUnlock));
        Assert.False(Enabled(CommandIds.ProjectLock));
    }

    [Fact]
    public async Task DraftCommands_NeedPendingEdits_ReviewOpensSyncAndDiscardAsks()
    {
        var session = OpenActive();
        Assert.False(Enabled(CommandIds.DraftReview));
        Assert.False(Enabled(CommandIds.DraftDiscard));
        session.SetDraft("dev", new RemoteDraft(BrowseFixtures.Binding(), Dev, "{}", "{}", new Dictionary<DraftKey, DraftSecret>()), 3);

        Assert.True(await Commands.ExecuteAsync(CommandIds.DraftReview));
        Assert.Equal((ProjectMode.Sync, SyncView.Draft), (session.Mode, session.SyncView));

        var discarding = Commands.ExecuteAsync(CommandIds.DraftDiscard);
        Providers.WaitForAssertion(() => Assert.Contains("Discard 3 pending edits to dev?", Providers.Markup));
        Providers.FindAll("fluent-button").First(b => b.TextContent.Trim() == "Discard").Click();
        await discarding;
        Assert.Null(session.DraftFor("dev"));
    }
}
