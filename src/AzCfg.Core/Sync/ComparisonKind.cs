namespace AzCfg.Core.Sync;

/// <summary>How one key compares between two effective configurations (<see cref="EnvironmentComparer"/>).</summary>
public enum ComparisonKind { Same, Different, OnlyLeft, OnlyRight }
