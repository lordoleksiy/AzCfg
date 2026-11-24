using AzCfg.Core.Abstractions;
using AzCfg.Core.Manifest;
using AzCfg.Core.Model;

namespace AzCfg.Core.UseCases;

/// <summary>
/// Maps the shared fields between a <see cref="ProjectBinding"/> (absolute <c>.csproj</c> paths, per user) and a
/// <see cref="ProjectManifest"/> (target names of <c>config project.*</c>, paths relative to the manifest folder; ADR 0012
/// amendment 2). Never carries a value, an active environment or ignore patterns.
/// </summary>
internal static class ManifestBinding
{
    /// <summary>The manifest describing <paramref name="binding"/>'s shared fields, for the file <paramref name="manifestPath"/>.</summary>
    /// <exception cref="InvalidOperationException">A target project lies outside the manifest folder.</exception>
    public static ProjectManifest FromBinding(ProjectBinding binding, string manifestPath) => new(
        ManifestValidator.SupportedSchemaVersion,
        binding.Service,
        Target(binding.ValuesTarget, manifestPath, "values"),
        Target(binding.SecretsTarget, manifestPath, "secrets"),
        Vaults(binding.VaultByEnvironment));

    /// <summary><paramref name="target"/> as a manifest target relative to the folder of <paramref name="manifestPath"/>.</summary>
    /// <exception cref="InvalidOperationException">The project lies outside the manifest folder.</exception>
    public static ManifestTarget Target(LocalTargetBinding target, string manifestPath, string field)
    {
        var folder = FolderOf(manifestPath);
        var relative = Path.GetRelativePath(folder, ProjectLookup.Full(folder, target.ProjectPath)).Replace('\\', '/');
        if (relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            throw new InvalidOperationException(
                $"Cannot share '{field}' via '{manifestPath}': project '{target.ProjectPath}' is outside the manifest folder.");
        }

        return new(KindName(target.Kind), relative);
    }

    /// <summary>
    /// <c>true</c> when <paramref name="target"/> is what <paramref name="shared"/> names: same target kind and the project is
    /// the <c>.csproj</c> or its folder (resolved against the manifest folder).
    /// </summary>
    public static bool Matches(LocalTargetBinding target, ManifestTarget shared, string manifestPath)
    {
        if (!KindName(target.Kind).Equals(shared.Target, StringComparison.Ordinal))
        {
            return false;
        }

        var folder = FolderOf(manifestPath);
        var wanted = ProjectLookup.Full(folder, shared.Project);
        var csproj = ProjectLookup.Full(folder, target.ProjectPath);
        return ProjectLookup.SamePath(csproj, wanted) || ProjectLookup.SamePath(Path.GetDirectoryName(csproj) ?? "", wanted);
    }

    /// <summary>The vaults for a manifest; <c>null</c> when there are none (the field is omitted).</summary>
    public static IReadOnlyDictionary<string, Uri>? Vaults(IReadOnlyDictionary<string, Uri> vaults) =>
        vaults.Count == 0 ? null : vaults.ToDictionary(p => p.Key, p => p.Value);

    public static string KindName(LocalTargetKind kind) => ProjectLookup.KindNames.First(k => k.Kind == kind).Name;

    public static string FolderOf(string manifestPath) => Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;

    /// <summary>
    /// <c>true</c> when the manifest describes the project at <paramref name="projectPath"/> (a folder, or a <c>.csproj</c>: its folder).
    /// For one of its targets, with T = the target's project folder (the <c>.csproj</c>'s folder, or the named folder): T is the
    /// project folder; or T lies inside it (resolving from a repository root); or the project folder lies inside T and no other
    /// <c>.csproj</c> sits between them (a subfolder of the target project, not a nested project such as <c>Orders/tests/Orders.Tests</c>).
    /// A manifest found upward that names another project (e.g. a monorepo root manifest of service A, resolved from <c>services/B</c>)
    /// does not apply.
    /// </summary>
    public static async Task<bool> AppliesToAsync(ManifestLocation location, string projectPath, IProjectDetector detector, CancellationToken ct)
    {
        var folder = FolderOf(location.FilePath);
        var project = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectPath));
        if (project.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            project = Path.GetDirectoryName(project)!;
        }

        foreach (var target in new[] { location.Manifest.Values, location.Manifest.Secrets })
        {
            var full = Path.TrimEndingDirectorySeparator(ProjectLookup.Full(folder, target.Project));
            var targetFolder = full.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(full)! : full;
            if (IsSameOrUnder(targetFolder, project))
            {
                return true;
            }

            if (IsSameOrUnder(project, targetFolder))
            {
                // A project of its own between the target folder (exclusive) and the project folder (inclusive) makes it another project.
                var nested = (await detector.DetectAsync(targetFolder, ct))
                    .Select(p => Path.GetDirectoryName(Path.GetFullPath(p.CsprojPath))!)
                    .Any(f => !ProjectLookup.SamePath(f, targetFolder) && IsSameOrUnder(f, targetFolder) && IsSameOrUnder(project, f));
                if (!nested)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary><paramref name="path"/> is <paramref name="folder"/> or inside it (file-system case rules; another drive is never inside).</summary>
    private static bool IsSameOrUnder(string path, string folder)
    {
        var relative = Path.GetRelativePath(folder, path);
        return relative == "."
            || (!Path.IsPathRooted(relative)
                && relative != ".."
                && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !relative.StartsWith("../", StringComparison.Ordinal));
    }
}
