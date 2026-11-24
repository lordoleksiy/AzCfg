using AzCfg.Core.Model;
using AzCfg.Core.UseCases;
using AzCfg.Ui.Application.Operations;
using AzCfg.Ui.Application.Workspace;
using AzCfg.Ui.Features.Workspace;
using AzCfg.Ui.Shared;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using static AzCfg.Ui.Tests.Features.Workspace.ShellFixtures;

namespace AzCfg.Ui.Tests.Features.Workspace;

/// <summary>UX4 (FR-W7): Share with team, the "shared (azcfg.json)" badge, the manifest error state and the bind flow of a shared project.</summary>
public sealed class ShareWithTeamTests : ShellContext
{
    private static readonly string Orders = IdOf("Orders.Api");
    private static readonly string Manifest = $"{Root}/Orders.Api/azcfg.json";

    private void Seed(ProjectNode orders)
    {
        var tree = Tree([Solution("Shop", orders, Node("Billing", null))]);
        WorkspaceService.Tree = tree;
        SeedTree(tree);
        SeedEnvironments();
    }

    private static IRenderedComponent<FluentTreeItem> Item(IRenderedComponent<WorkspaceSection> cut, string textStart) =>
        cut.FindComponents<FluentTreeItem>().First(i => i.Instance.Text!.StartsWith(textStart, StringComparison.Ordinal));

    private bool ShareEnabled() => Commands.All.Single(c => c.Id == CommandIds.ProjectShare).IsEnabled!();

    private IRenderedComponent<WorkspaceSection> RenderWithOrdersSelected()
    {
        _ = Providers;
        var cut = Render<WorkspaceSection>();
        cut.InvokeAsync(() => Workspace.SelectProject(Orders));
        return cut;
    }

    [Fact]
    public void Render_SharedProject_ShowsBadgeAndNoShareAction()
    {
        Seed(Node("Orders.Api", Binding("Orders.Api")) with { ManifestPath = Manifest });
        var cut = RenderWithOrdersSelected();

        Assert.Equal("Orders.Api · dev · shared (azcfg.json)", Item(cut, "Orders.Api").Instance.Text);
        Assert.Contains($"shared via {Manifest}", Item(cut, "Orders.Api").Markup);
        cut.WaitForAssertion(() => Assert.Contains("az-ws-shared", cut.Markup));
        Assert.DoesNotContain(cut.FindAll("fluent-button"), b => b.TextContent.Trim() == "Share with team…");
        Assert.False(ShareEnabled());
    }

    [Fact]
    public void Share_Confirmed_WritesViaServiceRefreshesTreeAndBadgeAppears()
    {
        Seed(Node("Orders.Api", Binding("Orders.Api")));
        var cut = RenderWithOrdersSelected();
        Assert.Equal("Orders.Api · dev", Item(cut, "Orders.Api").Instance.Text);

        Click(cut, "Share with team…");
        Providers.WaitForAssertion(() => Assert.Contains("Share Orders.Api with the team?", Providers.Markup));
        Assert.Contains("never a value", Providers.Markup);
        Click(Providers, "Share");

        cut.WaitForAssertion(() => Assert.Equal("Orders.Api · dev · shared (azcfg.json)", Item(cut, "Orders.Api").Instance.Text));
        Assert.Equal([Orders], WorkspaceService.Shares);
        Assert.Contains(Runner.Runs, r => r.Title == "Share Orders.Api" && r.Kind == OperationKind.Write);
        Assert.Contains(ProjectSettingKeys.SharedHint, Providers.Markup);
    }

    [Fact]
    public void Share_Cancelled_WritesNothing()
    {
        Seed(Node("Orders.Api", Binding("Orders.Api")));
        var cut = RenderWithOrdersSelected();

        Click(cut, "Share with team…");
        Providers.WaitForAssertion(() => Assert.Contains("Share Orders.Api with the team?", Providers.Markup));
        Click(Providers, "Cancel");

        Assert.Empty(WorkspaceService.Shares);
        Assert.Equal("Orders.Api · dev", Item(cut, "Orders.Api").Instance.Text);
    }

