using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Core.Abstractions;

public interface IConfigStore
{
    IAsyncEnumerable<ConfigEntry> ReadAsync(ConfigEnvironment env, string service, CancellationToken ct);
    IAsyncEnumerable<string> ListServicePrefixesAsync(ConfigEnvironment env, CancellationToken ct);
    Task<ApplyResult> ApplyAsync(ConfigEnvironment env, string service, IReadOnlyList<Change> changes, CancellationToken ct);
}
