namespace AzCfg.Core.Sync;

/// <summary>One key of a comparison. <c>Left</c>/<c>Right</c> are null when the key is absent on that side.</summary>
public sealed record ConfigComparisonItem(string Key, ComparisonKind Kind, ComparisonValue? Left, ComparisonValue? Right);
