namespace AzCfg.Ui.Components.KeyTree;

/// <summary>
/// Pure tree of keys split by <c>:</c> (§2.5). Children are in ordinal name order; a key may be a leaf and a branch at once.
/// Branches start collapsed unless <c>initiallyExpanded</c>. When two items share a key the last one wins.
/// </summary>
public sealed class KeyTreeModel<TRow> where TRow : class
{
    private readonly Node _root = new(string.Empty, string.Empty);
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private readonly Func<TRow, bool>? _counter;

    /// <param name="items">Rows to arrange.</param>
    /// <param name="keyOf">Full key of a row, segments separated by <c>:</c>.</param>
    /// <param name="counter">Which items add to <see cref="KeyTreeRow{TRow}.Count"/>; null counts every item.</param>
    /// <param name="initiallyExpanded">Whether every branch starts expanded.</param>
    public KeyTreeModel(IEnumerable<TRow> items, Func<TRow, string> keyOf, Func<TRow, bool>? counter = null, bool initiallyExpanded = false)
    {
        _counter = counter;
        foreach (var item in items)
        {
            Insert(keyOf(item), item);
        }

        Aggregate(_root);
        if (initiallyExpanded)
        {
            ExpandAll();
        }
    }

    /// <summary>Number of rows of the whole tree that satisfy the counter.</summary>
    public int TotalCount => _root.Count;

    public bool IsExpanded(string path) => _expanded.Contains(path);

    public void Expand(string path) => _expanded.Add(path);

    public void Collapse(string path) => _expanded.Remove(path);

    public void Toggle(string path)
    {
        if (!_expanded.Remove(path))
        {
            _expanded.Add(path);
        }
    }

    public void ExpandAll()
    {
        _expanded.Clear();
        Walk(_root, n =>
        {
            if (n.Children.Count > 0)
            {
                _expanded.Add(n.Path);
            }
        });
    }

    public void CollapseAll() => _expanded.Clear();

    /// <summary>The row at <paramref name="path"/> (visible or not), or null.</summary>
    public KeyTreeRow<TRow>? Find(string path)
    {
        var node = _root;
        foreach (var segment in path.Split(':'))
        {
            if (!node.Children.TryGetValue(segment, out node))
            {
                return null;
            }
        }

        return ToRow(node, path.Count(c => c == ':'));
    }

    /// <summary>The visible rows in display order: a branch's children follow it when it is expanded.</summary>
    public IReadOnlyList<KeyTreeRow<TRow>> Flatten()
    {
        var rows = new List<KeyTreeRow<TRow>>();
        Visit(_root, 0);
        return rows;

        void Visit(Node parent, int depth)
        {
            foreach (var child in parent.Sorted)
            {
                rows.Add(ToRow(child, depth));
                if (child.Children.Count > 0 && _expanded.Contains(child.Path))
                {
                    Visit(child, depth + 1);
                }
            }
        }
    }

    private KeyTreeRow<TRow> ToRow(Node node, int depth) =>
        new(node.Path, node.Name, depth, node.Item, node.Children.Count > 0, _expanded.Contains(node.Path), node.Count);

    private void Insert(string key, TRow item)
    {
        var node = _root;
        var path = string.Empty;
        foreach (var segment in key.Split(':'))
        {
            path = path.Length == 0 ? segment : $"{path}:{segment}";
            if (!node.Children.TryGetValue(segment, out var child))
            {
                child = new Node(path, segment);
                node.Children[segment] = child;
            }

            node = child;
        }

        node.Item = item;
    }

    private int Aggregate(Node node)
    {
        node.Sorted = [.. node.Children.Values.OrderBy(c => c.Name, StringComparer.Ordinal)];
        var count = node.Item is { } item && (_counter?.Invoke(item) ?? true) ? 1 : 0;
        foreach (var child in node.Sorted)
        {
            count += Aggregate(child);
        }

        return node.Count = count;
    }

    private static void Walk(Node node, Action<Node> visit)
    {
        visit(node);
        foreach (var child in node.Children.Values)
        {
            Walk(child, visit);
        }
    }

    private sealed class Node(string path, string name)
    {
        public string Path { get; } = path;

        public string Name { get; } = name;

        public TRow? Item { get; set; }

        public Dictionary<string, Node> Children { get; } = new(StringComparer.Ordinal);

        public IReadOnlyList<Node> Sorted { get; set; } = [];

        public int Count { get; set; }
    }
}
