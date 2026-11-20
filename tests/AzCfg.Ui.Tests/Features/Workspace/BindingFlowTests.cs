using AzCfg.Core.Model;
using AzCfg.Core.UseCases;
using AzCfg.Ui.Application.Operations;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Application.Workspace;
using AzCfg.Ui.Features.Workspace;
using AzCfg.Ui.Shared;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using static AzCfg.Ui.Tests.Features.Workspace.ShellFixtures;

namespace AzCfg.Ui.Tests.Features.Workspace;

public sealed class BindingFlowTests : ShellContext
{
    private static readonly string Billing = IdOf("Billing");
    private static readonly string Orders = IdOf("Orders.Api");

    private IBindingFlow Flow => Services.GetRequiredService<IBindingFlow>();

    private static LocalTargetOption Option(string project, LocalTargetKind kind, bool safe, bool exists = true) =>
        new(Target(project, kind), exists, safe);

    private void Seed(ProjectBinding? billing, bool confident = true, bool ordersBound = true)
    {
        var billingNode = Node("Billing", billing);
        SeedTree(Tree([Solution("Shop", billingNode, Node("Orders.Api", ordersBound ? Binding("Orders.Api") : null))]));
        SeedEnvironments();
        WorkspaceService.Proposal = new ProjectSetupProposal(
            billingNode.Project.Project,
            new ProjectResolution(
                ["billing", "orders"],
                confident,
                [Option("Billing", LocalTargetKind.AppSettingsLocal, false, false), Option("Billing", LocalTargetKind.UserSecrets, true)],
                billing));
    }

    private Task<bool> Start()
    {
        _ = Providers;
        var task = Flow.BindAsync(Billing);
        Providers.WaitForAssertion(() => Assert.Contains("az-bind", Providers.Markup));
        return task;
    }

    private static string[] Labels(IRenderedComponent<Microsoft.FluentUI.AspNetCore.Components.FluentProviders> providers, string group) =>
        [.. providers.FindAll($"input[name='{group}']").Select(i => i.ParentElement!.TextContent.Trim())];

    [Fact]
    public void Resolve_IBindingFlow_IsTheWorkspaceFeatureFlow() => Assert.IsType<BindingFlow>(Flow);

    [Fact]
    public async Task Bind_UnsafeTarget_IsNotOfferedForSecretsAndTheReasonIsShown()
    {
        Seed(null);
        var task = Start();
        var secrets = Providers.FindAll("input[name='az-bind-secrets']");
        Assert.Equal([true, false], secrets.Select(i => i.HasAttribute("disabled")));
        Assert.Contains("not offered for secrets", Providers.Markup);
        Assert.Contains("appsettings.local.json (new file)", Labels(Providers, "az-bind-values"));
        Click(Providers, "Cancel");
        Assert.False(await task);
    }

    [Fact]
    public async Task Bind_FirstBind_PassesChoicesBindsUpdatesTreeAndOffersPull()
    {
        Seed(null);
        var task = Start();
        Providers.Find("input[aria-label='Service']").Input("billing");
        Click(Providers, "Bind");
        Providers.WaitForAssertion(() => Assert.Contains("Pull dev now?", Providers.Markup));
        Click(Providers, "Pull");
        Assert.True(await task);
        var (service, values, secrets) = Assert.Single(WorkspaceService.Binds);
        Assert.Equal("billing", service);
        Assert.Equal(LocalTargetKind.AppSettingsLocal, values.Kind);
        Assert.Equal(LocalTargetKind.UserSecrets, secrets.Kind);
        Assert.Equal("billing", Workspace.FindProject(Billing)!.Binding!.Service);
        Assert.Equal([(Billing, "dev")], Activation.Calls);
        Assert.Contains(Runner.Runs, r => r.Title == "Bind Billing" && r.Kind == OperationKind.Write);
    }

    [Fact]
    public async Task Bind_PullDeclined_DoesNotActivate()
    {
        Seed(null);
        var task = Start();
        Providers.Find("input[aria-label='Service']").Input("billing");
        Click(Providers, "Bind");
        Providers.WaitForAssertion(() => Assert.Contains("Pull dev now?", Providers.Markup));
        Click(Providers, "Not now");
        Assert.True(await task);
        Assert.Empty(Activation.Calls);
    }

