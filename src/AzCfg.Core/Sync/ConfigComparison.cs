namespace AzCfg.Core.Sync;

/// <summary>Result of comparing two sides; <see cref="IgnoredCount"/> = keys skipped by the project's ignore rules.</summary>
public sealed record ConfigComparison(IReadOnlyList<ConfigComparisonItem> Items, int IgnoredCount);
