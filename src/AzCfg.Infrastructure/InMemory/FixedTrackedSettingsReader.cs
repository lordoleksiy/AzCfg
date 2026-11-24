using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;

namespace AzCfg.Infrastructure.InMemory;

/// <summary>Returns a fixed list of tracked setting keys regardless of the path (no git, no file system access).</summary>
public sealed class FixedTrackedSettingsReader(params TrackedSettingKey[] keys) : ITrackedSettingsReader
{
    public Task<IReadOnlyList<TrackedSettingKey>> ReadKeysAsync(string repoPath, CancellationToken ct) => Task.FromResult<IReadOnlyList<TrackedSettingKey>>(keys);
}