    [Fact]
    public async Task Bind_ServiceBlank_BindIsDisabled()
    {
        Seed(null, confident: false);
        var task = Start();
        Assert.Equal(string.Empty, Providers.Find("input[aria-label='Service']").GetAttribute("value") ?? string.Empty);
        Assert.True(ButtonText(Providers, "Bind").HasAttribute("disabled"));
        Click(Providers, "Cancel");
        Assert.False(await task);
    }

    [Fact]
    public async Task Bind_ConfidentCandidate_IsPreselectedAndOthersAreSuggested()
    {
        Seed(null);
        var task = Start();
        Assert.Equal("billing", Providers.Find("input[aria-label='Service']").GetAttribute("value"));
        Assert.Equal(["billing", "orders"], Providers.FindAll("#az-bind-services option").Select(o => o.GetAttribute("value")));
        Click(Providers, "Cancel");
        await task;
    }

    [Fact]
    public async Task Bind_Cancelled_BindsNothing()
    {
        Seed(null);
        var task = Start();
        Click(Providers, "Cancel");
        Assert.False(await task);
        Assert.Empty(WorkspaceService.Binds);
        Assert.Null(Workspace.FindProject(Billing)!.Binding);
    }

    [Fact]
    public async Task Rebind_KeepsActiveEnvironmentWhenCoreKeepsIt_NoPullOffer()
    {
        var existing = Binding("Billing", "billing", "stage", LocalTargetKind.AppSettingsLocal, LocalTargetKind.UserSecrets);
        Seed(existing);
        var task = Start();
        Assert.Contains("Vaults and local ignore patterns are kept", Providers.Markup);
        Assert.Equal("billing", Providers.Find("input[aria-label='Service']").GetAttribute("value"));
        Click(Providers, "Save binding");
        Assert.True(await task);
        Assert.Equal("stage", Workspace.FindProject(Billing)!.Binding!.ActiveEnvironment);
        Assert.Empty(Activation.Calls);
    }

    [Fact]
    public async Task Rebind_NewService_ActiveEnvironmentIsClearedAndPullIsOffered()
    {
        var existing = Binding("Billing", "billing", "stage", LocalTargetKind.AppSettingsLocal, LocalTargetKind.UserSecrets);
        Seed(existing);
        WorkspaceService.BindResult = (node, service) => node.Binding! with { Service = service, ActiveEnvironment = null };
        var task = Start();
        Providers.Find("input[aria-label='Service']").Input("invoices");
        Click(Providers, "Save binding");
        Providers.WaitForAssertion(() => Assert.Contains("Pull dev now?", Providers.Markup));
        Click(Providers, "Pull");
        Assert.True(await task);
        Assert.Null(Workspace.FindProject(Billing)!.Binding!.ActiveEnvironment);
        Assert.Equal("invoices", Workspace.FindProject(Billing)!.Binding!.Service);
        Assert.Equal([(Billing, "dev")], Activation.Calls);
    }

    [Fact]
    public async Task Bind_Succeeds_InvalidatesTheProjectSession()
    {
        Seed(null);
        var session = Sessions.GetOrCreate(Billing);
        session.SetView(new AzCfg.Ui.Application.Configuration.ConfigurationView(Binding("Billing"), Dev, false, [], [], DateTimeOffset.UnixEpoch));
        var task = Start();
        Click(Providers, "Bind");
        Providers.WaitForAssertion(() => Assert.Contains("Pull dev now?", Providers.Markup));
        Click(Providers, "Not now");
        await task;
        Assert.Null(session.View);
    }

    [Fact]
    public async Task Bind_ProposalFails_ReturnsFalseWithoutADialog()
    {
        Seed(null);
        WorkspaceService.Proposal = null;
        _ = Providers;
        Assert.False(await Flow.BindAsync(Billing));
        Assert.DoesNotContain("az-bind", Providers.Markup);
    }

    [Fact]
    public async Task Bind_UnknownProject_ReturnsFalse()
    {
        Seed(null);
        _ = Providers;
        Assert.False(await Flow.BindAsync(IdOf("Missing")));
    }
}
