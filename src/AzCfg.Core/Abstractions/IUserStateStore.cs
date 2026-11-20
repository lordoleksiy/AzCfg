using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Core.Abstractions;

public interface IUserStateStore
{
    Task<UserProfile?> LoadProfileAsync(CancellationToken ct);
    Task SaveProfileAsync(UserProfile profile, CancellationToken ct);
    Task<ProjectBinding?> LoadBindingAsync(string repoPath, CancellationToken ct);
    Task SaveBindingAsync(ProjectBinding binding, CancellationToken ct);

    /// <summary>Loads the last-pull snapshot used for 3-way diff. Never contains secret values (decision 5).</summary>
    Task<ConfigState?> LoadSnapshotAsync(string repoPath, string environment, CancellationToken ct);

    /// <summary>Saves the snapshot. <paramref name="state"/> must not carry secret values (decision 5).</summary>
    Task SaveSnapshotAsync(string repoPath, string environment, ConfigState state, CancellationToken ct);

    /// <summary>Deletes every snapshot of the repo (all environments); a no-op when there are none. Used when the bound service changes (ADR 0014).</summary>
    Task DeleteSnapshotsAsync(string repoPath, CancellationToken ct);

    /// <summary>A per-user random key stored in the user state directory, never in the repo (decision 5).</summary>
    Task<byte[]> GetOrCreateFingerprintKeyAsync(CancellationToken ct);
}
