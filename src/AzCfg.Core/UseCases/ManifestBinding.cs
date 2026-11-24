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
}
