using AzCfg.Core.Model;
using AzCfg.Core.UseCases;

namespace AzCfg.Ui.Application.Workspace;

public interface IWorkspaceService
{
    /// <summary>
    /// Scan + binding lookup (G4) + registry touch. An unbound project named by a shared <c>azcfg.json</c> opens bound from it (Core
    /// <c>ResolveAsync</c>, no wizard); a bound one is never changed here, only marked with <see cref="ProjectNode.ManifestChange"/>.
    /// A broken manifest sets <see cref="ProjectNode.ManifestError"/> on the project it belongs to.
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
    /// The <c>azcfg.json</c> path <see cref="ShareAsync"/> would write (Read). Throws when the project is unbound or another project's
    /// manifest is in effect.
    /// </summary>
    Task<string> PlanShareAsync(ProjectNode project, CancellationToken ct);

    /// <summary>
    /// Share with the team (FR-W7): Core <c>project.shared = true</c> writes <c>azcfg.json</c> (the binding's folder unless a manifest
    /// already names this project); returns its path. Throws like <see cref="PlanShareAsync"/>.
    /// </summary>
    Task<string> ShareAsync(ProjectNode project, CancellationToken ct);

    /// <summary>
    /// Takes the manifest's shared fields into a bound project marked with <see cref="ProjectNode.ManifestChange"/> (Core
    /// <c>ResolveAsync</c>, a Write); <see cref="ProjectResolution.PreviousService"/> says the service changed.
    /// </summary>
    Task<ProjectResolution> ApplyManifestAsync(ProjectNode project, CancellationToken ct);
}