    [Fact]
    public async Task Share_AnotherProjectsManifest_ToastsWithoutConfirmAndNoBadge()
    {
        Seed(Node("Orders.Api", Binding("Orders.Api")));
        WorkspaceService.ShareFailure = new InvalidOperationException("already shares another project");
        _ = Providers;
        Workspace.SelectProject(Orders);

        Assert.False(await Services.GetRequiredService<WorkspaceActions>().ShareAsync(Orders));

        Providers.WaitForAssertion(() => Assert.Contains("Could not share Orders.Api: already shares another project", Providers.Markup));
        Assert.DoesNotContain("Share Orders.Api with the team?", Providers.Markup);
        Assert.Empty(WorkspaceService.Shares);
        Assert.False(Workspace.FindProject(Orders)!.IsShared);
    }

    [Fact]
    public void Command_Share_EnabledOnlyForBoundUnsharedActiveProject()
    {
        Seed(Node("Orders.Api", Binding("Orders.Api")));
        Workspace.SelectProject(Orders);
        Assert.True(ShareEnabled());

        Workspace.SelectProject(IdOf("Billing"));
        Assert.False(ShareEnabled());
    }

    [Fact]
    public void Render_ManifestError_ShowsErrorStateInsteadOfShare()
    {
        Seed(Node("Orders.Api", null) with { ManifestError = "Invalid azcfg manifest 'C:/src/Shop/azcfg.json': bad" });
        var cut = RenderWithOrdersSelected();

        Assert.Equal("Orders.Api · unbound · azcfg.json error", Item(cut, "Orders.Api").Instance.Text);
        cut.WaitForAssertion(() => Assert.Contains("Invalid azcfg manifest", cut.Find(".az-ws-error").TextContent));
        Assert.DoesNotContain(cut.FindAll("fluent-button"), b => b.TextContent.Trim() == "Share with team…");
    }

    [Fact]
    public async Task Bind_UnboundProjectBoundFromManifest_NoDialogNoBindRefreshes()
    {
        var fromManifest = Binding("Orders.Api", "orders", null);
        Seed(Node("Orders.Api", null));
        WorkspaceService.Tree = Tree([Solution("Shop", Node("Orders.Api", fromManifest) with { ManifestPath = Manifest }, Node("Billing", null))]);
        WorkspaceService.Proposal = new ProjectSetupProposal(
            Node("Orders.Api").Project.Project, new ProjectResolution([], true, [], fromManifest, Manifest));
        _ = Providers;

        var task = Services.GetRequiredService<IBindingFlow>().BindAsync(Orders);
        Providers.WaitForAssertion(() => Assert.Contains("Pull dev now?", Providers.Markup));
        Assert.DoesNotContain("az-bind", Providers.Markup);
        Click(Providers, "Not now");
        Assert.True(await task);

        Assert.Empty(WorkspaceService.Binds);
        Assert.Equal(fromManifest, Workspace.FindProject(Orders)!.Binding);
        Assert.True(Workspace.FindProject(Orders)!.IsShared);
    }

    [Fact]
    public async Task Rebind_SharedProject_DialogSaysAzcfgJsonChangesAndToastSaysCommitIt()
    {
        var existing = Binding("Orders.Api", "orders", "dev");
        Seed(Node("Orders.Api", existing) with { ManifestPath = Manifest });
        WorkspaceService.Proposal = new ProjectSetupProposal(
            Node("Orders.Api").Project.Project,
            new ProjectResolution(["orders"], false, [new LocalTargetOption(existing.ValuesTarget, true, false), new LocalTargetOption(existing.SecretsTarget, true, true)], existing, Manifest));
        _ = Providers;

        var task = Services.GetRequiredService<IBindingFlow>().BindAsync(Orders);
        Providers.WaitForAssertion(() => Assert.Contains("az-bind-shared", Providers.Markup));
        Assert.Contains(Manifest, Providers.Find(".az-bind-shared").TextContent);
        Assert.Contains("commit it", Providers.Find(".az-bind-shared").TextContent);
        Providers.Find("input[aria-label='Service']").Input("sales");
        Click(Providers, "Save binding");

        Assert.True(await task);
        Providers.WaitForAssertion(() => Assert.Contains(ProjectSettingKeys.SharedHint, Providers.Markup));
        Assert.True(Workspace.FindProject(Orders)!.IsShared);
    }

    [Fact]
    public void Share_ConfirmShowsThePathCoreWillWrite()
    {
        Seed(Node("Orders.Api", Binding("Orders.Api")));
        WorkspaceService.SharePath = $"{Root}/azcfg.json";
        var cut = RenderWithOrdersSelected();

        Click(cut, "Share with team…");
        Providers.WaitForAssertion(() => Assert.Contains($"to {Root}/azcfg.json.", Providers.Markup));
        Assert.Contains(Runner.Runs, r => r.Title == "Prepare sharing of Orders.Api" && r.Kind == OperationKind.Read);
        Click(Providers, "Cancel");
        Assert.Empty(WorkspaceService.Shares);
    }

