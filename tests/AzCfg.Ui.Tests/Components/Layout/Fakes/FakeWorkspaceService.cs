using AzCfg.Core.Model;
using AzCfg.Ui.Application.Workspace;

namespace AzCfg.Ui.Tests.Components.Layout.Fakes;

/// <summary>Only <see cref="OpenAsync"/> is used by the platform components.</summary>
internal sealed class FakeWorkspaceService : IWorkspaceService
{
    public List<string> Opened { get; } = [];

    public Task<WorkspaceTree> OpenAsync(string rootPath, CancellationToken ct)
    {
        Opened.Add(rootPath);
        return Task.FromResult(new WorkspaceTree(rootPath, [], []));
    }

    public Task<ProjectSetupProposal> ProposeSetupAsync(ProjectNode project, CancellationToken ct) => throw new NotSupportedException();

    public Task<ProjectBinding> BindAsync(ProjectNode project, string service, LocalTargetBinding values, LocalTargetBinding secrets, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<ProjectBinding> SetVaultAsync(ProjectBinding binding, string environment, Uri? vault, CancellationToken ct) => throw new NotSupportedException();

    public Task<ProjectBinding> AddIgnoreAsync(ProjectBinding binding, string pattern, CancellationToken ct) => throw new NotSupportedException();

    public Task<ProjectBinding> RemoveIgnoreAsync(ProjectBinding binding, string pattern, CancellationToken ct) => throw new NotSupportedException();
}
