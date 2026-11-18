using System.Text;

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

    /// <summary>
    /// Push from developer-edited JSON (<c>azcfg edit</c>, UI drafts). <paramref name="BaseJson"/> = the edit JSON the edit
    /// started from. With <paramref name="BaseJson"/>, <c>PlanPushAsync</c> uses it (parsed with <c>FromEditJson</c> and the
    /// known secrets) as the 3-way base instead of the current remote: Azure changes made after the export are not
    /// reverted, keys changed on both sides become conflicts. Without <paramref name="BaseJson"/> the behaviour is
    /// unchanged (base = current remote). <see cref="object.ToString"/> never prints either JSON (it may hold secret values).
    /// </summary>
    public sealed record EditedJson(string Json, string? BaseJson = null) : PushSource
    {
        protected override bool PrintMembers(StringBuilder builder)
        {
            builder.Append(nameof(Json)).Append(" = ***, ").Append(nameof(BaseJson)).Append(" = ").Append(BaseJson is null ? "null" : "***");
            return true;
        }
    }

    /// <summary>
    /// Push from a developer-edited promote projection (<c>azcfg promote</c>): keys copied from
    /// <see cref="FromEnvironment"/> that are missing in the target environment, edited for it.
    /// <see cref="object.ToString"/> never prints the JSON (it may hold secret values).
    /// </summary>
    public sealed record Promote(string Json, string FromEnvironment) : PushSource
    {
        protected override bool PrintMembers(StringBuilder builder)
        {
            builder.Append(nameof(Json)).Append(" = ***, ").Append(nameof(FromEnvironment)).Append(" = ").Append(FromEnvironment);
            return true;
        }
    }
}
