using AzCfg.Core.Model;
using AzCfg.Ui.Application.Configuration;
using AzCfg.Ui.Application.Environments;
using AzCfg.Ui.Application.Operations;
using AzCfg.Ui.Features.Environments;
using AzCfg.Ui.Tests.Features.Workspace;
using Bunit;
using static AzCfg.Ui.Tests.Features.Workspace.ShellFixtures;

namespace AzCfg.Ui.Tests.Features.Environments;

public sealed class EnvironmentSetupWizardTests : ShellContext
{
    private static EnvironmentCandidate Candidate(string? name, string store, bool isProtected = false) =>
        new(name, new AzureResource(store, $"appcs-{store}", new Uri($"https://{store}.azconfig.io"), "sub", "rg", new Dictionary<string, string>()), isProtected);

    private IRenderedComponent<EnvironmentSetupWizard> RenderFirstRun(params EnvironmentCandidate[] candidates)
    {
        _ = Providers;
        EnvService.Snapshot = new EnvironmentsSnapshot(null, []);
        EnvService.Candidates = candidates;
        return Render<EnvironmentSetupWizard>();
    }

    private static void Next(IRenderedComponent<EnvironmentSetupWizard> cut) =>
        cut.Find(".fluent-wizard-buttons fluent-button[appearance=primary]").Click();

    private static string[] RowNames(IRenderedComponent<EnvironmentSetupWizard> cut) =>
        [.. cut.FindAll("tr.az-env-row input[aria-label='Name']").Select(i => i.GetAttribute("value") ?? string.Empty)];

    [Fact]
    public void Render_FirstStep_OffersTheTwoModesAndStoreModeIsDefault()
    {
        var cut = RenderFirstRun();
        var radios = cut.FindAll("input[name='az-env-mode']");
        Assert.Equal([true, false], radios.Select(r => r.HasAttribute("checked")));
        Assert.Contains("One store, a label per environment", cut.Markup);
    }

    [Fact]
    public void Render_Discovery_ListsStoresWithGuessedNamesTickedOnlyWhenGuessed()
    {
        var cut = RenderFirstRun(Candidate("dev", "dev"), Candidate("prod", "prod", true), Candidate(null, "sandbox"));
        Assert.Equal(["dev", "prod", "appcs-sandbox"], RowNames(cut));
        Assert.Equal([true, true, false], cut.FindAll("input[aria-label^='Use ']").Select(i => i.HasAttribute("checked")));
        Assert.True(cut.Find("input[aria-label='Protected prod']").HasAttribute("checked"));
        Assert.False(cut.Find("input[aria-label='Protected dev']").HasAttribute("checked"));
        Assert.Contains(Runner.Runs, r => r.Title == "Discover App Configuration stores" && r.Kind == OperationKind.Read);
    }

    [Fact]
    public void Render_DiscoveryFails_ShowsTheErrorAndManualAddStillWorks()
    {
        EnvService.DiscoverFailure = new InvalidOperationException("No access to Resource Graph");
        var cut = RenderFirstRun();
        Assert.Contains("No access to Resource Graph", cut.Markup);
        Assert.Contains("You can still add stores manually", cut.Markup);
        ButtonText(cut, "Add manually").Click();
        Assert.Single(cut.FindAll("tr.az-env-row"));
    }

    [Fact]
    public void Next_TickedRowWithoutName_StaysAndShowsTheValidationInline()
    {
        var cut = RenderFirstRun();
        ButtonText(cut, "Add manually").Click();
        cut.Find("input[aria-label='Endpoint']").Input("https://x.azconfig.io");
        Next(cut);
        Next(cut);
        Assert.Contains("Name is required", cut.Markup);
        Assert.Empty(EnvService.Saved);
    }

