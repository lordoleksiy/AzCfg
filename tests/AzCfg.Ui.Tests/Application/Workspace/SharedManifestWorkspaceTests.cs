using AzCfg.Core.Manifest;
using AzCfg.Core.Model;
using AzCfg.Ui.Application.Workspace;

namespace AzCfg.Ui.Tests.Application.Workspace;

/// <summary>UX4 (FR-W7): share with team via <c>azcfg.json</c>, and a fresh user state opening the shared project bound.</summary>
public sealed class SharedManifestWorkspaceTests : IDisposable
{
    private static readonly Uri Vault = new("https://kv-dev.vault.azure.net");
    private readonly string _root = WorkspaceTestWorld.NewRoot();
    private readonly WorkspaceProject _crm;
    private readonly WorkspaceProject _billing;
    private readonly List<WorkspaceTestWorld> _worlds = [];

    public SharedManifestWorkspaceTests()
    {
        _crm = WorkspaceTestWorld.ProjectAt(_root, "src/Crm", "Crm");
        _billing = WorkspaceTestWorld.ProjectAt(_root, "src/Billing", "Billing");
    }

    public void Dispose()
    {
        foreach (var world in _worlds)
        {
            world.Dispose();
        }
    }

    private string CrmFolder => Path.GetDirectoryName(_crm.Project.CsprojPath)!;

    private string ManifestFile => Path.Combine(CrmFolder, "azcfg.json");

    /// <summary>A developer's machine: its own user state, the same workspace and a <c>dev</c> environment.</summary>
    private WorkspaceTestWorld Developer(params WorkspaceProject[] extra)
    {
        var w = new WorkspaceTestWorld(null, [_crm, _billing, .. extra], _root);
        w.World.State.SaveProfileAsync(new UserProfile([w.Env("dev")]), default).GetAwaiter().GetResult();
        _worlds.Add(w);
        return w;
    }

    private static IWorkspaceService Service(WorkspaceTestWorld w) => w.Get<IWorkspaceService>();

    private static Dictionary<string, ProjectNode> Nodes(WorkspaceTree tree) =>
        tree.Solutions.SelectMany(s => s.Projects).Concat(tree.LooseProjects).ToDictionary(n => n.Project.Project.Name);

    /// <summary>Developer A binds Crm (with a dev vault) and shares it; returns A's world.</summary>
    private async Task<WorkspaceTestWorld> SharedByA()
    {
        var a = Developer();
        var binding = await Service(a).BindAsync(new ProjectNode(_crm, null), "crm", a.Target(_crm), a.Target(_crm), default);
        binding = await Service(a).SetVaultAsync(binding, "dev", Vault, default);
        await Service(a).ShareAsync(new ProjectNode(_crm, binding), default);
        return a;
    }

    private WorkspaceTestWorld DeveloperWithManifestOf(WorkspaceTestWorld a)
    {
        var b = Developer();
        b.World.Manifests.SetJson(ManifestFile, a.World.Manifests.Json(ManifestFile)!);
        return b;
    }

    [Fact]
    public async Task Share_WritesAzcfgJsonInProjectFolder_AndReopenedNodeIsShared()
    {
        var a = Developer();
        var binding = await Service(a).BindAsync(new ProjectNode(_crm, null), "crm", a.Target(_crm), a.Target(_crm), default);

        var path = await Service(a).ShareAsync(new ProjectNode(_crm, binding), default);

        Assert.Equal(ManifestFile, path);
        var manifest = ManifestSerializer.Deserialize(a.World.Manifests.Json(ManifestFile)!);
        Assert.Equal("crm", manifest.Service);
        var nodes = Nodes(await Service(a).OpenAsync(_root, default));
        Assert.Equal(ManifestFile, nodes["Crm"].ManifestPath);
        Assert.True(nodes["Crm"].IsShared);
        Assert.Null(nodes["Crm"].ManifestChange);
        Assert.False(nodes["Billing"].IsShared);
    }

    [Fact]
    public async Task Open_FreshUserStateWithManifest_ProjectIsBoundFromIt()
    {
        var a = await SharedByA();
        var b = DeveloperWithManifestOf(a);

        var crm = Nodes(await Service(b).OpenAsync(_root, default))["Crm"];

        var binding = Assert.IsType<ProjectBinding>(crm.Binding);
        Assert.Equal("crm", binding.Service);
        Assert.Equal(b.Target(_crm), binding.ValuesTarget);
        Assert.Equal(b.Target(_crm), binding.SecretsTarget);
        Assert.Equal(Vault, binding.VaultByEnvironment["dev"]);
        Assert.Equal(ManifestFile, crm.ManifestPath);
        Assert.Equal("crm", b.World.State.Binding(CrmFolder)!.Service);
    }

