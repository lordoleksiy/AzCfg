using AzCfg.Core.Abstractions;
using AzCfg.Core.Manifest;

namespace AzCfg.Infrastructure.InMemory;

/// <summary>
/// In-memory <see cref="IProjectManifestStore"/>: manifests are kept as JSON by file path (written with
/// <see cref="ManifestSerializer"/>, so a scenario also proves they round-trip). <see cref="FindAsync"/> walks up the path
/// string and stops at a folder listed in <see cref="GitRoots"/>.
/// </summary>
public sealed class InMemoryProjectManifestStore : IProjectManifestStore
{
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);

    /// <summary>Folders treated as git roots (the search does not go above them).</summary>
    public ISet<string> GitRoots { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>The manifest file exactly as it would be written to disk (<c>null</c> when none was saved).</summary>
    public string? Json(string filePath) => _files.GetValueOrDefault(filePath);

    /// <summary>Places raw content at <paramref name="filePath"/>, e.g. a corrupted file.</summary>
    public void SetJson(string filePath, string json) => _files[filePath] = json;

    public Task<ManifestLocation?> FindAsync(string path, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        for (var directory = Path.TrimEndingDirectorySeparator(path); !string.IsNullOrEmpty(directory); directory = Path.GetDirectoryName(directory))
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
        try
        {
            _files[filePath] = ManifestSerializer.Serialize(manifest);
        }
        catch (ManifestException e)
        {
            throw e.WithFilePath(filePath);
        }

        return Task.CompletedTask;
    }
}
