using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;
using AzCfg.Core.Sync;

namespace AzCfg.Infrastructure.State;

/// <summary>
/// File-based <see cref="IUserStateStore"/> under <paramref name="root"/>: profile.json, projects.json (keyed by
/// full repo path), snapshots/&lt;repo-hash&gt;/&lt;env&gt;.json and fingerprint.key. Missing files load as <c>null</c>;
/// writes go through a temp file and an atomic move.
/// </summary>
public sealed class JsonUserStateStore(string root) : IUserStateStore
{
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "azcfg");

    public Task<UserProfile?> LoadProfileAsync(CancellationToken ct) => ReadAsync<UserProfile>(Path.Combine(root, "profile.json"), ct);

    public Task SaveProfileAsync(UserProfile profile, CancellationToken ct) =>
        WriteAsync(Path.Combine(root, "profile.json"), JsonSerializer.SerializeToUtf8Bytes(profile, Options), ct);

    public async Task<ProjectBinding?> LoadBindingAsync(string repoPath, CancellationToken ct) =>
        (await LoadBindingsAsync(ct)).GetValueOrDefault(Path.GetFullPath(repoPath));

    public async Task SaveBindingAsync(ProjectBinding binding, CancellationToken ct)
    {
        var bindings = await LoadBindingsAsync(ct);
        bindings[Path.GetFullPath(binding.RepoPath)] = binding;
        await WriteAsync(Path.Combine(root, "projects.json"), JsonSerializer.SerializeToUtf8Bytes(bindings, Options), ct);
    }

    public Task<ConfigState?> LoadSnapshotAsync(string repoPath, string environment, CancellationToken ct) =>
        ReadAsync<ConfigState>(SnapshotPath(repoPath, environment), ct);

    public Task SaveSnapshotAsync(string repoPath, string environment, ConfigState state, CancellationToken ct) =>
        WriteAsync(SnapshotPath(repoPath, environment), JsonSerializer.SerializeToUtf8Bytes(state, Options), ct);

    public Task DeleteSnapshotsAsync(string repoPath, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(SnapshotPath(repoPath, "x"))!;
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        return Task.CompletedTask;
    }

    public async Task<byte[]> GetOrCreateFingerprintKeyAsync(CancellationToken ct)
    {
        var path = Path.Combine(root, "fingerprint.key");
        if (File.Exists(path))
        {
            return await File.ReadAllBytesAsync(path, ct);
        }

        var key = RandomNumberGenerator.GetBytes(32);
        await WriteAsync(path, key, ct);
        return key;
    }

    private async Task<Dictionary<string, ProjectBinding>> LoadBindingsAsync(CancellationToken ct) =>
        new(await ReadAsync<Dictionary<string, ProjectBinding>>(Path.Combine(root, "projects.json"), ct) ?? [], PathComparer);

    private string SnapshotPath(string repoPath, string environment)
    {
        var fullPath = Path.GetFullPath(repoPath);
        var key = OperatingSystem.IsWindows() ? fullPath.ToUpperInvariant() : fullPath;
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(root, "snapshots", hash, environment + ".json");
    }

    private static async Task<T?> ReadAsync<T>(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, Options, ct);
    }

    private static async Task WriteAsync(string path, byte[] content, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllBytesAsync(temp, content, ct);
        File.Move(temp, path, overwrite: true);
    }
}
