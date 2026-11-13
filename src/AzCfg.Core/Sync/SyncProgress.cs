namespace AzCfg.Core.Sync;

public sealed record SyncProgress(string Stage, int Done, int Total);
