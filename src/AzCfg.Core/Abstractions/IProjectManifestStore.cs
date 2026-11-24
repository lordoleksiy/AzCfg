using AzCfg.Core.Manifest;

namespace AzCfg.Core.Abstractions;

/// <summary>Finds, reads and writes the shared project manifest <c>azcfg.json</c> (ADR 0012, amendment 2).</summary>
public interface IProjectManifestStore
{
    /// <summary>The file name of the manifest.</summary>
    public const string FileName = "azcfg.json";

    /// <summary>
    /// The nearest <c>azcfg.json</c> from <paramref name="path"/> (a folder, or a file such as a <c>.csproj</c>: then its
    /// folder) upwards, stopping at the git root (the folder that contains <c>.git</c>); without a git root the search goes
    /// up to the filesystem root. <c>null</c> when there is none.
    /// </summary>
    /// <exception cref="ManifestException">The nearest file is unreadable or invalid; <see cref="ManifestException.FilePath"/> is set.</exception>
    Task<ManifestLocation?> FindAsync(string path, CancellationToken ct);

    /// <summary>Validates and writes <paramref name="manifest"/> to <paramref name="filePath"/>, replacing it atomically.</summary>
    /// <exception cref="ManifestException">The manifest is invalid; <see cref="ManifestException.FilePath"/> is set.</exception>
    Task SaveAsync(string filePath, ProjectManifest manifest, CancellationToken ct);
}
