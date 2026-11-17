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

    /// <summary>
    /// Adds a normalized local ignore pattern (see <c>Sync.IgnoreRules</c>, ADR 0008) to the bound project, skipping a
    /// case-insensitive duplicate, saves and returns the binding. Never touches local files or Azure. Throws
    /// <see cref="ArgumentException"/> for a blank pattern and <see cref="InvalidOperationException"/> when the project is not bound.
    /// </summary>
    Task<ProjectBinding> AddIgnoreAsync(string repoPath, string pattern, CancellationToken ct);

    /// <summary>
    /// Removes a local ignore pattern (ignoring case; a missing pattern is a no-op), saves and returns the binding. Never
    /// touches local files or Azure. Throws like <see cref="AddIgnoreAsync"/>.
    /// </summary>
    Task<ProjectBinding> RemoveIgnoreAsync(string repoPath, string pattern, CancellationToken ct);
}
