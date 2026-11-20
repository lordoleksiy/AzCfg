using System.Xml.Linq;
using AzCfg.Core.Abstractions;
using AzCfg.Core.Model;

namespace AzCfg.Infrastructure.Local;

/// <summary>
/// Finds .NET projects by searching for <c>*.csproj</c> files under a repository path, never descending into
/// <c>bin</c>, <c>obj</c>, <c>node_modules</c>, <c>.git</c>, <c>.vs</c>, <c>.idea</c> and other dot-folders.
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

        projects.Sort((a, b) => string.CompareOrdinal(a.CsprojPath, b.CsprojPath));

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
                if (IsSkippedDirectory(name))
                {
                    continue;
                }

                pending.Push(subDir);
            }
        }
    }

    internal static bool IsSkippedDirectory(string directoryName) =>
        directoryName.StartsWith('.')
        || string.Equals(directoryName, "bin", StringComparison.OrdinalIgnoreCase)
        || string.Equals(directoryName, "obj", StringComparison.OrdinalIgnoreCase)
        || string.Equals(directoryName, "node_modules", StringComparison.OrdinalIgnoreCase);

    private static ProjectInfo ParseProject(string repoPath, string csprojPath)
    {
        var project = XDocument.Load(csprojPath).Root;

        // Old-style (non-SDK) csproj files declare an MSBuild xmlns, so element names must be
        // matched by LocalName rather than by an exact (namespace-qualified) XName.
        var userSecretsId = project?.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "UserSecretsId")?.Value.Trim();
        var isAzureFunctions = project?.Descendants().Any(e => e.Name.LocalName == "AzureFunctionsVersion") ?? false;

        return new ProjectInfo(
            RepoPath: repoPath,
            CsprojPath: csprojPath,
            Name: Path.GetFileNameWithoutExtension(csprojPath),
            IsAzureFunctions: isAzureFunctions,
            UserSecretsId: string.IsNullOrEmpty(userSecretsId) ? null : userSecretsId,
            IsLibrary: project is null || IsLibrary(project));
    }

    private static bool IsLibrary(XElement project)
    {
        var elements = project.Descendants().ToList();

        var isTestProject = elements.Any(e => e.Name.LocalName == "IsTestProject"
                && string.Equals(e.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase))
            || elements.Any(e => e.Name.LocalName == "PackageReference"
                && string.Equals((string?)e.Attribute("Include"), "Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase));
        if (isTestProject)
        {
            return true;
        }

        var outputType = elements.FirstOrDefault(e => e.Name.LocalName == "OutputType")?.Value.Trim();
        var isExe = string.Equals(outputType, "Exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(outputType, "WinExe", StringComparison.OrdinalIgnoreCase);

        var sdks = new List<string?> { (string?)project.Attribute("Sdk") };
        sdks.AddRange(elements.Where(e => e.Name.LocalName == "Sdk").Select(e => (string?)e.Attribute("Name")));
        var isAppSdk = sdks.Any(IsAppSdk);

        var isAzureFunctions = elements.Any(e => e.Name.LocalName == "AzureFunctionsVersion");

        return !(isExe || isAppSdk || isAzureFunctions);
    }

    private static bool IsAppSdk(string? sdkAttribute) =>
        sdkAttribute is not null
        && sdkAttribute.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(sdk => sdk.Split('/')[0])
            .Any(sdk => string.Equals(sdk, "Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase)
                || string.Equals(sdk, "Microsoft.NET.Sdk.Worker", StringComparison.OrdinalIgnoreCase));
}
