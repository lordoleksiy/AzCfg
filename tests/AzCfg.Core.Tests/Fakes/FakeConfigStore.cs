using System.Runtime.CompilerServices;
using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Core.Tests.Fakes;

internal sealed class FakeConfigStore(
    Dictionary<string, string[]> prefixesByEnvironment,
    Dictionary<string, ConfigEntry[]>? entriesByEnvironment = null) : IConfigStore
{
    public IAsyncEnumerable<ConfigEntry> ReadAsync(ConfigEnvironment env, string service, CancellationToken ct) =>
        entriesByEnvironment![env.Name].ToAsyncEnumerable();

    public async IAsyncEnumerable<string> ListServicePrefixesAsync(ConfigEnvironment env, [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var prefix in prefixesByEnvironment[env.Name])
        {
            yield return prefix;
        }

        await Task.CompletedTask;
    }

    public Task<ApplyResult> ApplyAsync(ConfigEnvironment env, string service, IReadOnlyList<Change> changes, CancellationToken ct) => throw new NotSupportedException();
}