    [Fact]
    public void Command_Share_DisabledWhenTheManifestIsBroken()
    {
        Seed(Node("Orders.Api", Binding("Orders.Api")) with { ManifestError = "Invalid azcfg manifest" });
        Workspace.SelectProject(Orders);
        Assert.False(ShareEnabled());
    }

    [Fact]
    public void Apply_ManifestChangedService_ConfirmsResetAppliesInvalidatesAndToasts()
    {
        var mine = Binding("Orders.Api", "orders-old", "dev");
        Seed(Node("Orders.Api", mine) with { ManifestPath = Manifest, ManifestChange = new ManifestChange("orders-old", "orders") });
        var applied = mine with { Service = "orders", ActiveEnvironment = null };
        WorkspaceService.ApplyResult = new ProjectResolution([], true, [], applied, Manifest, "orders-old");
        WorkspaceService.Tree = Tree([Solution("Shop", Node("Orders.Api", applied) with { ManifestPath = Manifest }, Node("Billing", null))]);
        var session = Sessions.GetOrCreate(Orders);
        session.SetView(new AzCfg.Ui.Application.Configuration.ConfigurationView(mine, Dev, false, [], [], DateTimeOffset.UnixEpoch));
        var cut = RenderWithOrdersSelected();
        Assert.Equal("Orders.Api · dev · shared (azcfg.json) · azcfg.json changed", Item(cut, "Orders.Api").Instance.Text);

        Click(cut, "Apply azcfg.json…");
        Providers.WaitForAssertion(() => Assert.Contains("the active environment is cleared and the sync snapshots are reset", Providers.Markup));
        Click(Providers, "Apply");

        cut.WaitForAssertion(() => Assert.Equal("Orders.Api · no active env · shared (azcfg.json)", Item(cut, "Orders.Api").Instance.Text));
        Assert.Equal([Orders], WorkspaceService.Applied);
        Assert.Contains(Runner.Runs, r => r.Title == "Apply azcfg.json to Orders.Api" && r.Kind == OperationKind.Write);
        Assert.Null(session.View);
        Providers.WaitForAssertion(() => Assert.Contains("was ‘orders-old’", Providers.Markup));
    }

    [Fact]
    public void Apply_Cancelled_AppliesNothing()
    {
        Seed(Node("Orders.Api", Binding("Orders.Api")) with { ManifestPath = Manifest, ManifestChange = new ManifestChange("orders", "orders") });
        var cut = RenderWithOrdersSelected();

        Click(cut, "Apply azcfg.json…");
        Providers.WaitForAssertion(() => Assert.Contains("The active environment and snapshots stay", Providers.Markup));
        Click(Providers, "Cancel");

        Assert.Empty(WorkspaceService.Applied);
    }

    [Fact]
    public async Task Refresh_ProjectBoundFromManifest_InvalidatesItsSession()
    {
        Seed(Node("Orders.Api", null));
        var session = Sessions.GetOrCreate(Orders);
        session.SetView(new AzCfg.Ui.Application.Configuration.ConfigurationView(Binding("Orders.Api"), Dev, false, [], [], DateTimeOffset.UnixEpoch));
        WorkspaceService.Tree = Tree([Solution("Shop", Node("Orders.Api", Binding("Orders.Api")) with { ManifestPath = Manifest }, Node("Billing", null))]);

        Assert.True(await Services.GetRequiredService<WorkspaceActions>().RefreshAsync());

        Assert.Null(session.View);
    }

    [Fact]
    public async Task Refresh_UnchangedBindingReloaded_KeepsTheSession()
    {
        Seed(Node("Orders.Api", Binding("Orders.Api")));
        var session = Sessions.GetOrCreate(Orders);
        session.SetView(new AzCfg.Ui.Application.Configuration.ConfigurationView(Binding("Orders.Api"), Dev, false, [], [], DateTimeOffset.UnixEpoch));
        WorkspaceService.Tree = Tree([Solution("Shop", Node("Orders.Api", Binding("Orders.Api")), Node("Billing", null))]);

        Assert.True(await Services.GetRequiredService<WorkspaceActions>().RefreshAsync());

        Assert.NotNull(session.View);
    }
}
