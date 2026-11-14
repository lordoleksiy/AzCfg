using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Core.Tests.Fakes;

internal sealed class FakeUserStateStore : IUserStateStore
{
    public UserProfile? Profile { get; set; }
    public ProjectBinding? Binding { get; set; }

    public Task<UserProfile?> LoadProfileAsync(CancellationToken ct) => Task.FromResult(Profile);
    public Task SaveProfileAsync(UserProfile profile, CancellationToken ct) => throw new NotSupportedException();
    public Task<ProjectBinding?> LoadBindingAsync(string repoPath, CancellationToken ct) => Task.FromResult(Binding);

    public Task SaveBindingAsync(ProjectBinding binding, CancellationToken ct)
    {
        Binding = binding;
        return Task.CompletedTask;
    }

    public Task<ConfigState?> LoadSnapshotAsync(string repoPath, string environment, CancellationToken ct) => throw new NotSupportedException();
    public Task SaveSnapshotAsync(string repoPath, string environment, ConfigState state, CancellationToken ct) => throw new NotSupportedException();
    public Task<byte[]> GetOrCreateFingerprintKeyAsync(CancellationToken ct) => throw new NotSupportedException();
}
