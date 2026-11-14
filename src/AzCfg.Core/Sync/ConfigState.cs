namespace AzCfg.Core.Sync;

/// <summary>
/// A normalized snapshot of configuration (local, remote or base), keyed by <see cref="StateItem.Key"/>
/// with an ordinal comparer.
/// </summary>
public sealed record ConfigState(IReadOnlyDictionary<string, StateItem> Items);
