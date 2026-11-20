using AzCfg.Core.Model;
using AzCfg.Core.Sync;
using AzCfg.Ui.Application.Workspace;

namespace AzCfg.Ui.Tests.Application.Workspace;

public sealed class WorkspaceServiceTests : IDisposable
{
    private readonly WorkspaceProject _crm;
    private readonly WorkspaceProject _billing;
    private readonly WorkspaceProject _tools;
    private readonly WorkspaceTestWorld _w;

    public WorkspaceServiceTests()
    {
        var root = WorkspaceTestWorld.NewRoot();
        _crm = WorkspaceTestWorld.ProjectAt(root, "src/Crm", "Crm");
        _billing = WorkspaceTestWorld.ProjectAt(root, "src/Billing", "Billing");
        _tools = WorkspaceTestWorld.ProjectAt(root, "tools/Tool", "Tool");
        var solution = new SolutionInfo(Path.Combine(root, "All.slnx"), "All", SolutionFormat.Slnx, [_crm.Project.CsprojPath, _billing.Project.CsprojPath]);
        _w = new WorkspaceTestWorld([solution], [_crm, _billing, _tools], root);
        _w.World.State.SaveProfileAsync(new UserProfile([_w.Env("dev")]), default).GetAwaiter().GetResult();
        _w.World.AppConfig.Set("https://dev.azconfig.io", "crm:A", "1");
        _w.World.AppConfig.Set("https://dev.azconfig.io", "billing:A", "1");
    }

    public void Dispose() => _w.Dispose();

    private IWorkspaceService Service => _w.Get<IWorkspaceService>();

