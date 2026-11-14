namespace AzCfg.Core.Sync;

/// <summary>The full 3-way diff; a future UI uses this to compare local vs remote vs base.</summary>
public sealed record ConfigDiff(IReadOnlyList<DiffItem> Items);
