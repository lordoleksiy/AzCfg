using AzCfg.Ui.Components.KeyTree;
using AzCfg.Ui.Tests.Support;
using Bunit;
using Microsoft.AspNetCore.Components;

namespace AzCfg.Ui.Tests.Components.KeyTree;

public sealed class KeyTreeGridTests : UiTestContext
{
    private sealed record Item(string Key, string Value);

    private static KeyTreeModel<Item> Model(IEnumerable<Item> items, bool expanded = true) => new(items, i => i.Key, null, expanded);

    private static KeyTreeColumn<Item> ValueColumn() =>
        new("Value", "1fr", row => b => b.AddContent(0, row.Item?.Value ?? "(group)"));

    private IRenderedComponent<KeyTreeGrid<Item>> RenderGrid(KeyTreeModel<Item> model, Action<ComponentParameterCollectionBuilder<KeyTreeGrid<Item>>>? configure = null) =>
        Render<KeyTreeGrid<Item>>(p =>
        {
            p.Add(g => g.Model, model).Add(g => g.Columns, [ValueColumn()]);
            configure?.Invoke(p);
        });

    private static Item[] Sample() => [new("Db:Host", "localhost"), new("Db:Port", "5432"), new("Api", "on"), new("Api:Key", "k")];

    [Fact]
    public void Render_ShowsKeyColumnCellsAndHeaders()
    {
        var cut = RenderGrid(Model(Sample()));
        Assert.Contains("localhost", cut.Markup);
        Assert.Contains("(group)", cut.Markup);
        Assert.Contains("Value", cut.Find(".az-keytree-header").TextContent);
    }

    [Fact]
    public void Render_2000Rows_RendersOnlyAWindow()
    {
        var items = Enumerable.Range(0, 2000).Select(i => new Item($"Section{i / 50}:Key{i}", "v")).ToList();
        var cut = RenderGrid(Model(items));
        var rendered = cut.FindAll(".az-keytree-row").Count;
        Assert.InRange(rendered, 1, 200);
    }

    [Fact]
    public void Click_Row_SelectsIt()
    {
        string? selected = null;
        var cut = RenderGrid(Model(Sample()), p => p.Add(g => g.SelectedPathChanged, (string? s) => selected = s));
        cut.Find("[data-path='Db:Port']").Click();
        Assert.Equal("Db:Port", selected);
        Assert.Contains("selected", cut.Find("[data-path='Db:Port']").ClassList);
    }

    [Fact]
    public void ArrowDownUp_MovesSelection()
    {
        string? selected = "Api";
        var cut = RenderGrid(Model(Sample()), p => p
            .Add(g => g.SelectedPath, selected)
            .Add(g => g.SelectedPathChanged, (string? s) => selected = s));
        cut.Find("[data-path='Api']").KeyDown("ArrowDown");
        Assert.Equal("Api:Key", selected);
        cut.Find("[data-path='Api:Key']").KeyDown("ArrowUp");
        Assert.Equal("Api", selected);
    }

    [Fact]
    public void ArrowRightLeft_ExpandsAndCollapsesABranch()
    {
        var model = Model(Sample(), expanded: false);
        var cut = RenderGrid(model);
        Assert.False(model.IsExpanded("Db"));
        cut.Find("[data-path='Db']").KeyDown("ArrowRight");
        Assert.True(model.IsExpanded("Db"));
        Assert.NotEmpty(cut.FindAll("[data-path='Db:Host']"));
        cut.Find("[data-path='Db']").KeyDown("ArrowLeft");
        Assert.False(model.IsExpanded("Db"));
        Assert.Empty(cut.FindAll("[data-path='Db:Host']"));
    }

    [Fact]
    public void ArrowLeft_OnLeaf_SelectsTheParent()
    {
        string? selected = null;
        var cut = RenderGrid(Model(Sample()), p => p.Add(g => g.SelectedPathChanged, (string? s) => selected = s));
        cut.Find("[data-path='Db:Host']").KeyDown("ArrowLeft");
        Assert.Equal("Db", selected);
    }

    [Fact]
    public void Enter_RaisesOnActivateWithTheRow()
    {
        string? activated = null;
        var cut = RenderGrid(Model(Sample()), p => p.Add(g => g.OnActivate, (KeyTreeRow<Item> r) => activated = r.Path));
        cut.Find("[data-path='Api:Key']").KeyDown("Enter");
        Assert.Equal("Api:Key", activated);
    }

    [Fact]
    public void Click_Chevron_TogglesWithoutSelecting()
    {
        string? selected = null;
        var model = Model(Sample());
        var cut = RenderGrid(model, p => p.Add(g => g.SelectedPathChanged, (string? s) => selected = s));
        cut.Find("[data-path='Db'] .az-keytree-toggle").Click();
        Assert.False(model.IsExpanded("Db"));
        Assert.Null(selected);
    }

    [Fact]
    public void Render_HighlightText_MarksMatches()
    {
        var cut = RenderGrid(Model(Sample()), p => p.Add(g => g.HighlightText, "Port"));
        Assert.Contains("mark", cut.Find("[data-path='Db:Port']").InnerHtml, StringComparison.OrdinalIgnoreCase);
    }
}
