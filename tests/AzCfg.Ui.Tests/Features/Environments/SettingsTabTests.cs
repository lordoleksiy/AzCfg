using AzCfg.Core.Model;
using AzCfg.Core.UseCases;
using AzCfg.Ui.Application.Configuration;
using AzCfg.Ui.Application.Environments;
using AzCfg.Ui.Application.Operations;
using AzCfg.Ui.Application.State;
using AzCfg.Ui.Features.Environments;
using AzCfg.Ui.Shared;
using AzCfg.Ui.Tests.Features.Workspace;
using AzCfg.Ui.Tests.Features.Workspace.Fakes;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using static AzCfg.Ui.Tests.Features.Workspace.ShellFixtures;

namespace AzCfg.Ui.Tests.Features.Environments;

public sealed class SettingsTabTests : ShellContext
{
    private static readonly WorkbenchTab Tab = new(Guid.NewGuid(), new TabDescriptor(TabKind.Settings), "Settings", false, true);
    private static readonly string Orders = IdOf("Orders.Api");
    private readonly FakeBindingFlow _binding = new();

    public SettingsTabTests() => Services.AddScoped<IBindingFlow>(_ => _binding);

    private IRenderedComponent<SettingsTab> RenderTab()
    {
        _ = Providers;
        return Render<SettingsTab>(p => p.Add(t => t.Tab, Tab));
    }

    private static IReadOnlyList<AngleSharp.Dom.IElement> Rows(IRenderedComponent<SettingsTab> cut) => cut.FindAll("tr.az-env-row");

    private static AngleSharp.Dom.IElement Field(IRenderedComponent<SettingsTab> cut, string env, string label) =>
        cut.Find($"tr.az-env-row[data-env='{env}'] input[aria-label='{label}']");

    private static void AddRow(IRenderedComponent<SettingsTab> cut, string name, string endpoint)
    {
        ButtonTextOf(cut, "Add environment").Click();
        cut.FindAll("tr.az-env-row").Last().QuerySelector("input[aria-label='Name']")!.Input(name);
        cut.FindAll("tr.az-env-row").Last().QuerySelector("input[aria-label='Endpoint']")!.Input(endpoint);
    }

    private static AngleSharp.Dom.IElement ButtonTextOf(IRenderedComponent<SettingsTab> cut, string text) =>
        cut.FindAll("fluent-button").First(b => b.TextContent.Trim() == text);

    [Fact]
    public void Render_ShowsTheSavedEnvironmentsAndNoRename()
    {
        SeedEnvironments();
        var cut = RenderTab();
        Assert.Equal(["dev", "prod"], Rows(cut).Select(r => r.GetAttribute("data-env")));
        Assert.True(Field(cut, "dev", "Name").HasAttribute("disabled"));
        Assert.Contains("cannot be renamed", Field(cut, "dev", "Name").GetAttribute("title"));
        Assert.Equal("https://dev.azconfig.io/", Field(cut, "dev", "Endpoint").GetAttribute("value"));
        Assert.Contains("protected", Rows(cut)[1].ClassList);
        Assert.True(cut.Find("input[aria-label='Protected prod']").HasAttribute("checked"));
    }

    [Fact]
    public void Render_NotLoaded_LoadsTheProfile()
    {
        var cut = RenderTab();
        Assert.Equal(1, EnvService.Loads);
        cut.WaitForAssertion(() => Assert.Equal(2, Rows(cut).Count));
    }

    [Fact]
    public void Save_NewValidEnvironment_SavesTheWholeProfileAndInvalidatesTheSessions()
    {
        SeedEnvironments();
        var session = Sessions.GetOrCreate(Orders);
        session.SetView(new ConfigurationView(Binding("Orders.Api"), Dev, false, [], [], DateTimeOffset.UnixEpoch));
        var cut = RenderTab();
        AddRow(cut, "qa", "https://qa.azconfig.io");
        Click(cut, "Save");
        cut.WaitForAssertion(() => Assert.Single(EnvService.Saved));
        Assert.Equal(["dev", "prod", "qa"], EnvService.Saved[0].Environments.Select(e => e.Name));
        Assert.Equal(["dev", "prod", "qa"], Workspace.Environments!.Environments.Select(e => e.Name));
        Assert.Null(session.View);
        cut.WaitForAssertion(() => Assert.Equal(3, Rows(cut).Count));
        Assert.Contains(Runner.Runs, r => r.Title == "Save environments" && r.Kind == OperationKind.Write);
    }

