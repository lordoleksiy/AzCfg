using AzCfg.Core.Model;
using AzCfg.Ui.Application.Workspace;

namespace AzCfg.Ui.Tests.Features.Browse.Fakes;

/// <summary>Only the ignore rules are used by Browse.</summary>
internal sealed class FakeWorkspaceService : IWorkspaceService
{
    public List<string> Calls { get; } = [];

    public Task<ProjectBinding> AddIgnoreAsync(ProjectBinding binding, string pattern, CancellationToken ct)
    {
        Calls.Add($"Add {pattern}");
        return Task.FromResult(binding with { Ignore = [.. binding.Ignore ?? [], pattern] });
    }

    public Task<ProjectBinding> RemoveIgnoreAsync(ProjectBinding binding, string pattern, CancellationToken ct)
    {
        Calls.Add($"Remove {pattern}");
        return Task.FromResult(binding with { Ignore = [.. (binding.Ignore ?? []).Where(p => p != pattern)] });
    }

    public Task<WorkspaceTree> OpenAsync(string rootPath, CancellationToken ct) => throw new NotSupportedException();

    public Task<ProjectSetupProposal> ProposeSetupAsync(ProjectNode project, CancellationToken ct) => throw new NotSupportedException();

    public Task<ProjectBinding> BindAsync(ProjectNode project, string service, LocalTargetBinding values, LocalTargetBinding secrets, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<ProjectBinding> SetVaultAsync(ProjectBinding binding, string environment, Uri? vault, CancellationToken ct) => throw new NotSupportedException();
}
