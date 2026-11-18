using System.Text.Json;
using System.Text.Json.Serialization;
using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Infrastructure.InMemory;

/// <summary>
/// In-memory per-user state: profile, bindings by full repo path, snapshots by <c>(repo, environment)</c> and a fixed fingerprint key.
/// Everything is stored as JSON (same serializer settings as <c>JsonUserStateStore</c>), so a scenario also proves the state
/// round-trips and can inspect what would land on disk.
/// </summary>
public sealed class InMemoryUserStateStore : IUserStateStore
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new JsonStringEnumConverter() } };

    private readonly Dictionary<string, string> _bindings = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Repo, string Env), string> _snapshots = [];
    private string? _profile;

    public byte[] FingerprintKey { get; } = [.. Enumerable.Range(1, 32).Select(i => (byte)i)];

    public UserProfile? Profile => Load<UserProfile>(_profile);

    public ProjectBinding? Binding(string repoPath) => Load<ProjectBinding>(_bindings.GetValueOrDefault(repoPath));

    public ConfigState? Snapshot(string repoPath, string environment) => Load<ConfigState>(SnapshotJson(repoPath, environment));

    /// <summary>The snapshot exactly as it would be written to disk (<c>null</c> when none was saved).</summary>
    public string? SnapshotJson(string repoPath, string environment) => _snapshots.GetValueOrDefault((repoPath, environment));

    public Task<UserProfile?> LoadProfileAsync(CancellationToken ct) => Task.FromResult(Profile);

    public Task SaveProfileAsync(UserProfile profile, CancellationToken ct)
    {
        _profile = JsonSerializer.Serialize(profile, Options);
        return Task.CompletedTask;
    }

    public Task<ProjectBinding?> LoadBindingAsync(string repoPath, CancellationToken ct) => Task.FromResult(Binding(repoPath));

    public Task SaveBindingAsync(ProjectBinding binding, CancellationToken ct)
    {
        _bindings[binding.RepoPath] = JsonSerializer.Serialize(binding, Options);
        return Task.CompletedTask;
    }

    public Task<ConfigState?> LoadSnapshotAsync(string repoPath, string environment, CancellationToken ct) => Task.FromResult(Snapshot(repoPath, environment));

    public Task SaveSnapshotAsync(string repoPath, string environment, ConfigState state, CancellationToken ct)
    {
        _snapshots[(repoPath, environment)] = JsonSerializer.Serialize(state, Options);
        return Task.CompletedTask;
    }

    public Task<byte[]> GetOrCreateFingerprintKeyAsync(CancellationToken ct) => Task.FromResult(FingerprintKey);

    private static T? Load<T>(string? json) where T : class => json is null ? null : JsonSerializer.Deserialize<T>(json, Options);
}