    [Fact]
    public void Save_InvalidRows_ShowInlineErrorsAndSaveNothing()
    {
        SeedEnvironments();
        var cut = RenderTab();
        ButtonTextOf(cut, "Add environment").Click();
        Click(cut, "Save");
        cut.WaitForAssertion(() => Assert.Contains("Name is required", cut.Markup));
        Assert.Contains("Endpoint must be an absolute https URL", cut.Markup);
        Assert.Empty(EnvService.Saved);
    }

    [Fact]
    public void Save_DuplicateName_IsReportedOnTheRow()
    {
        SeedEnvironments();
        var cut = RenderTab();
        AddRow(cut, "DEV", "https://other.azconfig.io");
        Click(cut, "Save");
        cut.WaitForAssertion(() => Assert.Contains("Duplicate name", cut.Markup));
        Assert.Empty(EnvService.Saved);
    }

    [Fact]
    public void Save_EditedEndpointLabelAndProtected_AreSaved()
    {
        SeedEnvironments();
        var cut = RenderTab();
        Field(cut, "dev", "Endpoint").Input("https://dev2.azconfig.io");
        Field(cut, "dev", "Label").Input("main");
        cut.Find("input[aria-label='Protected dev']").Change(true);
        Click(cut, "Save");
        cut.WaitForAssertion(() => Assert.Single(EnvService.Saved));
        var dev = EnvService.Saved[0].Environments.Single(e => e.Name == "dev");
        Assert.Equal((new Uri("https://dev2.azconfig.io"), "main", true), (dev.StoreEndpoint, dev.Label, dev.IsProtected));
    }

    [Fact]
    public void Save_ModeDefaultLabelAndGitIgnore_AreSaved()
    {
        SeedEnvironments();
        var cut = RenderTab();
        cut.Find("input[aria-label='Default label']").Input("main");
        cut.Find("input[aria-label='Git-ignore local files']").Change(true);
        Click(cut, "Save");
        cut.WaitForAssertion(() => Assert.Single(EnvService.Saved));
        var profile = EnvService.Saved[0];
        Assert.Equal(("main", true, EnvironmentMode.StorePerEnvironment), (profile.DefaultLabel, profile.GitIgnoreLocalFiles, profile.Mode));
    }

    [Fact]
    public void Mode_Label_HidesTheDefaultLabelAndIsSaved()
    {
        var shared = new ConfigEnvironment("dev", new Uri("https://shared.azconfig.io"), "Development", false);
        EnvService.Snapshot = new EnvironmentsSnapshot(new UserProfile([shared]), [shared]);
        SeedEnvironments();
        var cut = RenderTab();
        cut.Find("select[aria-label='Mode']").Change(nameof(EnvironmentMode.LabelPerEnvironment));
        Assert.Empty(cut.FindAll("input[aria-label='Default label']"));
        Click(cut, "Save");
        cut.WaitForAssertion(() => Assert.Equal(EnvironmentMode.LabelPerEnvironment, EnvService.Saved.Single().Mode));
    }

    [Fact]
    public void Remove_Environment_IsSavedWithoutIt()
    {
        SeedEnvironments();
        var cut = RenderTab();
        ButtonTitled(cut, "Remove prod").Click();
        Assert.Single(Rows(cut));
        Click(cut, "Save");
        cut.WaitForAssertion(() => Assert.Equal(["dev"], EnvService.Saved.Single().Environments.Select(e => e.Name)));
    }

    [Fact]
    public void Remove_EnvironmentActiveInAnOpenProject_AsksFirstAndCancelSavesNothing()
    {
        SeedTree(Tree([Solution("Shop", Node("Orders.Api", Binding("Orders.Api", active: "prod")))]));
        SeedEnvironments();
        var cut = RenderTab();
        ButtonTitled(cut, "Remove prod").Click();
        Click(cut, "Save");
        Providers.WaitForAssertion(() => Assert.Contains("Orders.Api (prod)", Providers.Markup));
        Click(Providers, "Cancel");
        Assert.Empty(EnvService.Saved);
    }

    [Fact]
    public void Remove_EnvironmentActiveInAnOpenProject_ConfirmedSaves()
    {
        SeedTree(Tree([Solution("Shop", Node("Orders.Api", Binding("Orders.Api", active: "prod")))]));
        SeedEnvironments();
        var cut = RenderTab();
        ButtonTitled(cut, "Remove prod").Click();
        Click(cut, "Save");
        Providers.WaitForAssertion(() => Assert.Contains("make another environment active", Providers.Markup));
        Click(Providers, "Remove");
        cut.WaitForAssertion(() => Assert.Equal(["dev"], EnvService.Saved.Single().Environments.Select(e => e.Name)));
    }

