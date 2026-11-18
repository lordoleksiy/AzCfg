using AzCfg.Ui.Components.Common;
using AzCfg.Ui.Features.Platform;
using AzCfg.Ui.Tests.Support;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace AzCfg.Ui.Tests.Components.Common;

public sealed class InspectorPaneTests : UiTestContext
{
    public InspectorPaneTests() => Services.AddScoped<LayoutState>();

    [Fact]
    public void Render_ShowsTitleSectionsAndContent()
    {
        var cut = Render<InspectorPane>(p => p
            .Add(i => i.Title, "Db:ConnectionString")
            .AddChildContent("<section class=\"x\">body</section>"));
        Assert.Contains("Db:ConnectionString", cut.Find(".az-inspector-heading").TextContent);
        Assert.Contains("body", cut.Find(".az-inspector-body").InnerHtml);
    }

    [Fact]
    public void Click_Close_TogglesTheLayoutAndMarksThePaneHidden()
    {
        var layout = Services.GetRequiredService<LayoutState>();
        var cut = Render<InspectorPane>(p => p.Add(i => i.Title, "k"));
        Assert.DoesNotContain("wide-hidden", cut.Find("aside").ClassList);
        cut.Find("fluent-button").Click();
        Assert.False(layout.InspectorVisible);
        cut.WaitForAssertion(() => Assert.Contains("wide-hidden", cut.Find("aside").ClassList));
        Assert.Contains("overlay-open", cut.Find("aside").ClassList);
    }

    [Fact]
    public void Render_InspectorSection_HasItsTitle() =>
        Assert.Contains("Local", Render<InspectorSection>(p => p.Add(s => s.Title, "Local")).Find(".az-inspector-section-title").TextContent);
}
