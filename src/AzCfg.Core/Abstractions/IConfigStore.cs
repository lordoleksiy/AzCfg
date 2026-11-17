using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Core.Abstractions;

public interface IConfigStore
{
    /// <summary>Reads <c>global:*</c> and <c>&lt;service&gt;:*</c> entries selected by <see cref="ConfigEnvironment.Label"/> (ADR 0008); each entry carries its own label.</summary>
    IAsyncEnumerable<ConfigEntry> ReadAsync(ConfigEnvironment env, string service, CancellationToken ct);
    IAsyncEnumerable<string> ListServicePrefixesAsync(ConfigEnvironment env, CancellationToken ct);
    Task<ApplyResult> ApplyAsync(ConfigEnvironment env, string service, IReadOnlyList<Change> changes, CancellationToken ct);

    /// <summary>Distinct non-empty labels used in the store, ordinal order (offered by <c>init</c>).</summary>
    Task<IReadOnlyList<string>> ListLabelsAsync(Uri storeEndpoint, CancellationToken ct);
}