    [Fact]
    public void Remove_EnvironmentNobodyUses_DoesNotAsk()
    {
        SeedTree(Tree([Solution("Shop", Node("Orders.Api", Binding("Orders.Api", active: "dev")))]));
        SeedEnvironments();
        var cut = RenderTab();
        ButtonTitled(cut, "Remove prod").Click();
        Click(cut, "Save");
        cut.WaitForAssertion(() => Assert.Single(EnvService.Saved));
        Assert.DoesNotContain("Remove active environment", Providers.Markup);
    }

    [Fact]
    public void Discard_RestoresTheSavedState()
    {
        SeedEnvironments();
        var cut = RenderTab();
        ButtonTitled(cut, "Remove prod").Click();
        Click(cut, "Discard changes");
        Assert.Equal(2, Rows(cut).Count);
        Assert.Empty(EnvService.Saved);
    }

    [Fact]
    public void Save_Fails_ShowsTheError()
    {
        SeedEnvironments();
        EnvService.SaveFailure = new UnauthorizedAccessException("The profile is read-only");
        var cut = RenderTab();
        Click(cut, "Save");
        cut.WaitForAssertion(() => Assert.Contains("The profile is read-only", cut.Markup));
    }

    [Fact]
    public void Wizard_ButtonRunsTheSetupCommand()
    {
        SeedEnvironments();
        var ran = false;
        Commands.Register(new UiCommand(CommandIds.EnvSetup, "Set up", "Env", null, () => { ran = true; return Task.CompletedTask; }));
        Click(RenderTab(), "Set up with wizard…");
        Assert.True(ran);
    }

    [Fact]
    public void ProjectSettings_NoSelection_ShowsAHint()
    {
        SeedEnvironments();
        Assert.Contains("Select a bound project", RenderTab().Markup);
    }

    [Fact]
    public void ProjectSettings_BoundSelectedProject_ListsTheCoreSettingsThroughARead()
    {
        SeedTree(Tree([Solution("Shop", Node("Orders.Api", Binding("Orders.Api")))]));
        SeedEnvironments();
        Workspace.SelectProject(Orders);
        ProjectSettings.Settings = [new UserSetting("project.service", "orders"), new UserSetting("project.vault.dev", null)];
        var cut = RenderTab();
        Assert.Equal(["project.service", "project.vault.dev"], cut.FindAll(".az-settings-table th").Select(t => t.TextContent));
        Assert.Equal(["orders", "—"], cut.FindAll(".az-settings-table td").Select(t => t.TextContent));
        Assert.Equal([$"{Root}/Orders.Api"], ProjectSettings.Listed);
        Assert.Contains(("Read project settings", OperationKind.Read), Runner.Runs);
        Assert.Contains("Orders.Api", cut.Find(".az-settings-section[aria-label='Project settings'] h3").TextContent);
    }

    [Fact]
    public void ProjectSettings_EditBinding_RunsTheBindingFlowAndReloads()
    {
        SeedTree(Tree([Solution("Shop", Node("Orders.Api", Binding("Orders.Api")))]));
        SeedEnvironments();
        Workspace.SelectProject(Orders);
        var cut = RenderTab();
        Click(cut, "Edit binding…");
        Assert.Equal([Orders], _binding.Calls);
        cut.WaitForAssertion(() => Assert.Equal(2, ProjectSettings.Listed.Count));
    }

    [Fact]
    public void ProjectSettings_SelectionChange_ReloadsForTheNewProject()
    {
        SeedTree(Tree([Solution("Shop", Node("Orders.Api", Binding("Orders.Api")), Node("Billing", Binding("Billing", "billing")))]));
        SeedEnvironments();
        Workspace.SelectProject(Orders);
        var cut = RenderTab();
        cut.InvokeAsync(() => Workspace.SelectProject(IdOf("Billing")));
        cut.WaitForAssertion(() => Assert.Equal([$"{Root}/Orders.Api", $"{Root}/Billing"], ProjectSettings.Listed));
    }

    [Fact]
    public void WizardSave_WhileTabIsOpenAndUntouched_RefreshesTheRows()
    {
        SeedEnvironments();
        var cut = RenderTab();
        var profile = Profile(Dev);
        cut.InvokeAsync(() => Workspace.SetEnvironments(new EnvironmentsSnapshot(profile, [Dev])));
        cut.WaitForAssertion(() => Assert.Single(Rows(cut)));
    }
}
