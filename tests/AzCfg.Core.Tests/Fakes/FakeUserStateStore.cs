using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Core.Tests.Fakes;

internal sealed class FakeUserStateStore : IUserStateStore
{
    public UserProfile? Profile { get; set; }
    public ProjectBinding? Binding { get; set; }
    public Dictionary<string, ConfigState> Snapshots { get; } = [];
    public (string Environment, ConfigState State)? SavedSnapshot { get; private set; }

    public Task<UserProfile?> LoadProfileAsync(CancellationToken ct) => Task.FromResult(Profile);
    public Task SaveProfileAsync(UserProfile profile, CancellationToken ct)
    {
        Profile = profile;
        return Task.CompletedTask;
    }

    public Task<ProjectBinding?> LoadBindingAsync(string repoPath, CancellationToken ct) => Task.FromResult(Binding);

    public Task SaveBindingAsync(ProjectBinding binding, CancellationToken ct)
    {
        Binding = binding;
        return Task.CompletedTask;
    }

    public Task<ConfigState?> LoadSnapshotAsync(string repoPath, string environment, CancellationToken ct) =>
        Task.FromResult(Snapshots.GetValueOrDefault(environment));
    public Task SaveSnapshotAsync(string repoPath, string environment, ConfigState state, CancellationToken ct)
    {
        SavedSnapshot = (environment, state);
        return Task.CompletedTask;
    }
    public int DeleteSnapshotsCalls { get; private set; }
    public bool DeleteSnapshotsThrows { get; set; }
    public Task DeleteSnapshotsAsync(string repoPath, CancellationToken ct)
    {
        if (DeleteSnapshotsThrows)
        {
            throw new IOException("disk");
        }

        DeleteSnapshotsCalls++;
        Snapshots.Clear();
        return Task.CompletedTask;
    }
    public Task<byte[]> GetOrCreateFingerprintKeyAsync(CancellationToken ct) => throw new NotSupportedException();
}
