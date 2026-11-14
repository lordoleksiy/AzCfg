using System.Xml.Linq;
using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;

namespace AzCfg.Infrastructure.Local;

/// <summary>
/// Finds .NET projects by searching for <c>*.csproj</c> files under a repository path, skipping
/// <c>bin</c>/<c>obj</c> output directories entirely (they are not descended into).
/// </summary>
public sealed class CsprojDetector : IProjectDetector
{
    public Task<IReadOnlyList<ProjectInfo>> DetectAsync(string repoPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoPath);

        var projects = new List<ProjectInfo>();

        foreach (var csprojPath in FindCsprojFiles(repoPath))
        {
            ct.ThrowIfCancellationRequested();
            projects.Add(ParseProject(repoPath, csprojPath));
        }

        return Task.FromResult<IReadOnlyList<ProjectInfo>>(projects);
    }

    private static IEnumerable<string> FindCsprojFiles(string repoPath)
    {
        if (!Directory.Exists(repoPath))
        {
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(repoPath);

        while (pending.Count > 0)
        {
            var dir = pending.Pop();

            foreach (var file in Directory.EnumerateFiles(dir, "*.csproj"))
            {
                yield return file;
            }

            foreach (var subDir in Directory.EnumerateDirectories(dir))
            {
                var name = Path.GetFileName(subDir);
                if (IsBuildOutputDirectory(name))
                {
                    continue;
                }

                pending.Push(subDir);
            }
        }
    }

    private static bool IsBuildOutputDirectory(string directoryName) =>
        string.Equals(directoryName, "bin", StringComparison.OrdinalIgnoreCase)
        || string.Equals(directoryName, "obj", StringComparison.OrdinalIgnoreCase);

    private static ProjectInfo ParseProject(string repoPath, string csprojPath)
    {
        var project = XDocument.Load(csprojPath).Root;

        var userSecretsId = project?.Descendants("UserSecretsId").FirstOrDefault()?.Value.Trim();
        var isAzureFunctions = project?.Descendants("AzureFunctionsVersion").Any() ?? false;

        return new ProjectInfo(
            RepoPath: repoPath,
            CsprojPath: csprojPath,
            Name: Path.GetFileNameWithoutExtension(csprojPath),
            IsAzureFunctions: isAzureFunctions,
            UserSecretsId: string.IsNullOrEmpty(userSecretsId) ? null : userSecretsId);
    }
}
