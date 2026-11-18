using System.Diagnostics;
using AzCfg.Ui.Components.KeyTree;

namespace AzCfg.Ui.Tests.Components.KeyTree;

public sealed class KeyTreeModelTests
{
    internal sealed record Item(string Key, bool Changed = false);

    private static KeyTreeModel<Item> Model(IEnumerable<Item> items, Func<Item, bool>? counter = null, bool expanded = false) =>
        new(items, i => i.Key, counter, expanded);

    private static string[] Paths(KeyTreeModel<Item> model) => [.. model.Flatten().Select(r => r.Path)];

    [Fact]
    public void Flatten_Collapsed_ShowsOnlyTopLevelInOrdinalOrder()
    {
        var model = Model([new("b:x"), new("B"), new("a:y"), new("a:x")]);
        Assert.Equal(["B", "a", "b"], Paths(model));
    }

    [Fact]
    public void Flatten_Expanded_ListsChildrenAfterTheirParent()
    {
        var model = Model([new("Logging:Level:Default"), new("Logging:Level:Other"), new("Db:Host")], expanded: true);
        Assert.Equal(["Db", "Db:Host", "Logging", "Logging:Level", "Logging:Level:Default", "Logging:Level:Other"], Paths(model));
    }

    [Fact]
    public void Flatten_KeyThatIsLeafAndBranch_HasItemAndChildren()
    {
        var model = Model([new("Feature"), new("Feature:On")], expanded: true);
        var rows = model.Flatten();
        var feature = rows[0];
        Assert.Equal("Feature", feature.Item?.Key);
        Assert.True(feature.HasChildren);
        Assert.False(feature.IsLeaf);
        Assert.Equal("Feature:On", rows[1].Path);
        Assert.Equal(1, rows[1].Depth);
    }

    [Fact]
    public void Flatten_PurePrefix_HasNoItem()
    {
        var model = Model([new("A:B")]);
        Assert.Null(model.Flatten()[0].Item);
    }

    [Fact]
    public void Count_AggregatesItemsMatchingThePredicateBelowEachRow()
    {
        var model = Model([new("A:1", true), new("A:2"), new("A:3", true), new("B", true)], i => i.Changed, expanded: true);
        var rows = model.Flatten().ToDictionary(r => r.Path);
        Assert.Equal(2, rows["A"].Count);
        Assert.Equal(1, rows["B"].Count);
        Assert.Equal(1, rows["A:1"].Count);
        Assert.Equal(0, rows["A:2"].Count);
        Assert.Equal(3, model.TotalCount);
    }

    [Fact]
    public void Count_WithoutPredicate_CountsEveryItemIncludingTheBranchKeyItself()
    {
        var model = Model([new("A"), new("A:1"), new("A:2")]);
        Assert.Equal(3, model.Flatten()[0].Count);
    }

    [Fact]
    public void ExpandCollapse_TogglesVisibilityOfChildren()
    {
        var model = Model([new("A:1"), new("A:2")]);
        Assert.Equal(["A"], Paths(model));
        model.Expand("A");
        Assert.Equal(["A", "A:1", "A:2"], Paths(model));
        Assert.True(model.IsExpanded("A"));
        model.Collapse("A");
        Assert.Equal(["A"], Paths(model));
        model.Toggle("A");
        Assert.True(model.IsExpanded("A"));
    }

    [Fact]
    public void ExpandAllCollapseAll_AffectEveryBranch()
    {
        var model = Model([new("A:B:C"), new("D:E")]);
        model.ExpandAll();
        Assert.Equal(["A", "A:B", "A:B:C", "D", "D:E"], Paths(model));
        model.CollapseAll();
        Assert.Equal(["A", "D"], Paths(model));
    }

    [Fact]
    public void Find_CollapsedDescendant_ReturnsItsRow()
    {
        var model = Model([new("A:B:C")]);
        Assert.Equal("C", model.Find("A:B:C")?.Name);
        Assert.Equal(2, model.Find("A:B:C")?.Depth);
        Assert.Null(model.Find("A:Z"));
    }

    [Fact]
    public void New_DuplicateKeys_LastItemWins()
    {
        var model = Model([new("A", false), new("A", true)]);
        Assert.True(model.Flatten()[0].Item!.Changed);
    }

    [Fact]
    public void Flatten_2000Keys_IsFast()
    {
        var items = Enumerable.Range(0, 2000).Select(i => new Item($"Section{i / 50}:Group{i % 5}:Key{i}"));
        var model = Model(items, expanded: true);
        model.Flatten();
        var stopwatch = Stopwatch.StartNew();
        var rows = model.Flatten();
        stopwatch.Stop();
        Assert.Equal(2000 + 5 * 40 + 40, rows.Count);
        Assert.True(stopwatch.ElapsedMilliseconds < 100, $"took {stopwatch.ElapsedMilliseconds} ms");
    }
}
