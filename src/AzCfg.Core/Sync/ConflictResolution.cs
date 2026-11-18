namespace AzCfg.Core.Sync;

/// <summary>How <c>ISyncService.ResolveConflictAsync</c> resolves a local-files conflict of one key.</summary>
public enum ConflictResolution
{
    /// <summary>Keep the local value: the snapshot takes the current remote item, so the next push plans the local value.</summary>
    KeepLocal,

    /// <summary>Take the remote value: the key is pulled into the local files.</summary>
    KeepRemote,
}