    [Fact]
    public async Task ProposeThenBind_StaleUnboundNodeWithManifest_BoundFromManifestAndNotOverwritten()
    {
        var a = await SharedByA();
        var b = DeveloperWithManifestOf(a);
        var stale = new ProjectNode(_crm, null);

        var proposal = await Service(b).ProposeSetupAsync(stale, default);

        Assert.Equal(ManifestFile, proposal.Resolution.ManifestPath);
        var fromManifest = Assert.IsType<ProjectBinding>(proposal.Resolution.Existing);
        Assert.Equal("crm", fromManifest.Service);

        // A client that still binds the stale node re-binds the saved binding: the vault from the manifest stays.
        var rebound = await Service(b).BindAsync(stale, "crm", b.Target(_crm), b.Target(_crm), default);
        Assert.Equal(Vault, rebound.VaultByEnvironment["dev"]);
        Assert.Equal(Vault, b.World.State.Binding(CrmFolder)!.VaultByEnvironment["dev"]);
    }

    [Fact]
    public async Task Open_CorruptedManifest_ReportedOnTheNodeNotThrown()
    {
        var b = Developer();
        b.World.Manifests.SetJson(ManifestFile, "{ not json");

        var nodes = Nodes(await Service(b).OpenAsync(_root, default));

        Assert.Null(nodes["Crm"].Binding);
        Assert.False(nodes["Crm"].IsShared);
        Assert.Contains("azcfg.json", nodes["Crm"].ManifestError);
        Assert.Null(nodes["Billing"].ManifestError);
    }

    [Fact]
    public async Task Open_ManifestOfAnotherProject_NotAppliedToThisOne()
    {
        var a = Developer();
        var crm = await Service(a).BindAsync(new ProjectNode(_crm, null), "crm", a.Target(_crm), a.Target(_crm), default);
        var rootManifest = Path.Combine(_root, "azcfg.json");
        a.World.Manifests.SetJson(rootManifest, ManifestSerializer.Serialize(new ProjectManifest(1, "crm", new("user-secrets", "src/Crm"), new("user-secrets", "src/Crm"), null)));

        var nodes = Nodes(await Service(a).OpenAsync(_root, default));

        Assert.Equal(rootManifest, nodes["Crm"].ManifestPath);
        Assert.Null(nodes["Billing"].Binding);
        Assert.Null(nodes["Billing"].ManifestPath);
        Assert.Equal(crm.RepoPath, nodes["Crm"].Binding!.RepoPath);
    }

