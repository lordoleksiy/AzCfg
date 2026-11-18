using AzCfg.Ui.Features.Platform;

namespace AzCfg.Ui.Tests.Features.Platform;

public sealed class LayoutStateTests
{
    [Fact]
    public void New_HasVisibleDefaultPanesWithPlanWidths()
    {
        var layout = new LayoutState();
        Assert.True(layout.SidebarVisible);
        Assert.True(layout.InspectorVisible);
        Assert.False(layout.SidebarOverlayOpen);
        Assert.Equal((280, 340), (layout.SidebarWidth, layout.InspectorWidth));
    }

    [Fact]
    public void ToggleSidebar_FlipsWideAndOverlayFlags()
    {
        var layout = new LayoutState();
        layout.ToggleSidebar();
        Assert.False(layout.SidebarVisible);
        Assert.True(layout.SidebarOverlayOpen);
        layout.ToggleSidebar();
        Assert.True(layout.SidebarVisible);
        Assert.False(layout.SidebarOverlayOpen);
    }

    [Fact]
    public void ToggleInspector_FlipsWideAndOverlayFlags()
    {
        var layout = new LayoutState();
        layout.ToggleInspector();
        Assert.False(layout.InspectorVisible);
        Assert.True(layout.InspectorOverlayOpen);
    }

    [Theory]
    [InlineData(50, LayoutState.SidebarMin)]
    [InlineData(300, 300)]
    [InlineData(9999, LayoutState.SidebarMax)]
    public void SetSidebarWidth_ClampsToPlanRange(int requested, int expected)
    {
        var layout = new LayoutState();
        layout.SetSidebarWidth(requested);
        Assert.Equal(expected, layout.SidebarWidth);
    }

    [Theory]
    [InlineData(50, LayoutState.InspectorMin)]
    [InlineData(9999, LayoutState.InspectorMax)]
    public void SetInspectorWidth_ClampsToPlanRange(int requested, int expected)
    {
        var layout = new LayoutState();
        layout.SetInspectorWidth(requested);
        Assert.Equal(expected, layout.InspectorWidth);
    }

    [Fact]
    public void Toggle_RaisesChanged()
    {
        var layout = new LayoutState();
        var raised = 0;
        layout.Changed += () => raised++;
        layout.ToggleSidebar();
        layout.SetInspectorWidth(300);
        Assert.Equal(2, raised);
    }
}
