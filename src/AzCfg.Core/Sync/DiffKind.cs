namespace AzCfg.Core.Sync;

/// <summary>Classification of one <see cref="DiffItem"/> in a 3-way diff.</summary>
public enum DiffKind
{
    Unchanged,
    LocalAdded,
    LocalModified,
    LocalDeleted,
    RemoteAdded,
    RemoteModified,
    RemoteDeleted,
    Conflict,
}