    [Fact]
    public async Task Share_AnotherProjectsManifestInEffect_Refused()
    {
        var a = Developer();
        var billing = await Service(a).BindAsync(new ProjectNode(_billing, null), "billing", a.Target(_billing), a.Target(_billing), default);
        var rootManifest = Path.Combine(_root, "azcfg.json");
        var json = ManifestSerializer.Serialize(new ProjectManifest(1, "crm", new("user-secrets", "src/Crm"), new("user-secrets", "src/Crm"), null));
        a.World.Manifests.SetJson(rootManifest, json);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(a).ShareAsync(new ProjectNode(_billing, billing), default));
        Assert.Equal(json, a.World.Manifests.Json(rootManifest));
    }

    [Fact]
    public async Task PlanShare_ReturnsTheFileCoreWrites()
    {
        var a = Developer();
        var binding = await Service(a).BindAsync(new ProjectNode(_crm, null), "crm", a.Target(_crm), a.Target(_crm), default);

        Assert.Equal(ManifestFile, await Service(a).PlanShareAsync(new ProjectNode(_crm, binding), default));
        Assert.Null(a.World.Manifests.Json(ManifestFile));
    }

    [Fact]
    public async Task Open_BoundProjectWhoseManifestChanged_NotRewrittenOnlyMarked_ApplyResets()
    {
        var a = await SharedByA();
        var b = DeveloperWithManifestOf(a);
        var mine = new ProjectBinding(CrmFolder, "crm-old", b.Target(_crm), b.Target(_crm), new Dictionary<string, Uri>(), "dev");
        await b.World.State.SaveBindingAsync(mine, default);
        await b.World.State.SaveSnapshotAsync(CrmFolder, "dev", new AzCfg.Core.Sync.ConfigState(new Dictionary<string, AzCfg.Core.Sync.StateItem>()), default);

        var crm = Nodes(await Service(b).OpenAsync(_root, default))["Crm"];

        var saved = b.World.State.Binding(CrmFolder)!;
        Assert.Equal(("crm-old", "dev"), (saved.Service, saved.ActiveEnvironment));
        Assert.Empty(saved.VaultByEnvironment);
        Assert.NotNull(await b.World.State.LoadSnapshotAsync(CrmFolder, "dev", default));
        Assert.Equal("crm-old", crm.Binding!.Service);
        Assert.Equal(ManifestFile, crm.ManifestPath);
        Assert.Equal(new ManifestChange("crm-old", "crm"), crm.ManifestChange);
        Assert.True(crm.ManifestChange!.ServiceChanged);

        var resolution = await Service(b).ApplyManifestAsync(crm, default);

        Assert.Equal("crm-old", resolution.PreviousService);
        Assert.Equal("crm", b.World.State.Binding(CrmFolder)!.Service);
        Assert.Null(b.World.State.Binding(CrmFolder)!.ActiveEnvironment);
        Assert.Null(await b.World.State.LoadSnapshotAsync(CrmFolder, "dev", default));
        Assert.Null(Nodes(await Service(b).OpenAsync(_root, default))["Crm"].ManifestChange);
    }

    [Fact]
    public async Task Open_BoundProjectMissingTheManifestVault_MarkedChanged()
    {
        var a = await SharedByA();
        var b = DeveloperWithManifestOf(a);
        await b.World.State.SaveBindingAsync(new ProjectBinding(CrmFolder, "crm", b.Target(_crm), b.Target(_crm), new Dictionary<string, Uri>(), "dev"), default);

        var crm = Nodes(await Service(b).OpenAsync(_root, default))["Crm"];

        Assert.False(crm.ManifestChange!.ServiceChanged);
        Assert.Empty(b.World.State.Binding(CrmFolder)!.VaultByEnvironment);
    }

    [Fact]
    public async Task Open_MalformedRootManifestInMonorepo_NotAttributedToAnyProject()
    {
        var b = Developer();
        b.World.Manifests.SetJson(Path.Combine(_root, "azcfg.json"), "{ not json");

        var nodes = Nodes(await Service(b).OpenAsync(_root, default));

        Assert.All(nodes.Values, n => Assert.Null(n.ManifestError));
        Assert.All(nodes.Values, n => Assert.Null(n.Binding));
    }

    [Fact]
    public async Task Open_ManifestNamesAFolderWithTwoProjects_NamesNeither()
    {
        var tests = WorkspaceTestWorld.ProjectAt(_root, "src/Crm", "Crm.Tests");
        var b = Developer(tests);
        b.World.Manifests.SetJson(Path.Combine(_root, "azcfg.json"),
            ManifestSerializer.Serialize(new ProjectManifest(1, "crm", new("user-secrets", "src/Crm"), new("user-secrets", "src/Crm"), null)));

        var nodes = Nodes(await Service(b).OpenAsync(_root, default));

        Assert.Null(nodes["Crm"].Binding);
        Assert.Null(nodes["Crm"].ManifestPath);
        Assert.Null(nodes["Crm.Tests"].Binding);
    }

    [Fact]
    public async Task ProposeThenBind_RootManifest_BindingKeyedByManifestFolderAndReusedByBind()
    {
        var b = Developer();
        var rootManifest = Path.Combine(_root, "azcfg.json");
        b.World.Manifests.SetJson(rootManifest, ManifestSerializer.Serialize(new ProjectManifest(
            1, "crm", new("user-secrets", "src/Crm/Crm.csproj"), new("user-secrets", "src/Crm/Crm.csproj"), new Dictionary<string, Uri> { ["dev"] = Vault })));
        var stale = new ProjectNode(_crm, null);

        var proposal = await Service(b).ProposeSetupAsync(stale, default);
        var rebound = await Service(b).BindAsync(stale, "crm", b.Target(_crm), b.Target(_crm), default);

        Assert.Equal(_root, proposal.Resolution.Existing!.RepoPath);
        Assert.Equal(_root, rebound.RepoPath);
        Assert.Equal(Vault, rebound.VaultByEnvironment["dev"]);
        Assert.Null(b.World.State.Binding(CrmFolder));
    }
}
