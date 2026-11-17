namespace AzCfg.Core.Sync;

/// <summary>
/// Where the local side of a push comes from. The library default is <see cref="LocalFiles"/> (push from
/// the bound local targets); <c>edit</c> is a push with <see cref="EditedJson"/> as the source.
/// </summary>
public abstract record PushSource
{
    /// <summary>
    /// Push from the local files bound in <c>ProjectBinding</c> (the default), skipping ignored keys (ADR 0008). By default
    /// an upsert: local deletions are not deleted remotely but reported as <see cref="SyncWarningKind.DeleteSkipped"/>;
    /// <paramref name="Delete"/> also plans them as deletes.
    /// </summary>
    public sealed record LocalFiles(bool Delete = false) : PushSource;

    /// <summary>Push from a developer-edited JSON projection (see <c>azcfg edit</c>).</summary>
    public sealed record EditedJson(string Json) : PushSource;

    /// <summary>
    /// Push from a developer-edited promote projection (<c>azcfg promote</c>): keys copied from
    /// <see cref="FromEnvironment"/> that are missing in the target environment, edited for it.
    /// </summary>
    public sealed record Promote(string Json, string FromEnvironment) : PushSource;
}
