using AzCfg.Core.UseCases;

namespace AzCfg.Ui.Tests.Features.Environments.Fakes;

internal sealed class FakeProjectSettingsService : IProjectSettingsService
{
    public List<string> Listed { get; } = [];

    public IReadOnlyList<UserSetting> Settings { get; set; } = [];

    public Task<IReadOnlyList<UserSetting>> ListAsync(string repoPath, CancellationToken ct)
    {
        Listed.Add(repoPath);
        return Task.FromResult(Settings);
    }

    public Task<UserSetting> GetAsync(string repoPath, string key, CancellationToken ct) => throw new NotSupportedException();

    public Task<UserSetting> SetAsync(string repoPath, string key, string? value, CancellationToken ct) => throw new NotSupportedException();
}
