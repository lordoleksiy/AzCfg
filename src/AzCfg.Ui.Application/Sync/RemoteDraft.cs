using System.Text;
using AzCfg.Core.Model;

namespace AzCfg.Ui.Application.Sync;

/// <summary>
/// Pending Azure edits of one environment (AD-5). <see cref="Json"/>/<see cref="BaseJson"/> never contain secret values.
/// <see cref="object.ToString"/> prints only the environment name and counts.
/// </summary>
public sealed record RemoteDraft(ProjectBinding Binding, ConfigEnvironment Environment, string BaseJson, string Json,
    IReadOnlyDictionary<DraftKey, DraftSecret> Secrets)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Environment = ").Append(Environment.Name).Append(", Secrets = ").Append(Secrets.Count);
        return true;
    }
}