    /// <summary>Core validates targets: <c>appsettings-local</c> applies only to a project folder with an appsettings*.json.</summary>
    private static void WithAppSettings(WorkspaceProject project)
    {
        var folder = Path.GetDirectoryName(project.Project.CsprojPath)!;
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "appsettings.json"), "{}");
    }

    [Fact]
    public async Task OpenAsync_GroupsProjectsBySolutionAndRestAreLoose()
    {
        var tree = await Service.OpenAsync(_w.Root, default);

        var solution = Assert.Single(tree.Solutions);
        Assert.Equal(["Crm", "Billing"], solution.Projects.Select(p => p.Project.Project.Name));
        Assert.Equal("Tool", Assert.Single(tree.LooseProjects).Project.Project.Name);
    }

    [Fact]
    public async Task OpenAsync_TouchesRegistry()
    {
        await Service.OpenAsync(_w.Root, default);

        Assert.Equal(_w.Root, Assert.Single(await _w.Get<IWorkspaceRegistry>().ListAsync(default)).RootPath);
    }

    [Fact]
    public async Task OpenAsync_CliBindingAtRepoRoot_AdoptedOnlyForItsProject()
    {
        var binding = new ProjectBinding(_w.Root, "crm", _w.Target(_crm), _w.Target(_crm), new Dictionary<string, Uri>());
        await _w.World.State.SaveBindingAsync(binding, default);

        var tree = await Service.OpenAsync(_w.Root, default);

        var nodes = tree.Solutions.SelectMany(s => s.Projects).Concat(tree.LooseProjects).ToDictionary(n => n.Project.Project.Name);
        Assert.Equal("crm", nodes["Crm"].Binding!.Service);
        Assert.Equal(_w.Root, nodes["Crm"].Binding!.RepoPath);
        Assert.Null(nodes["Billing"].Binding);
        Assert.Null(nodes["Tool"].Binding);
    }

    [Fact]
    public async Task OpenAsync_BindingOutsideWorkspaceRoot_NotUsed()
    {
        var outside = new ProjectBinding(Path.GetDirectoryName(_w.Root)!, "crm", _w.Target(_crm), _w.Target(_crm), new Dictionary<string, Uri>());
        await _w.World.State.SaveBindingAsync(outside, default);

        var tree = await Service.OpenAsync(_w.Root, default);

        Assert.All(tree.Solutions.SelectMany(s => s.Projects), n => Assert.Null(n.Binding));
    }

    [Fact]
    public async Task BindAsync_NewProject_UsesProjectFolderAsRepoPath()
    {
        var node = new ProjectNode(_crm, null);

        var binding = await Service.BindAsync(node, "crm", _w.Target(_crm), _w.Target(_crm), default);

        Assert.Equal(_w.At("src", "Crm"), binding.RepoPath);
        Assert.Equal("crm", _w.World.State.Binding(binding.RepoPath)!.Service);
    }

    [Fact]
    public async Task BindAsync_ExistingNewService_FollowsCoreRule_ClearsActiveAndSnapshots_KeepsVaultsAndIgnore()
    {
        var old = new ProjectBinding(_w.Root, "crm", _w.Target(_crm), _w.Target(_crm),
            new Dictionary<string, Uri> { ["dev"] = new("https://kv.vault.azure.net") }, "dev", ["Logging:*"]);
        await _w.World.State.SaveBindingAsync(old, default);
        WithAppSettings(_crm);
        await _w.World.State.SaveSnapshotAsync(_w.Root, "dev", new ConfigState(new Dictionary<string, StateItem>()), default);

        var rebound = await Service.BindAsync(new ProjectNode(_crm, old), "billing", _w.Target(_crm, LocalTargetKind.AppSettingsLocal), _w.Target(_crm), default);

        Assert.Equal(_w.Root, rebound.RepoPath);
        Assert.Equal("billing", rebound.Service);
        Assert.Equal(LocalTargetKind.AppSettingsLocal, rebound.ValuesTarget.Kind);
        Assert.Equal(old.VaultByEnvironment, rebound.VaultByEnvironment);
        Assert.Null(rebound.ActiveEnvironment);
        Assert.Equal(["Logging:*"], rebound.Ignore);
        Assert.Equal("billing", _w.World.State.Binding(_w.Root)!.Service);
        Assert.Null(await _w.World.State.LoadSnapshotAsync(_w.Root, "dev", default));
    }

    [Fact]
    public async Task BindAsync_ExistingSameServiceNewTarget_KeepsActiveAndSnapshots()
    {
        var old = new ProjectBinding(_w.Root, "crm", _w.Target(_crm), _w.Target(_crm), new Dictionary<string, Uri>(), "dev");
        await _w.World.State.SaveBindingAsync(old, default);
        WithAppSettings(_crm);
        await _w.World.State.SaveSnapshotAsync(_w.Root, "dev", new ConfigState(new Dictionary<string, StateItem>()), default);

        var rebound = await Service.BindAsync(new ProjectNode(_crm, old), "crm", _w.Target(_crm, LocalTargetKind.AppSettingsLocal), _w.Target(_crm), default);

        Assert.Equal(LocalTargetKind.AppSettingsLocal, rebound.ValuesTarget.Kind);
        Assert.Equal("dev", rebound.ActiveEnvironment);
        Assert.NotNull(await _w.World.State.LoadSnapshotAsync(_w.Root, "dev", default));
    }

    [Fact]
    public async Task ProposeSetupAsync_Bound_HasTargetOptionsAndCurrentServiceFirst()
    {
        var binding = new ProjectBinding(_w.Root, "crm", _w.Target(_crm), _w.Target(_crm), new Dictionary<string, Uri>());

        var proposal = await Service.ProposeSetupAsync(new ProjectNode(_crm, binding), default);

        Assert.Equal(binding, proposal.Resolution.Existing);
        Assert.False(proposal.Resolution.IsConfident);
        Assert.NotEmpty(proposal.Resolution.TargetOptions);
        Assert.Equal(["crm", "billing"], proposal.Resolution.Candidates);
    }

    [Fact]
    public async Task ProposeSetupAsync_BoundWithServiceMissingInStore_StillListsItFirst()
    {
        var binding = new ProjectBinding(_w.Root, "legacy", _w.Target(_crm), _w.Target(_crm), new Dictionary<string, Uri>());

        var proposal = await Service.ProposeSetupAsync(new ProjectNode(_crm, binding), default);

        Assert.Equal("legacy", proposal.Resolution.Candidates[0]);
        Assert.Contains("crm", proposal.Resolution.Candidates);
    }

    [Fact]
    public async Task ProposeSetupAsync_Unbound_ConfidentCandidateAndOnlyOwnTargets()
    {
        var proposal = await Service.ProposeSetupAsync(new ProjectNode(_crm, null), default);

        Assert.Null(proposal.Resolution.Existing);
        Assert.Contains("crm", proposal.Resolution.Candidates);
        Assert.All(proposal.Resolution.TargetOptions, o => Assert.Equal(_crm.Project.CsprojPath, o.Target.ProjectPath));
        Assert.NotEmpty(proposal.Resolution.TargetOptions);
    }

    [Fact]
    public async Task SetVaultAsync_SetsReplacesAndRemoves()
    {
        var binding = new ProjectBinding(_w.Root, "crm", _w.Target(_crm), _w.Target(_crm), new Dictionary<string, Uri>());
        await _w.World.State.SaveBindingAsync(binding, default);

        var set = await Service.SetVaultAsync(binding, "dev", new Uri("https://a.vault.azure.net"), default);
        var replaced = await Service.SetVaultAsync(set, "DEV", new Uri("https://b.vault.azure.net"), default);
        var removed = await Service.SetVaultAsync(replaced, "dev", null, default);

        Assert.Equal(new Uri("https://b.vault.azure.net"), Assert.Single(replaced.VaultByEnvironment).Value);
        Assert.Empty(removed.VaultByEnvironment);
        Assert.Empty(_w.World.State.Binding(_w.Root)!.VaultByEnvironment);
    }

    [Fact]
    public async Task AddAndRemoveIgnoreAsync_EditsSavedBinding()
    {
        var binding = new ProjectBinding(_w.Root, "crm", _w.Target(_crm), _w.Target(_crm), new Dictionary<string, Uri>());
        await _w.World.State.SaveBindingAsync(binding, default);

        var added = await Service.AddIgnoreAsync(binding, "Logging:*", default);
        var removed = await Service.RemoveIgnoreAsync(added, "logging:*", default);

        Assert.Equal(["Logging:*"], added.Ignore);
        Assert.Empty(removed.Ignore!);
    }
}
