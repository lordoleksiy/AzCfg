using AzCfg.Core.Abstractions;
using AzCfg.Core.Manifest;

namespace AzCfg.Infrastructure.Project;

/// <summary>
/// File-based <see cref="IProjectManifestStore"/>: walks up from the given path to the git root (a folder containing a
/// <c>.git</c> folder or file) looking for <c>azcfg.json</c>. A corrupted or invalid file throws
/// <see cref="ManifestException"/> carrying the path; writes go through a temp file and an atomic move.
/// </summary>
public sealed class FileProjectManifestStore : IProjectManifestStore
{
    public async Task<ManifestLocation?> FindAsync(string path, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var directory = File.Exists(fullPath) ? Path.GetDirectoryName(fullPath) : fullPath;

        for (; directory is not null; directory = Path.GetDirectoryName(directory))
        {
            ct.ThrowIfCancellationRequested();
            var candidate = Path.Combine(directory, IProjectManifestStore.FileName);
            if (File.Exists(candidate))
            {
                return new ManifestLocation(await ReadAsync(candidate, ct), candidate);
            }

            if (IsGitRoot(directory))
            {
                return null;
            }
        }

        return null;
    }

    public async Task SaveAsync(string filePath, ProjectManifest manifest, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(manifest);
        var fullPath = Path.GetFullPath(filePath);

        string json;
        try
        {
            json = ManifestSerializer.Serialize(manifest);
        }
        catch (ManifestException e)
        {
            throw e.WithFilePath(fullPath);
        }

        var temp = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllTextAsync(temp, json, ct);
            File.Move(temp, fullPath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Cannot write azcfg manifest '{fullPath}': {e.Message}", e);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Best effort: never mask the original failure.
        }
    }

    private static async Task<ProjectManifest> ReadAsync(string filePath, CancellationToken ct)
    {
        string json;
        try
        {
            json = await File.ReadAllTextAsync(filePath, ct);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new ManifestException(
                [new ManifestError(ManifestErrorKind.Unreadable, null, $"The file cannot be read: {e.Message}")], filePath, e);
        }

        try
        {
            return ManifestSerializer.Deserialize(json);
        }
        catch (ManifestException e)
        {
            throw e.WithFilePath(filePath);
        }
    }

    private static bool IsGitRoot(string directory)
    {
        var git = Path.Combine(directory, ".git");
        return Directory.Exists(git) || File.Exists(git);
    }
}
