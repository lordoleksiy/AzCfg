using AzCfg.Core.Abstractions;
using AzCfg.Core.Manifest;

namespace AzCfg.Core.Tests.Fakes;

/// <summary>Manifests by file path; <see cref="FindAsync"/> walks up the path string (no git root). Records saves.</summary>
internal sealed class FakeProjectManifestStore : IProjectManifestStore
{
    public Dictionary<string, ProjectManifest> Files { get; } = new(StringComparer.Ordinal);

    public List<(string FilePath, ProjectManifest Manifest)> Saved { get; } = [];

    /// <summary>When set, <see cref="FindAsync"/> throws it (e.g. a corrupted file).</summary>
    public ManifestException? FindError { get; set; }

    public Task<ManifestLocation?> FindAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (FindError is { } error)
        {
            throw error;
        }

        for (var directory = Path.TrimEndingDirectorySeparator(path); !string.IsNullOrEmpty(directory); directory = Path.GetDirectoryName(directory))
        {
            // Keys compare as full paths, so "/repo\azcfg.json" and "\repo\azcfg.json" are the same file; the stored key is returned.
            var candidate = Path.GetFullPath(Path.Combine(directory, IProjectManifestStore.FileName));
            foreach (var (file, manifest) in Files)
            {
                if (string.Equals(Path.GetFullPath(file), candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult<ManifestLocation?>(new ManifestLocation(manifest, file));
                }
            }
        }

        return Task.FromResult<ManifestLocation?>(null);
    }

    public Task SaveAsync(string filePath, ProjectManifest manifest, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Files[filePath] = manifest;
        Saved.Add((filePath, manifest));
        return Task.CompletedTask;
    }
}
