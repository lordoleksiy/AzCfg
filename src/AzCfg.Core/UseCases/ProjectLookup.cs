using AzCfg.Core.Model;

namespace AzCfg.Core.UseCases;

/// <summary>Resolves a project path (a .csproj or its directory, relative to the repository) to a detected project.</summary>
internal static class ProjectLookup
{
    /// <summary>The <c>&lt;target&gt;</c> tokens of the <c>project.values</c> / <c>project.secrets</c> settings.</summary>
    public static readonly (string Name, LocalTargetKind Kind)[] KindNames =
    [
        ("user-secrets", LocalTargetKind.UserSecrets),
        ("appsettings-local", LocalTargetKind.AppSettingsLocal),
        ("local-settings", LocalTargetKind.LocalSettings),
    ];

    /// <summary>The project whose .csproj is <paramref name="path"/>, or the only project in directory <paramref name="path"/>; otherwise <c>null</c>.</summary>
    public static ProjectInfo? Find(IReadOnlyList<ProjectInfo> projects, string repoPath, string path)
    {
        var wanted = Full(repoPath, path);
        return projects.FirstOrDefault(p => SamePath(Full(repoPath, p.CsprojPath), wanted))
            ?? (projects.Where(p => SamePath(Full(repoPath, Path.GetDirectoryName(p.CsprojPath) ?? ""), wanted)).ToList() is [var only] ? only : null);
    }

    private static string Full(string repoPath, string path) => Path.GetFullPath(path, Path.GetFullPath(repoPath));

    private static bool SamePath(string a, string b) =>
        string.Equals(
            a.TrimEnd(Path.DirectorySeparatorChar),
            b.TrimEnd(Path.DirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
