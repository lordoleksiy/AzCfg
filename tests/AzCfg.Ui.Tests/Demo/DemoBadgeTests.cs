using AzCfg.Ui.Demo;
using AzCfg.Ui.Shared;
using AzCfg.Ui.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace AzCfg.Ui.Tests.Demo;

public sealed class DemoBadgeTests : UiTestContext
{
    [Fact]
    public void Render_ShowsDemoLabel() => Assert.Contains("DEMO", Render<DemoBadge>().Markup);

    [Fact]
    public async Task Demo_RegistersBadgeAsRightAlignedStatusBarItem()
    {
        await using var demo = await DemoApp.StartAsync("--demo=empty");
        var item = Assert.Single(demo.Services.GetServices<StatusBarItemRegistration>(), r => r.Component == typeof(DemoBadge));
        Assert.Equal(StatusBarAlignment.Right, item.Alignment);
    }
}
