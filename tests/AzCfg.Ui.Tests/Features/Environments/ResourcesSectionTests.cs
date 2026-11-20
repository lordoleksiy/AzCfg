using AzCfg.Core.Model;
using AzCfg.Ui.Application.Environments;
using AzCfg.Ui.Application.Operations;
using AzCfg.Ui.Features.Environments;
using AzCfg.Ui.Tests.Features.Workspace;
using Bunit;
using static AzCfg.Ui.Tests.Features.Workspace.ShellFixtures;

namespace AzCfg.Ui.Tests.Features.Environments;

public sealed class ResourcesSectionTests : ShellContext
{
    private static readonly string Orders = IdOf("Orders.Api");

    private static AzureResource Vault(string name) =>
        new($"/vaults/{name}", name, new Uri($"https://{name}.vault.azure.net/"), "sub", "rg-shop", new Dictionary<string, string>());

    private void SeedProject(ProjectBinding? binding)
    {
        SeedTree(Tree([Solution("Shop", Node("Orders.Api", binding))]));
        SeedEnvironments();
        Workspace.SelectProject(Orders);
    }

    [Fact]
    public void Render_ShowsTheStoreHostOfEveryEnvironment()
    {
        SeedEnvironments();
        var cut = Render<ResourcesSection>();
        Assert.Equal(["dev.azconfig.io", "prod.azconfig.io"], cut.FindAll(".az-res-env .az-res-row:first-child .az-res-host").Select(h => h.TextContent));
    }

    [Fact]
    public void Render_NoBoundProject_ExplainsWhereTheVaultComesFrom()
    {
        SeedEnvironments();
        Assert.Contains("Select a bound project", Render<ResourcesSection>().Markup);
    }

    [Fact]
    public void Render_BoundProject_ShowsTheVaultOrChoose()
    {
        var binding = Binding("Orders.Api") with { VaultByEnvironment = new Dictionary<string, Uri> { ["dev"] = new("https://kv-shop-dev.vault.azure.net/") } };
        SeedProject(binding);
        var cut = Render<ResourcesSection>();
        Assert.Equal("kv-shop-dev.vault.azure.net", cut.Find(".az-res-env[data-env='dev'] .az-res-vault .az-res-host").TextContent);
        Assert.Contains("not set —", cut.Find(".az-res-env[data-env='prod'] .az-res-vault").TextContent);
        Assert.Contains("Choose…", cut.Find(".az-res-env[data-env='prod'] .az-res-vault").TextContent);
    }

    [Fact]
    public void Choose_Vault_SetsItThroughAWriteAndUpdatesTheBinding()
    {
        Sync.Vaults = [Vault("kv-shop-dev"), Vault("kv-other")];
        SeedProject(Binding("Orders.Api"));
        var providers = Providers;
        var cut = Render<ResourcesSection>();
        cut.Find(".az-res-env[data-env='dev'] .az-res-vault .az-link").Click();
        providers.WaitForAssertion(() => Assert.Equal(["kv-shop-dev", "kv-other"], providers.FindAll(".az-vault-chooser-item .az-mono").Select(e => e.TextContent)));
        providers.Find("input[aria-label='Search vaults']").Input("shop");
        providers.WaitForAssertion(() => Assert.Single(providers.FindAll(".az-vault-chooser-item")));
        providers.Find(".az-vault-chooser-item").Click();
        cut.WaitForAssertion(() => Assert.Equal("kv-shop-dev.vault.azure.net", cut.Find(".az-res-env[data-env='dev'] .az-res-vault .az-res-host").TextContent));
        Assert.Equal([("dev", (Uri?)new Uri("https://kv-shop-dev.vault.azure.net/"))], WorkspaceService.Vaults);
        Assert.Contains(Runner.Runs, r => r.Title == "Set the Key Vault of dev" && r.Kind == OperationKind.Write);
        Assert.True(Workspace.FindProject(Orders)!.Binding!.VaultByEnvironment.ContainsKey("dev"));
    }

    [Fact]
    public void Clear_Vault_RemovesIt()
    {
        var binding = Binding("Orders.Api") with { VaultByEnvironment = new Dictionary<string, Uri> { ["dev"] = new("https://kv-shop-dev.vault.azure.net/") } };
        SeedProject(binding);
        var cut = Render<ResourcesSection>();
        cut.FindAll(".az-res-env[data-env='dev'] .az-res-vault .az-link").First(b => b.TextContent == "Clear").Click();
        cut.WaitForAssertion(() => Assert.Contains("Choose…", cut.Find(".az-res-env[data-env='dev'] .az-res-vault").TextContent));
        Assert.Equal([("dev", (Uri?)null)], WorkspaceService.Vaults);
    }

    [Fact]
    public void Discover_ListsStoresAndVaultsWithCopyButtons()
    {
        EnvService.Candidates = [new EnvironmentCandidate("dev", new AzureResource("1", "appcs-shop-dev", new Uri("https://appcs-shop-dev.azconfig.io"), "sub", "rg", new Dictionary<string, string>()), false)];
        Sync.Vaults = [Vault("kv-shop-dev")];
        var providers = Providers;
        SeedEnvironments();
        var cut = Render<ResourcesSection>();
        Click(cut, "Discover in Azure…");
        providers.WaitForAssertion(() => Assert.Contains("appcs-shop-dev", providers.Markup));
        Assert.Contains("kv-shop-dev", providers.Markup);
        Assert.Contains("Copy endpoint of appcs-shop-dev", providers.Markup);
        Assert.Contains("Copy URI of kv-shop-dev", providers.Markup);
        Assert.Contains(("Discover App Configuration stores", OperationKind.Read), Runner.Runs);
    }

    [Fact]
    public void Discover_Fails_ShowsTheError()
    {
        EnvService.DiscoverFailure = new InvalidOperationException("Resource Graph is not reachable");
        var providers = Providers;
        SeedEnvironments();
        Click(Render<ResourcesSection>(), "Discover in Azure…");
        providers.WaitForAssertion(() => Assert.Contains("Resource Graph is not reachable", providers.Markup));
    }

    [Fact]
    public void Render_NoEnvironments_SaysSo()
    {
        EnvService.Snapshot = new EnvironmentsSnapshot(null, []);
        Workspace.SetEnvironments(EnvService.Snapshot);
        Assert.Contains("No environments yet", Render<ResourcesSection>().Markup);
    }
}
