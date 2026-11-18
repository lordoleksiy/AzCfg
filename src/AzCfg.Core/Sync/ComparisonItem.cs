using AzCfg.Core.Model;

namespace AzCfg.Core.Sync;

/// <summary>One key of two effective configurations (after ScopeMerger). Secret values are never involved.</summary>
public sealed record ComparisonItem(string Key, ComparisonKind Kind, ConfigEntry? Left, ConfigEntry? Right);
