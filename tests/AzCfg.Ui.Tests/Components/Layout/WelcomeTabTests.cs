using AzCfg.Ui.Application.Environments;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Components.Layout;
using AzCfg.Ui.Shared;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace AzCfg.Ui.Tests.Components.Layout;

public sealed class WelcomeTabTests : WorkbenchContext
{
    private static readonly WorkbenchTab Tab = new(Guid.NewGuid(), new TabDescriptor(TabKind.Welcome), "Welcome", false, true);

    [Fact]
    public void Render_FirstRun_ShowsThreeStepsAndRecentFolders()
    {
        var cut = Render<WelcomeTab>(p => p.Add(w => w.Tab, Tab));
        Assert.Equal(3, cut.FindAll(".az-check").Count);
        Assert.Contains("Set up environments", cut.Markup);
        Assert.Contains("Open a folder", cut.Markup);
        Assert.Contains("Shop", cut.Find(".az-recent").TextContent);
    }

    [Fact]
    public void Render_AuthRequired_ShowsAzLoginHintAndRetry()
    {
        Status.SetState(ConnectionState.AuthRequired);
        var cut = Render<WelcomeTab>(p => p.Add(w => w.Tab, Tab));
        Assert.Contains("az login", cut.Markup);
        cut.FindAll("fluent-button").First(b => b.TextContent.Trim() == "Retry").Click();
        Assert.Contains("Check Azure account", Runner.Titles);
    }

    [Fact]
    public void Render_SignedIn_MarksFirstStepDone()
    {
        var cut = Render<WelcomeTab>(p => p.Add(w => w.Tab, Tab));
        Assert.Contains("done", cut.FindAll(".az-check")[0].ClassList);
        Assert.Contains("dev@contoso.com", cut.Markup);
    }

    [Fact]
    public void Click_Steps_ExecuteTheCommandIds()
    {
        var registry = Services.GetRequiredService<ICommandRegistry>();
        var executed = new List<string>();
        registry.Register(new UiCommand(CommandIds.EnvSetup, "Set up environments", "Env", null, () => { executed.Add(CommandIds.EnvSetup); return Task.CompletedTask; }));
        registry.Register(new UiCommand(CommandIds.WorkspaceOpenFolder, "Open folder", "Workspace", null, () => { executed.Add(CommandIds.WorkspaceOpenFolder); return Task.CompletedTask; }));
        var cut = Render<WelcomeTab>(p => p.Add(w => w.Tab, Tab));
        cut.FindAll("fluent-button").Single(b => b.TextContent.Trim() == "Set up…").Click();
        cut.FindAll("fluent-button").Single(b => b.TextContent.Trim() == "Open…").Click();
        Assert.Equal([CommandIds.EnvSetup, CommandIds.WorkspaceOpenFolder], executed);
    }

    [Fact]
    public void Click_RecentFolder_OpensItAndStoresTheTree()
    {
        var cut = Render<WelcomeTab>(p => p.Add(w => w.Tab, Tab));
        cut.Find(".az-recent-item").Click();
        cut.WaitForAssertion(() => Assert.Equal(["C:/src/Shop"], Workspace.Opened));
        Assert.Equal("C:/src/Shop", Services.GetRequiredService<WorkspaceState>().Tree?.RootPath);
    }
}
