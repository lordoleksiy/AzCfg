using AzCfg.Ui.Components.Layout;
using AzCfg.Ui.Hosting;

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
    public void Render_DemoHost_ShowsDemoBadgeAndShortcutHint()
    {
        using var ctx = new Ctx(new UiHostOptions { Demo = "full" });
        var markup = ctx.Render<StatusBar>().Markup;
        Assert.Contains("DEMO", markup);
        Assert.Contains("Ctrl+K", markup);
    }

    private sealed class Ctx(UiHostOptions options) : WorkbenchContext(hostOptions: options);
}
