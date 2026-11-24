using AzCfg.Core.Model;
using AzCfg.Core.UseCases;
using AzCfg.Ui.Application.Workspace;

namespace AzCfg.Ui.Tests.Features.Workspace.Fakes;

/// <summary>Configurable <see cref="IWorkspaceService"/>: <see cref="OpenGate"/> holds a scan until released or cancelled.</summary>
internal sealed class FakeWorkspaceService : IWorkspaceService
{
    public WorkspaceTree Tree { get; set; } = new("C:/src/Shop", [], []);

    public TaskCompletionSource? OpenGate { get; set; }

    public Exception? OpenFailure { get; set; }

    public List<string> Opened { get; } = [];

    public CancellationToken LastOpenToken { get; private set; }

    public ProjectSetupProposal? Proposal { get; set; }

    public List<(string Service, LocalTargetBinding Values, LocalTargetBinding Secrets)> Binds { get; } = [];

    public List<(string Environment, Uri? Vault)> Vaults { get; } = [];

    /// <summary>The binding <see cref="BindAsync"/> returns; null builds one from the arguments (the active environment of the old binding is kept).</summary>
    public Func<ProjectNode, string, ProjectBinding>? BindResult { get; set; }

    public async Task<WorkspaceTree> OpenAsync(string rootPath, CancellationToken ct)
    {
        Opened.Add(rootPath);
        LastOpenToken = ct;
        if (OpenGate is not null)
        {
            await OpenGate.Task.WaitAsync(ct);
        }

        return OpenFailure is null ? Tree with { RootPath = rootPath } : throw OpenFailure;
    }

    public Task<ProjectSetupProposal> ProposeSetupAsync(ProjectNode project, CancellationToken ct) =>
        Task.FromResult(Proposal ?? throw new InvalidOperationException("No proposal configured"));

    public Task<ProjectBinding> BindAsync(ProjectNode project, string service, LocalTargetBinding values, LocalTargetBinding secrets, CancellationToken ct)
    {
        Binds.Add((service, values, secrets));
        var binding = BindResult?.Invoke(project, service)
            ?? new ProjectBinding(Path.GetDirectoryName(project.Id)!, service, values, secrets, project.Binding?.VaultByEnvironment ?? new Dictionary<string, Uri>(), project.Binding?.ActiveEnvironment);
        return Task.FromResult(binding);
    }

    public Task<ProjectBinding> SetVaultAsync(ProjectBinding binding, string environment, Uri? vault, CancellationToken ct)
    {
        Vaults.Add((environment, vault));
        var vaults = new Dictionary<string, Uri>(binding.VaultByEnvironment, StringComparer.OrdinalIgnoreCase);
        if (vault is null)
        {
            vaults.Remove(environment);
        }
        else
        {
            vaults[environment] = vault;
        }

        return Task.FromResult(binding with { VaultByEnvironment = vaults });
    }

    public Task<ProjectBinding> AddIgnoreAsync(ProjectBinding binding, string pattern, CancellationToken ct) => throw new NotSupportedException();

    public Task<ProjectBinding> RemoveIgnoreAsync(ProjectBinding binding, string pattern, CancellationToken ct) => throw new NotSupportedException();

    public List<string> Shares { get; } = [];

    public List<string> Applied { get; } = [];

    /// <summary>The path <see cref="PlanShareAsync"/> returns; null = <c>&lt;RepoPath&gt;/azcfg.json</c>.</summary>
    public string? SharePath { get; set; }

    /// <summary>What <see cref="ApplyManifestAsync"/> returns (the tree for the next open is the test's to set).</summary>
    public ProjectResolution? ApplyResult { get; set; }

    public Task<string> PlanShareAsync(ProjectNode project, CancellationToken ct) =>
        ShareFailure is not null ? Task.FromException<string>(ShareFailure) : Task.FromResult(SharePath ?? Path.Combine(project.Binding!.RepoPath, "azcfg.json"));

    public Task<ProjectResolution> ApplyManifestAsync(ProjectNode project, CancellationToken ct)
    {
        Applied.Add(project.Id);
        return Task.FromResult(ApplyResult ?? new ProjectResolution([], true, [], project.Binding, project.ManifestPath));
    }

    public Exception? ShareFailure { get; set; }

    /// <summary>Records the share; the next <see cref="OpenAsync"/> returns the project with the manifest path (as after a real share).</summary>
    public Task<string> ShareAsync(ProjectNode project, CancellationToken ct)
    {
        if (ShareFailure is not null)
        {
            return Task.FromException<string>(ShareFailure);
        }

        Shares.Add(project.Id);
        var path = Path.Combine(project.Binding!.RepoPath, "azcfg.json");
        IReadOnlyList<ProjectNode> Mark(IReadOnlyList<ProjectNode> nodes) => [.. nodes.Select(n => n.Id == project.Id ? n with { ManifestPath = path } : n)];
        Tree = Tree with
        {
            Solutions = [.. Tree.Solutions.Select(s => s with { Projects = Mark(s.Projects) })],
            LooseProjects = Mark(Tree.LooseProjects),
        };
        return Task.FromResult(path);
    }
}
