using AzCfg.Core.Model;
using AzCfg.Ui.Application.Workspace;

namespace AzCfg.Ui.Tests.Features.Sync.Fakes;

/// <summary>Only <see cref="SetVaultAsync"/> is used by the Sync views.</summary>
internal sealed class FakeWorkspaceService : IWorkspaceService
{
    public List<(string Environment, Uri? Vault)> Vaults { get; } = [];

    public Task<ProjectBinding> SetVaultAsync(ProjectBinding binding, string environment, Uri? vault, CancellationToken ct)
    {
        Vaults.Add((environment, vault));
        var vaults = new Dictionary<string, Uri>(binding.VaultByEnvironment, StringComparer.OrdinalIgnoreCase) { [environment] = vault! };
        return Task.FromResult(binding with { VaultByEnvironment = vaults });
    }

    public Task<WorkspaceTree> OpenAsync(string rootPath, CancellationToken ct) => throw new NotSupportedException();

    public Task<ProjectSetupProposal> ProposeSetupAsync(ProjectNode project, CancellationToken ct) => throw new NotSupportedException();

    public Task<ProjectBinding> BindAsync(ProjectNode project, string service, LocalTargetBinding values, LocalTargetBinding secrets, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<ProjectBinding> AddIgnoreAsync(ProjectBinding binding, string pattern, CancellationToken ct) => throw new NotSupportedException();

    public Task<ProjectBinding> RemoveIgnoreAsync(ProjectBinding binding, string pattern, CancellationToken ct) => throw new NotSupportedException();

    public Task<string> ShareAsync(ProjectNode project, CancellationToken ct) => throw new NotSupportedException();
}
