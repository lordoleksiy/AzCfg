namespace AzCfg.Core.Sync;

/// <summary>
/// A non-fatal condition found while syncing, returned as data (Core never prints, ADR 0003/0008).
/// <see cref="Key"/> is the configuration path; <see cref="Message"/> never contains a secret value.
/// </summary>
public sealed record SyncWarning(SyncWarningKind Kind, string Key, string Message);