    [Fact]
    public void Done_SavesTheTickedStoresInStoreModeAndPublishesTheSnapshot()
    {
        var session = Sessions.GetOrCreate(IdOf("Orders.Api"));
        session.SetView(new ConfigurationView(Binding("Orders.Api"), Dev, false, [], [], DateTimeOffset.UnixEpoch));
        var cut = RenderFirstRun(Candidate("dev", "dev"), Candidate("prod", "prod", true), Candidate(null, "sandbox"));
        Next(cut);
        Next(cut);
        cut.Find("input[aria-label='Default label']").Input("main");
        Next(cut);
        cut.WaitForAssertion(() => Assert.Single(EnvService.Saved));
        var profile = EnvService.Saved.Single();
        Assert.Equal(EnvironmentMode.StorePerEnvironment, profile.Mode);
        Assert.Equal("main", profile.DefaultLabel);
        Assert.Equal([("dev", false), ("prod", true)], profile.Environments.Select(e => (e.Name, e.IsProtected)));
        Assert.Equal(["dev", "prod"], Workspace.Environments!.Environments.Select(e => e.Name));
        Assert.Null(session.View);
        Assert.Contains(Runner.Runs, r => r.Title == "Save environments" && r.Kind == OperationKind.Write);
    }

    [Fact]
    public void LabelMode_LoadsLabelsOfTheSharedStoreAndSavesThemAsEnvironments()
    {
        EnvService.Labels = ["Development", "Production"];
        var cut = RenderFirstRun(Candidate("dev", "shared"));
        cut.FindAll("input[name='az-env-mode']")[1].Change(true);
        cut.Find("input[aria-label='Shared store']").Input("https://shared.azconfig.io");
        Click(cut, "Load labels");
        cut.WaitForAssertion(() => Assert.Equal(["dev", "prod"], RowNames(cut)));
        Assert.True(cut.Find("input[aria-label='Protected prod']").HasAttribute("checked"));
        Next(cut);
        Next(cut);
        Next(cut);
        cut.WaitForAssertion(() => Assert.Single(EnvService.Saved));
        var profile = EnvService.Saved.Single();
        Assert.Equal(EnvironmentMode.LabelPerEnvironment, profile.Mode);
        Assert.Equal([("dev", "Development"), ("prod", "Production")], profile.Environments.Select(e => (e.Name, e.Label)));
        Assert.All(profile.Environments, e => Assert.Equal(new Uri("https://shared.azconfig.io"), e.StoreEndpoint));
    }

    [Fact]
    public void LabelMode_StoresOfDifferentEndpoints_ReportTheOneStoreRule()
    {
        var cut = RenderFirstRun();
        cut.FindAll("input[name='az-env-mode']")[1].Change(true);
        ButtonText(cut, "Add manually").Click();
        ButtonText(cut, "Add manually").Click();
        cut.FindAll("input[aria-label='Endpoint']")[0].Input("https://a.azconfig.io");
        cut.FindAll("input[aria-label='Endpoint']")[1].Input("https://b.azconfig.io");
        cut.FindAll("input[aria-label='Name']")[0].Input("dev");
        cut.FindAll("input[aria-label='Name']")[1].Input("prod");
        Next(cut);
        Next(cut);
        Assert.Contains("Label mode uses one shared store", cut.Markup);
    }

    [Fact]
    public void Done_SaveFails_ShowsTheErrorInTheReviewStep()
    {
        EnvService.SaveFailure = new ArgumentException("Duplicate environment name 'dev'.");
        var cut = RenderFirstRun(Candidate("dev", "dev"));
        Next(cut);
        Next(cut);
        Next(cut);
        cut.WaitForAssertion(() => Assert.Contains("Duplicate environment name", cut.Markup));
        Assert.Empty(EnvService.Saved);
    }

    [Fact]
    public void Render_ExistingProfile_WarnsThatSavingReplacesIt()
    {
        SeedEnvironments();
        var cut = Render<EnvironmentSetupWizard>();
        Assert.Contains("Saving replaces the environments that are configured now", cut.Markup);
    }

    [Fact]
    public void Rename_NewRow_FollowsTheProtectedNameRuleUntilTouched()
    {
        var cut = RenderFirstRun();
        ButtonText(cut, "Add manually").Click();
        cut.Find("input[aria-label='Name']").Input("prod");
        Assert.True(cut.Find("input[aria-label^='Protected']").HasAttribute("checked"));
        cut.Find("input[aria-label^='Protected']").Change(false);
        cut.Find("input[aria-label='Name']").Input("production");
        Assert.False(cut.Find("input[aria-label^='Protected']").HasAttribute("checked"));
    }
}
