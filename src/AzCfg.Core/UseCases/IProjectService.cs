using AzCfg.Core.Model;

namespace AzCfg.Core.UseCases;

public interface IProjectService
{
    Task<ProjectResolution> ResolveAsync(string repoPath, CancellationToken ct);
    Task<ProjectBinding> BindAsync(string repoPath, string service, IReadOnlyList<ProjectInfo> targets, CancellationToken ct);
}
