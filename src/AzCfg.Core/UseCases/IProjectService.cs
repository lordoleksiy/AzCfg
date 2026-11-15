using AzCfg.Core.Model;

namespace AzCfg.Core.UseCases;

public interface IProjectService
{
    Task<ProjectResolution> ResolveAsync(string repoPath, CancellationToken ct);

    /// <summary>
    /// Binds a project to a service and to the developer's chosen local targets (decision 2).
    /// <paramref name="secretsTarget"/> must be safe for secrets (see <see cref="LocalTargetOption.IsSafeForSecrets"/>);
    /// an unsafe secrets target is rejected.
    /// </summary>
    Task<ProjectBinding> BindAsync(
        string repoPath,
        string service,
        LocalTargetBinding valuesTarget,
        LocalTargetBinding secretsTarget,
        CancellationToken ct);
}
