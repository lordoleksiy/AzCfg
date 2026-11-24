using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;

namespace AzCfg.Core.Tests.Fakes;

internal sealed class FakeTrackedSettingsReader : ITrackedSettingsReader
{
    public List<TrackedSettingKey> Keys { get; } = [];

    public bool Throws { get; set; }

    public Task<IReadOnlyList<TrackedSettingKey>> ReadKeysAsync(string repoPath, CancellationToken ct) =>
        Throws ? throw new IOException("boom, value=CANARY") : Task.FromResult<IReadOnlyList<TrackedSettingKey>>(Keys);
}
