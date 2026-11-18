namespace AzCfg.Ui.Application.Workspace;

/// <summary>
/// <see cref="IDirectoryBrowser"/> over the local file system. Roots are the home folder plus ready drives on Windows, and
/// <c>/</c> on Unix. Unreadable folders list as empty.
/// </summary>
public sealed class DirectoryBrowser : IDirectoryBrowser
{
    private static readonly string[] ProjectPatterns = ["*.sln", "*.slnx", "*.csproj"];

    public Task<DirectoryListing> ListAsync(string? path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(new DirectoryListing(null, null, [.. Roots().Select(r => Item(Path.GetFileName(r.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } n ? n : r, r))]));
        }

        var full = Path.GetFullPath(path);
        return Task.FromResult(new DirectoryListing(full, Directory.GetParent(full)?.FullName, [.. Children(full).Select(d => Item(Path.GetFileName(d), d))]));
    }

    public IReadOnlyList<string> Complete(string partialPath, int max = 20)
    {
        if (string.IsNullOrWhiteSpace(partialPath))
        {
            return [.. Roots().Take(max)];
        }

        var endsWithSeparator = partialPath.EndsWith(Path.DirectorySeparatorChar) || partialPath.EndsWith(Path.AltDirectorySeparatorChar);
        var folder = endsWithSeparator ? partialPath : Path.GetDirectoryName(partialPath);
        var prefix = endsWithSeparator ? "" : Path.GetFileName(partialPath);
        return string.IsNullOrEmpty(folder)
            ? []
            : [.. Children(Path.GetFullPath(folder)).Where(d => Path.GetFileName(d).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Take(max)];
    }

    private static IEnumerable<string> Roots() =>
        OperatingSystem.IsWindows()
            ? [Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), .. DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.RootDirectory.FullName)]
            : [Path.DirectorySeparatorChar.ToString()];

    private static List<string> Children(string folder)
    {
        try
        {
            return [.. new DirectoryInfo(folder).EnumerateDirectories()
                .Where(d => !d.Name.StartsWith('.') && (d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .Select(d => d.FullName)];
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    private static DirectoryItem Item(string name, string fullPath) => new(name, fullPath, HasDotNetProjects(fullPath));

    private static bool HasDotNetProjects(string folder)
    {
        try
        {
            return ProjectPatterns.Any(p => Directory.EnumerateFiles(folder, p).Any());
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
