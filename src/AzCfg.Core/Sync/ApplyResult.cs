namespace AzCfg.Core.Sync;

public sealed record ApplyResult(int Applied, IReadOnlyList<string> Failed);
