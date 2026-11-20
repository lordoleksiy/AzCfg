using AzCfg.Ui.Application.State;
using AzCfg.Ui.Features.Platform;
using AzCfg.Ui.Tests.Components.Layout;
using AzCfg.Ui.Tests.State;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace AzCfg.Ui.Tests.Features.Platform;

public sealed class TabCloserTests : WorkbenchContext
{
    private static readonly TabDescriptor Orders = Project("Orders");

    [Fact]
    public async Task CloseAsync_CleanProjectTab_ClosesTabAndSession()
    {
        var tabs = Tabs;
        var sessions = Services.GetRequiredService<ProjectSessions>();
        var id = tabs.Open(Orders, "Orders");
        sessions.GetOrCreate(Orders.ProjectId!);
        var closed = await Services.GetRequiredService<TabCloser>().CloseAsync(id);
        Assert.True(closed);
        Assert.Empty(tabs.Tabs);
        Assert.False(sessions.TryGet(Orders.ProjectId!, out _));
    }

    [Fact]
    public async Task CloseAsync_UnknownTab_ReturnsFalse() =>
        Assert.False(await Services.GetRequiredService<TabCloser>().CloseAsync(Guid.NewGuid()));

    [Fact]
    public async Task CloseAsync_DirtyTab_AsksAndKeepsTabWhenCancelled()
    {
        var providers = Render<FluentProviders>();
        var (id, _) = OpenDirty();
        var closing = Services.GetRequiredService<TabCloser>().CloseAsync(id);
        providers.WaitForAssertion(() => Assert.Contains("Discard 2 pending edits?", providers.Markup));
        ClickButton(providers, "Cancel");
        Assert.False(await closing);
        Assert.Single(Tabs.Tabs);
    }

    [Fact]
    public async Task CloseAsync_DirtyTab_ClosesAndDropsSessionWhenConfirmed()
    {
        var providers = Render<FluentProviders>();
        var (id, sessions) = OpenDirty();
        var closing = Services.GetRequiredService<TabCloser>().CloseAsync(id);
        providers.WaitForAssertion(() => Assert.Contains("Discard 2 pending edits?", providers.Markup));
        ClickButton(providers, "Discard");
        Assert.True(await closing);
        Assert.Empty(Tabs.Tabs);
        Assert.False(sessions.TryGet(Orders.ProjectId!, out _));
    }

    private (Guid Id, ProjectSessions Sessions) OpenDirty()
    {
        var sessions = Services.GetRequiredService<ProjectSessions>();
        var id = Tabs.Open(Orders, "Orders");
        sessions.GetOrCreate(Orders.ProjectId!).SetDraft("dev", StateFixtures.Draft(), 2);
        Tabs.SetDirty(id, true);
        return (id, sessions);
    }

    private static void ClickButton(IRenderedComponent<FluentProviders> providers, string text) =>
        providers.FindAll("fluent-button").First(b => b.TextContent.Trim() == text).Click();
}
