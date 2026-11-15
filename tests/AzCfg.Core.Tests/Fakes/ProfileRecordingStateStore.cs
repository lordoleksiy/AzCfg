using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Core.Tests.Fakes;

public sealed class ProfileRecordingStateStore : IUserStateStore
{
    public UserProfile? Saved { get; private set; }

    public Task SaveProfileAsync(UserProfile profile, CancellationToken ct)
    {
        Saved = profile;
        return Task.CompletedTask;
    }

    public Task<UserProfile?> LoadProfileAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<ProjectBinding?> LoadBindingAsync(string repoPath, CancellationToken ct) => throw new NotSupportedException();
    public Task SaveBindingAsync(ProjectBinding binding, CancellationToken ct) => throw new NotSupportedException();
    public Task<ConfigState?> LoadSnapshotAsync(string repoPath, string environment, CancellationToken ct) => throw new NotSupportedException();
    public Task SaveSnapshotAsync(string repoPath, string environment, ConfigState state, CancellationToken ct) => throw new NotSupportedException();
    public Task<byte[]> GetOrCreateFingerprintKeyAsync(CancellationToken ct) => throw new NotSupportedException();
}
