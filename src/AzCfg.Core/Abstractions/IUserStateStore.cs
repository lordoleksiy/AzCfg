using AzCfg.Core.Model;

namespace AzCfg.Core.Abstractions;

public interface IUserStateStore
{
    Task<UserProfile?> LoadProfileAsync(CancellationToken ct);
    Task SaveProfileAsync(UserProfile profile, CancellationToken ct);
    Task<ProjectBinding?> LoadBindingAsync(string repoPath, CancellationToken ct);
    Task SaveBindingAsync(ProjectBinding binding, CancellationToken ct);
    Task<IReadOnlyList<ConfigEntry>?> LoadSnapshotAsync(string repoPath, string environment, CancellationToken ct);
    Task SaveSnapshotAsync(string repoPath, string environment, IReadOnlyList<ConfigEntry> entries, CancellationToken ct);
}
