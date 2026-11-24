using AzCfg.Core.Model;

namespace AzCfg.Ui.Application.Workspace;

public interface IWorkspaceService
{
    /// <summary>
    /// Scan + binding lookup (G4) + registry touch. A project named by a shared <c>azcfg.json</c> opens bound from it (Core
    /// <c>ResolveAsync</c>, no wizard) with <see cref="ProjectNode.ManifestPath"/>; a broken manifest sets <see cref="ProjectNode.ManifestError"/>.
    /// </summary>
    Task<WorkspaceTree> OpenAsync(string rootPath, CancellationToken ct);

    /// <summary><c>IProjectService.ResolveAsync</c>(folder of the .csproj) + G19 for a bound project.</summary>
    Task<ProjectSetupProposal> ProposeSetupAsync(ProjectNode project, CancellationToken ct);

    /// <summary>
    /// New binding: <c>RepoPath</c> = folder of the .csproj. Existing (<c>project.Binding</c>): its <c>RepoPath</c>;
    /// <c>VaultByEnvironment</c>, <c>ActiveEnvironment</c> and <c>Ignore</c> are kept (G5).
    /// </summary>
    Task<ProjectBinding> BindAsync(ProjectNode project, string service, LocalTargetBinding values, LocalTargetBinding secrets, CancellationToken ct);

    /// <summary>A <c>null</c> <paramref name="vault"/> removes the environment's vault.</summary>
    Task<ProjectBinding> SetVaultAsync(ProjectBinding binding, string environment, Uri? vault, CancellationToken ct);

    /// <summary>Via <c>IProjectService.AddIgnoreAsync</c>.</summary>
    Task<ProjectBinding> AddIgnoreAsync(ProjectBinding binding, string pattern, CancellationToken ct);

    /// <summary>Via <c>IProjectService.RemoveIgnoreAsync</c>.</summary>
    Task<ProjectBinding> RemoveIgnoreAsync(ProjectBinding binding, string pattern, CancellationToken ct);

    /// <summary>
    /// Share with the team (FR-W7): Core <c>project.shared = true</c> writes <c>azcfg.json</c> (the binding's folder unless a manifest
    /// already names this project); returns its path. Throws when the project is unbound or another project's manifest is in effect.
    /// </summary>
    Task<string> ShareAsync(ProjectNode project, CancellationToken ct);
}
