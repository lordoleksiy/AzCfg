using System.Runtime.CompilerServices;
using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Core.Tests.Fakes;

internal sealed class FakeConfigStore(
    Dictionary<string, string[]> prefixesByEnvironment,
    Dictionary<string, ConfigEntry[]>? entriesByEnvironment = null,
    List<string>? calls = null) : IConfigStore
{
    public IReadOnlyList<Change>? Applied { get; private set; }
    public string[] FailKeys { get; set; } = [];
    public string[] Labels { get; set; } = [];

    public Task<IReadOnlyList<string>> ListLabelsAsync(Uri storeEndpoint, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(Labels);

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

    public Task<ApplyResult> ApplyAsync(ConfigEnvironment env, string service, IReadOnlyList<Change> changes, CancellationToken ct)
    {
        calls?.Add("apply");
        Applied = changes;
        var failed = changes.Select(c => (c.After ?? c.Before)!.Key).Where(FailKeys.Contains).ToList();
        return Task.FromResult(new ApplyResult(changes.Count - failed.Count, failed));
    }
}
