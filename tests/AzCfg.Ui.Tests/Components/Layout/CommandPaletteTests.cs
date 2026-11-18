using AzCfg.Ui.Application.State;
using AzCfg.Ui.Components.Layout;
using AzCfg.Ui.Features.Platform;
using AzCfg.Ui.Shared;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace AzCfg.Ui.Tests.Components.Layout;

public sealed class CommandPaletteTests : WorkbenchContext
{
    private PaletteState Palette => Services.GetRequiredService<PaletteState>();

    [Fact]
    public void Render_Closed_ShowsNothing() => Assert.Empty(Render<CommandPalette>().FindAll(".az-palette"));

    [Fact]
    public void Open_ListsCommandsWithShortcuts()
    {
        var cut = Render<CommandPalette>();
        cut.InvokeAsync(() => Palette.Open(PaletteMode.Commands));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".az-palette-item")));
        Assert.Contains("Show command palette", cut.Markup);
        Assert.Contains("Ctrl+K", cut.Markup);
    }

    [Fact]
    public void Input_Query_FiltersAndEnterRunsTheFirstCommand()
    {
        var cut = Render<CommandPalette>();
        cut.InvokeAsync(() => Palette.Open(PaletteMode.Commands));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".az-palette-item")));
        cut.Find("input").Input("toggle side");
        Assert.Single(cut.FindAll(".az-palette-item"));
        var layout = Services.GetRequiredService<LayoutState>();
        cut.Find("input").KeyDown("Enter");
        Assert.False(layout.SidebarVisible);
        Assert.False(Palette.IsOpen);
    }

    [Fact]
    public void KeyDown_ArrowDown_MovesTheActiveItem()
    {
        var cut = Render<CommandPalette>();
        cut.InvokeAsync(() => Palette.Open(PaletteMode.Commands));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".az-palette-item")));
        Assert.Contains("active", cut.FindAll(".az-palette-item")[0].ClassList);
        cut.Find("input").KeyDown("ArrowDown");
        Assert.Contains("active", cut.FindAll(".az-palette-item")[1].ClassList);
        cut.Find("input").KeyDown("ArrowUp");
        Assert.Contains("active", cut.FindAll(".az-palette-item")[0].ClassList);
    }

    [Fact]
    public void KeyDown_Escape_ClosesThePalette()
    {
        var cut = Render<CommandPalette>();
        cut.InvokeAsync(() => Palette.Open(PaletteMode.Commands));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".az-palette")));
        cut.Find("input").KeyDown("Escape");
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".az-palette")));
    }

    [Fact]
    public void Open_DisabledCommand_IsMarkedAndNotExecutedOnClick()
    {
        var cut = Render<CommandPalette>();
        cut.InvokeAsync(() => Palette.Open(PaletteMode.Commands));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".az-palette-item")));
        cut.Find("input").Input("next tab");
        var item = Assert.Single(cut.FindAll(".az-palette-item"));
        Assert.Contains("disabled", item.ClassList);
        item.Click();
        Assert.True(Palette.IsOpen);
    }

    [Fact]
    public void QuickOpen_ShowsProjectAndKeyPlaceholder()
    {
        var cut = Render<CommandPalette>();
        cut.InvokeAsync(() => Palette.Open(PaletteMode.QuickOpen));
        cut.WaitForAssertion(() => Assert.Contains("Go to project or key", cut.Find("input").GetAttribute("placeholder")));
    }

    [Fact]
    public void Shortcut_CommandsFromRegistry_AreTheSameAsKeyBindings()
    {
        var registry = Services.GetRequiredService<ICommandRegistry>();
        var palette = registry.All.Single(c => c.Id == CommandIds.Palette);
        Assert.Equal("Ctrl+K", palette.Shortcut);
    }

    [Fact]
    public async Task ExecuteAsync_NextTab_ActivatesTheNextTabCyclically()
    {
        var tabs = Services.GetRequiredService<TabsState>();
        tabs.Open(Project("A"), "A");
        tabs.Open(Project("B"), "B");
        var first = tabs.Tabs[0].Id;
        var registry = Services.GetRequiredService<ICommandRegistry>();
        Assert.Equal(tabs.Tabs[1].Id, tabs.ActiveId);
        Assert.True(await registry.ExecuteAsync(CommandIds.NextTab));
        Assert.Equal(first, tabs.ActiveId);
    }
}
