namespace AzCfg.Core.Sync;

/// <summary>Kinds of <see cref="SyncWarning"/> (ADR 0008).</summary>
public enum SyncWarningKind
{
    /// <summary>Store mode: the same key exists under several labels; one copy was chosen.</summary>
    DuplicateLabels,

    /// <summary>Label mode: the key exists only under another label (or none); the change adds an override instead.</summary>
    LabelOverride,

    /// <summary>A local deletion is not applied remotely (upsert push, or the key lives under another label).</summary>
    DeleteSkipped,
}
