namespace AzCfg.Ui.Components.KeyTree;

/// <summary>
/// One visible row of a <see cref="KeyTreeModel{TRow}"/>. <see cref="Item"/> is null for a pure branch (a path prefix that is
/// not a key itself); a key that is also a prefix of other keys has both an item and children. <see cref="Count"/> is the number of
/// items at or below the row that satisfy the model's counter predicate.
/// </summary>
public sealed record KeyTreeRow<TRow>(string Path, string Name, int Depth, TRow? Item, bool HasChildren, bool IsExpanded, int Count)
    where TRow : class
{
    public bool IsLeaf => !HasChildren;
}
