using AzCfg.Core.Abstractions;
using AzCfg.Core.Manifest;

namespace AzCfg.Infrastructure.InMemory;

/// <summary>
/// In-memory <see cref="IProjectManifestStore"/>: manifests are kept as JSON by file path (written with
/// <see cref="ManifestSerializer"/>, so a scenario also proves they round-trip). <see cref="FindAsync"/> walks up the path
/// and stops at a folder listed in <see cref="GitRoots"/>. Paths (file keys and git roots) compare like the file system:
/// full path, no trailing separator, case-insensitive on Windows.
/// </summary>
public sealed class InMemoryProjectManifestStore : IProjectManifestStore
{
    private readonly Dictionary<string, string> _files = new(NormalizedPathComparer.Instance);

    /// <summary>Folders treated as git roots (the search does not go above them).</summary>
    public ISet<string> GitRoots { get; } = new HashSet<string>(NormalizedPathComparer.Instance);

    /// <summary>The manifest file exactly as it would be written to disk (<c>null</c> when none was saved).</summary>
    public string? Json(string filePath) => _files.GetValueOrDefault(filePath);

    /// <summary>Places raw content at <paramref name="filePath"/>, e.g. a corrupted file.</summary>
    public void SetJson(string filePath, string json) => _files[filePath] = json;

    public Task<ManifestLocation?> FindAsync(string path, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        for (var directory = Normalize(path); !string.IsNullOrEmpty(directory); directory = Path.GetDirectoryName(directory))
        {
            var candidate = Path.Combine(directory, IProjectManifestStore.FileName);
            if (_files.TryGetValue(candidate, out var json))
            {
                try
                {
                    return Task.FromResult<ManifestLocation?>(new ManifestLocation(ManifestSerializer.Deserialize(json), candidate));
                }
                catch (ManifestException e)
                {
                    throw e.WithFilePath(candidate);
                }
            }

            if (GitRoots.Contains(directory))
            {
                break;
            }
        }

        return Task.FromResult<ManifestLocation?>(null);
    }

    public Task SaveAsync(string filePath, ProjectManifest manifest, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var fullPath = Normalize(filePath);
        try
        {
            _files[fullPath] = ManifestSerializer.Serialize(manifest);
        }
        catch (ManifestException e)
        {
            throw e.WithFilePath(fullPath);
        }

        return Task.CompletedTask;
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private sealed class NormalizedPathComparer : IEqualityComparer<string>
    {
        public static readonly NormalizedPathComparer Instance = new();

        private static readonly StringComparer Inner =
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        public bool Equals(string? x, string? y) =>
            x is null || y is null ? ReferenceEquals(x, y) : Inner.Equals(Normalize(x), Normalize(y));

        public int GetHashCode(string obj) => Inner.GetHashCode(Normalize(obj));
    }
}
