using AzCfg.Ui.Components.Layout;
using AzCfg.Ui.Demo;
using AzCfg.Ui.Hosting;
using AzCfg.Ui.Shared;

namespace AzCfg.Ui.Tests.Components.Layout;

public sealed class StatusBarTests
{
    [Fact]
    public void Render_RealAzure_HasNoDemoBadge()
    {
        using var ctx = new Ctx(new UiHostOptions());
        Assert.DoesNotContain("DEMO", ctx.Render<StatusBar>().Markup);
    }

    [Fact]
    public void Render_DemoHostWithRegisteredBadge_ShowsDemoBadgeOnceAndShortcutHint()
    {
        using var ctx = new Ctx(new UiHostOptions { Demo = "full" });
        ctx.Services.AddStatusBarItem(typeof(DemoBadge), StatusBarAlignment.Right, 100);
        var markup = ctx.Render<StatusBar>().Markup;
        Assert.Single(markup.Split("DEMO</span>").Skip(1));
        Assert.Contains("Ctrl+K", markup);
    }

    private sealed class Ctx(UiHostOptions options) : WorkbenchContext(hostOptions: options);
}
