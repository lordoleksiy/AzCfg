using AzCfg.Core.UseCases;

namespace AzCfg.Cli.Tests.Commands;

/// <summary><see cref="IProjectSettingsService"/> that records <see cref="SetAsync"/> calls; <c>project.shared</c> answers like Core.</summary>
internal sealed class StubProjectSettings : IProjectSettingsService
{
    public List<(string RepoPath, string Key, string? Value)> Sets { get; } = [];

    public Task<IReadOnlyList<UserSetting>> ListAsync(string repoPath, CancellationToken ct) => throw new NotSupportedException();

    public Task<UserSetting> GetAsync(string repoPath, string key, CancellationToken ct) => throw new NotSupportedException();

    public Task<UserSetting> SetAsync(string repoPath, string key, string? value, CancellationToken ct)
    {
        Sets.Add((repoPath, key, value));
        return Task.FromResult(new UserSetting(key, value) { Source = Path.Combine(repoPath, "azcfg.json"), Hint = ProjectSettingKeys.SharedHint });
    }
}
