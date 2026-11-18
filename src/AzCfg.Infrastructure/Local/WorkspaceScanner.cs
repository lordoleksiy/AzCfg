using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;
using Microsoft.Extensions.Configuration.UserSecrets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;

namespace AzCfg.Infrastructure.Local;

/// <summary>
/// <see cref="IWorkspaceScanner"/> over the file system: <c>*.sln</c>/<c>*.slnx</c> files (read with SolutionPersistence),
/// projects from <see cref="IProjectDetector"/> and the well-known config files of each project folder. Directories skipped
/// by <see cref="CsprojDetector"/> are skipped here too. An unreadable solution is logged and skipped.
/// </summary>
public sealed class WorkspaceScanner(IProjectDetector detector, ILogger<WorkspaceScanner>? logger = null) : IWorkspaceScanner
{
    private readonly ILogger _logger = logger ?? NullLogger<WorkspaceScanner>.Instance;

    public async Task<WorkspaceScan> ScanAsync(string rootPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var root = Path.GetFullPath(rootPath);
        var solutions = new List<SolutionInfo>();

        foreach (var path in FindSolutionFiles(root))
        {
            ct.ThrowIfCancellationRequested();
            if (await TryReadSolutionAsync(path, ct) is { } solution)
            {
                solutions.Add(solution);
            }
        }

        var projects = (await detector.DetectAsync(root, ct))
            .Select(p => new WorkspaceProject(p, ConfigFilesOf(p)))
            .ToList();
        solutions.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));

        return new WorkspaceScan(root, solutions, projects);
    }

    private static IEnumerable<string> FindSolutionFiles(string root)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var dir = pending.Pop();

            foreach (var file in Directory.EnumerateFiles(dir).Where(IsSolutionFile))
            {
                yield return file;
            }

            foreach (var subDir in Directory.EnumerateDirectories(dir).Where(d => !CsprojDetector.IsSkippedDirectory(Path.GetFileName(d))))
            {
                pending.Push(subDir);
            }
        }
    }

    private static bool IsSolutionFile(string path) =>
        Path.GetExtension(path).Equals(".sln", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(path).Equals(".slnx", StringComparison.OrdinalIgnoreCase);

    private async Task<SolutionInfo?> TryReadSolutionAsync(string path, CancellationToken ct)
    {
        try
        {
            var model = await SolutionSerializers.GetSerializerByMoniker(path)!.OpenAsync(path, ct);
            var dir = Path.GetDirectoryName(path)!;
            var projectPaths = model.SolutionProjects
                .Where(p => p.FilePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                .Select(p => Path.GetFullPath(Path.Combine(dir, p.FilePath.Replace('\\', Path.DirectorySeparatorChar))))
                .ToList();
            var format = Path.GetExtension(path).Equals(".slnx", StringComparison.OrdinalIgnoreCase) ? SolutionFormat.Slnx : SolutionFormat.Sln;
            return new SolutionInfo(path, Path.GetFileNameWithoutExtension(path), format, projectPaths);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Skipping unreadable solution {Solution}: {ExceptionType}", path, ex.GetType().Name);
            return null;
        }
    }

    private static List<ConfigFileInfo> ConfigFilesOf(ProjectInfo project)
    {
        var dir = Path.GetDirectoryName(project.CsprojPath)!;
        var files = new List<ConfigFileInfo>();
        var appSettings = Path.Combine(dir, "appsettings.json");
        if (File.Exists(appSettings))
        {
            files.Add(new ConfigFileInfo(appSettings, ConfigFileKind.AppSettings, null, true));
        }

        var environmentFiles = Directory.EnumerateFiles(dir, "appsettings.*.json")
            .Select(f => (Path: f, Name: Path.GetFileName(f)))
            .Where(f => f.Name.StartsWith("appsettings.", StringComparison.OrdinalIgnoreCase) && f.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Select(f => (f.Path, Env: f.Name["appsettings.".Length..^".json".Length]))
            .Where(f => f.Env.Length > 0)
            .OrderBy(f => f.Path, StringComparer.Ordinal);
        foreach (var (path, env) in environmentFiles)
        {
            files.Add(env.Equals("local", StringComparison.OrdinalIgnoreCase)
                ? new ConfigFileInfo(path, ConfigFileKind.AppSettingsLocal, null, true)
                : new ConfigFileInfo(path, ConfigFileKind.AppSettingsEnvironment, env, true));
        }

        var localSettings = Path.Combine(dir, "local.settings.json");
        if (File.Exists(localSettings))
        {
            files.Add(new ConfigFileInfo(localSettings, ConfigFileKind.LocalSettings, null, true));
        }

        if (project.UserSecretsId is { } id)
        {
            var secrets = PathHelper.GetSecretsPathFromSecretsId(id);
            files.Add(new ConfigFileInfo(secrets, ConfigFileKind.UserSecrets, null, File.Exists(secrets)));
        }

        return files;
    }
}
