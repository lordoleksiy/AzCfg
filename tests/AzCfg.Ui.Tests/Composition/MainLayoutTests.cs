using AzCfg.Ui.Components.Layout;
using AzCfg.Ui.Tests.Support;
using Microsoft.AspNetCore.Components;

namespace AzCfg.Ui.Tests.Composition;

public sealed class MainLayoutTests : UiTestContext
{
    [Fact]
    public void Render_WithFluentServices_RendersBody()
    {
        var cut = Render<MainLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "spike body"))));
        Assert.Contains("spike body", cut.Markup);
    }
}
