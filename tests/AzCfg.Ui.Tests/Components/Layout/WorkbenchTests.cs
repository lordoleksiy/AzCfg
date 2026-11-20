using AzCfg.Ui.Application.Environments;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Components.Layout;
using Bunit;
using Microsoft.FluentUI.AspNetCore.Components;

namespace AzCfg.Ui.Tests.Components.Layout;

public sealed class WorkbenchTests : WorkbenchContext
{
    [Fact]
    public void Render_WithRegistrations_ShowsSectionStatusItemAndWelcomeTab()
    {
        var cut = Render<Workbench>();
        Assert.Contains("section-body", cut.Markup);
        Assert.Contains("status-item", cut.Markup);
        Assert.Contains("Welcome to AzCfg", cut.Markup);
        Assert.Contains("TESTS", cut.Markup);
    }

    [Fact]
    public void Render_ProjectTabOpened_RendersRegisteredComponentWithTab()
    {
        var cut = Render<Workbench>();
        cut.InvokeAsync(() => Tabs.Open(Project("Orders"), "Orders"));
        cut.WaitForAssertion(() => Assert.Contains("tab:Orders", cut.Markup));
    }

    [Fact]
    public void Open_SameDescriptorTwice_ShowsOneTab()
    {
        var cut = Render<Workbench>();
        cut.InvokeAsync(() =>
        {
            Tabs.Open(Project("Orders"), "Orders");
            Tabs.Open(Project("Orders"), "Orders");
        });
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".az-tab"), t => t.TextContent.Contains("Orders")));
        Assert.Equal(2, cut.FindAll(".az-tab").Count);
    }

    [Fact]
    public void Render_TabComponentThrows_OtherTabAndChromeSurvive()
    {
        var cut = Render<Workbench>();
        cut.InvokeAsync(() =>
        {
            Tabs.Open(Project("Orders"), "Orders");
            Tabs.Open(new TabDescriptor(TabKind.Settings), "Settings");
        });
        cut.WaitForAssertion(() => Assert.Contains("Reload tab", cut.Markup));
        Assert.Contains("tab:Orders", cut.Markup);
        Assert.Contains("status-item", cut.Markup);
        Assert.Contains("section-body", cut.Markup);
    }

    [Fact]
    public void ClickClose_CleanTab_ClosesIt()
    {
        var cut = Render<Workbench>();
        cut.InvokeAsync(() => Tabs.Open(Project("Orders"), "Orders"));
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".az-tab").Count));
        cut.Find("[aria-label='Close Orders']").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".az-tab")));
    }

    [Fact]
    public void Render_DirtyTab_ShowsDirtyDot()
    {
        var cut = Render<Workbench>();
        cut.InvokeAsync(() =>
        {
            var id = Tabs.Open(Project("Orders"), "Orders");
            Tabs.SetDirty(id, true);
        });
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".az-tab-dirty")));
    }

    [Fact]
    public void Render_AuthRequired_ShowsSignInBarWithRetry()
    {
        Status.SetState(ConnectionState.AuthRequired);
        var cut = Render<Workbench>();
        Assert.Contains("az login", cut.Markup);
        cut.FindComponent<FluentMessageBar>();
        Assert.Contains("Retry", cut.Markup);
    }

    [Fact]
    public void Render_UnknownAccount_ChecksItOnce()
    {
        Status.SetState(ConnectionState.Unknown);
        Render<Workbench>();
        Assert.Equal(1, Status.Checks);
        Assert.Contains("Check Azure account", Runner.Titles);
    }

    [Fact]
    public void Render_RunningOperation_ShowsItsTitleInStatusBar()
    {
        Runner.StartRunning("Reading dev…");
        var cut = Render<Workbench>();
        Assert.Contains("Reading dev…", cut.Find(".az-statusbar").TextContent);
    }

}
